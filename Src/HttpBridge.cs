using System.Net.Sockets;
using System.Threading;
using System.Net.Http;
using System.Text;
using System.Net;
using System;
using System.Collections.Specialized;

using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

class HttpBridge : IDisposable{

	private static readonly Rssdp.Infrastructure.HttpResponseParser httpResponseParser = new Rssdp.Infrastructure.HttpResponseParser();
	private static readonly Rssdp.Infrastructure.HttpRequestParser httpRequestParser = new Rssdp.Infrastructure.HttpRequestParser();

	private HttpListener listener = new HttpListener();

	private HttpClient client = new HttpClient(new SocketsHttpHandler(){

		PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
		PooledConnectionLifetime = TimeSpan.MaxValue,
		ConnectTimeout = TimeSpan.FromSeconds(20),
		MaxConnectionsPerServer = 65366,
		AllowAutoRedirect = false
	});

	public int Port { get; }

	private static int GetFreeTcpPort(){

		TcpListener tmp = new TcpListener(IPAddress.Loopback,0);
		tmp.Start();
		int port = (tmp.LocalEndpoint as IPEndPoint).Port;
		tmp.Stop();
		return port;
	}

	public HttpBridge( Action<HttpListenerContext> onHttpListenerContextGot, UInt16 listenPort = 0){

		this.Port = listenPort != 0 ? listenPort : GetFreeTcpPort();
		this.client.DefaultRequestHeaders.ConnectionClose = false;

		this.listener.Prefixes.Add("http://*:" + this.Port + "/");
		this.listener.Start();

		ThreadStart listenerDaemonCoroutine = () => {

			try{

				while( this.listener.IsListening ){

					// Blocks waiting for a request
					HttpListenerContext request = this.listener.GetContext();

					// Start a new worker coroutine
					var worker = new Thread(() => onHttpListenerContextGot(request));
					worker.IsBackground = true;
					worker.Start();
				}

			}catch( Exception e){

				Console.WriteLine("On listenerDaemonCoroutine: " + e.ToString());
			}
		};

		// Starts the daemon coroutine
		var daemon = new Thread(listenerDaemonCoroutine);
		daemon.IsBackground = true;
		daemon.Start();
	}

	public void Dispose(){

		if( this.listener.IsListening )
			this.listener.Close();

		this.client.Dispose();
	}

// ################################
// ######### HTTP request #########
// ################################

	public class ParsedReceivedRequest{

		public NameValueCollection Headers { get; }
		public JObject Body { get; }

		public bool IsLengthWrong { get; }

		public bool BadParsed { get; }

		public bool ContainsHeader( string name){

			foreach( var s in this.Headers.AllKeys)
				if( name.Equals(s,StringComparison.OrdinalIgnoreCase) )
					return true;

			return false;
		}

		public ParsedReceivedRequest( HttpListenerRequest request){

			// Process the input
			var buff = new byte[request.ContentLength64];
			int aux = 0, trials = 0;

			do{

				aux += request.InputStream.ReadAsync(buff,0,buff.Length-aux).Result;
				++trials;

			}while( aux != buff.Length && trials != 4 );

			// Set the class properties
			try{

				this.Body = JObject.Parse(request.ContentEncoding.GetString(buff,0,aux));
				this.BadParsed = false;

			}catch( JsonException e){

				this.Body = JObject.Parse("\"Error\":\"Bad json parse:\n\n" + e.ToString().Replace("\"","'") + "\"");
				this.BadParsed = true;
			}

			this.IsLengthWrong = aux != buff.Length;
			this.Headers = request.Headers;
		}
	}

// ################################
// ######### HTTP response ########
// ################################

#region Parsed Post Request Response


	public struct ParsedPostRequestResponse{

		public NameValueCollection ResponseHeaders { get; }
		public JObject ResponseBody { get; }

		public Uri RequestUri { get; }

		public bool IsLengthWrong { get; }

		public bool BadParsed { get; }

		public bool ContainsResponseHeader( string name){

			foreach( var s in this.ResponseHeaders.AllKeys)
				if( name.Equals(s,StringComparison.OrdinalIgnoreCase) )
					return true;

			return false;
		}

		public ParsedPostRequestResponse( Uri _RequestUrl, NameValueCollection _ResponseHeaders, JObject _ResponseBody, bool _IsLengthWrong, bool _BadParsed){

			this.ResponseHeaders = _ResponseHeaders;
			this.IsLengthWrong = _IsLengthWrong;
			this.ResponseBody = _ResponseBody;
			this.RequestUri = _RequestUrl;
			this.BadParsed = _BadParsed;
		}
	}

	public ParsedPostRequestResponse Post( Uri url, JObject requestBody, Encoding encoder, Encoding decoder, Tuple<string,string>[] extraHeaders = null){

Console.WriteLine("HttpBridge.Post: entrei");

		HttpResponseMessage response = null;

		{
			// Get the body content
			byte[] body = encoder.GetBytes(requestBody != null ? requestBody.ToString() : "");

			var request = new HttpRequestMessage(){

				Content = new ByteArrayContent(body),
				Method = HttpMethod.Post,
				RequestUri = url
			};

			// Set the default headers
			if( body.Length > 0 )
				request.Content.Headers.ContentLength = body.Length;

			// Set the extra headers
			if( extraHeaders != null ){

				foreach( var pair in extraHeaders){

					if( Rssdp.Infrastructure.HttpRequestParser.IsContentHeader(pair.Item1) )
						httpRequestParser.AddContentHeader(request.Content.Headers,pair.Item1,pair.Item2);
					else
						httpRequestParser.AddRequestHeader(request.Headers,pair.Item1,pair.Item2);
				}
			}

			if( body.Length > 0 && !request.Content.Headers.Contains("Content-Type") )
				throw new Exception("No content type specified for non empty request");

try{

Console.WriteLine("HttpBridge.Post: no try");

			// Get the response using the request content object
			var responseTask = this.client.SendAsync(request,CancellationToken.None);
			responseTask.Wait();

Console.WriteLine("HttpBridge.Post: recebi response");

			if( !responseTask.IsFaulted )
				response = responseTask.Result;

			else
				throw new Exception("Cliente não mandou resposta, provavelmente");

}catch( Exception e){

	Console.WriteLine("Erro ao enviar request e receber response para o Óculus:\n\n" + e.ToString());
	return new ParsedPostRequestResponse(url,new NameValueCollection(),new JObject(),false,false);
}


		}

		// Get the response body content
		byte[] bodyBytes = response.Content.ReadAsByteArrayAsync().Result;

		// Get the headers
		var headers = new NameValueCollection();

		foreach( var kpv in response.Headers)
			headers.Add(kpv.Key,string.Join(" ",kpv.Value));

		foreach( var kpv in response.Content.Headers)
			headers.Add(kpv.Key,string.Join(" ",kpv.Value));

		JObject parsedBody;
		bool BadParsed;

		try{

			parsedBody = JObject.Parse(decoder.GetString(bodyBytes));
			BadParsed = false;

		}catch( JsonException e){

			parsedBody = JObject.Parse("\"Error\":\"Bad json parse:\n\n" + e.ToString().Replace("\"","'") + "\"");
			BadParsed = true;
		}

		return new ParsedPostRequestResponse(url,headers,parsedBody,response.Content.Headers.ContentLength != bodyBytes.Length,BadParsed);
	}

#endregion

	public static void SendRequestResponse( HttpListenerResponse response, JObject responseBody, Encoding encoder, Tuple<string,string>[] extraHeaders = null){

		// Create the body content
		byte[] responseBodyBytes = encoder.GetBytes(responseBody.ToString());

		// Set the default headers
		response.KeepAlive = true;

		if( responseBodyBytes.Length > 0 )
			response.ContentLength64 = responseBodyBytes.Length;

		// Set the extra headers
		if( extraHeaders != null ){

			foreach( var pair in extraHeaders){

				if( pair.Item1.Equals("Content-Length",StringComparison.InvariantCultureIgnoreCase) )
					throw new InvalidOperationException("The content length cannot be set via this method");

				else if( pair.Item1.Equals("Connection",StringComparison.InvariantCultureIgnoreCase) )
					throw new InvalidOperationException("The 'Connection' attribute cannot be set via this method; it's set to keep-alive.");

				else{

					try{

						response.Headers.Add(pair.Item1,pair.Item2);

					}catch( ArgumentException){

						if( response.Headers.GetType().GetProperty(pair.Item1.Replace("-","")) != null )
							response.Headers.GetType().GetProperty(pair.Item1.Replace("-","")).SetValue(response.Headers,pair.Item2);
						else
							throw;
					}
				}
			}
		}

		if( responseBodyBytes.Length > 0 && string.IsNullOrEmpty(response.ContentType) )
			throw new Exception("No content type specified for non empty request");

		// Send the body content
		response.OutputStream.Write(responseBodyBytes,0,responseBodyBytes.Length);
		response.OutputStream.Flush();
	}

	public static void RemoveEmptyRequestResponseHeaders( HttpListenerResponse response){

		foreach( var key in response.Headers.AllKeys)
			if( string.IsNullOrEmpty(response.Headers[key]) )
				response.Headers.Remove(key);
	}
}