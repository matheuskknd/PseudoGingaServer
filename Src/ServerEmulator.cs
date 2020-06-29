using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

class ServerEmulator : IDisposable{

	private static readonly string EVENT_PREFIX = "/dtv/current-service/apps/";

	// Stores a list of: <BaseUrl,Connection>
	private LinkedList<Tuple<Uri,Socket>> clients = new LinkedList<Tuple<Uri,Socket>>();

	// An instance of HttpViceVersa that can send requests and is also listenning to
	public HttpViceVersa HttpViceVersa { get; }

	// SSDP server listenning on port 1900
	private Rssdp.Infrastructure.SsdpCommunicationsServer ssdpServer;

	// Event callback
	private Func<string,JObject,JObject> OnEventReceived;
	Action<Uri> OnClientDisconnected;
	Action<Uri> OnClientConnected;

	public ServerEmulator( Func<string,JObject,JObject> onEventReceived, Action<Uri> onClientConnected = null, Action<Uri> onClientDisconnected = null){

		this.ssdpServer = new Rssdp.Infrastructure.SsdpCommunicationsServer(new Rssdp.SocketFactory(IPAddress.Any.ToString()),1900,1);
		this.HttpViceVersa = new HttpViceVersa(this.OnHttpServerRequested,44642);

		this.OnClientDisconnected = onClientDisconnected;
		this.OnClientConnected = onClientConnected;
		this.OnEventReceived = onEventReceived;

		// Starts to listen
		this.ssdpServer.Request​Received += OnSsdpServerRequested;
		this.ssdpServer.BeginListeningForBroadcasts();
	}

	public void ListClients(){

		lock(this.clients){

			if( this.clients.Count == 0 )
				Console.WriteLine("No clients registered");

			else
				foreach( var client in this.clients)
					Console.WriteLine("Client location: " + client.Item1.ToString());
		}
	}

	public void Dispose(){

		this.ssdpServer.Dispose();

		lock(this.clients){

			foreach( var client in this.clients){

				try{

					client.Item2.Shutdown(SocketShutdown.Both);

				}finally{

					client.Item2.Close();
				}
			}
		}

		this.HttpViceVersa.Dispose();
	}

// ################################
// ############ Action ############
// ################################

	public void SendAction( JObject body = null){

		lock(this.clients){

			if( this.clients.Count != 0 )
				foreach( var client in this.clients){

					Task.Factory.StartNew(() => {

						var message = new JObject(new JProperty("message","Some action message"));

						var postContext = this.HttpViceVersa.Post(

							new Uri(client.Item1,"dtv/remote-mediaplayer/scene/nodes/node-id/"),
							message,
							Encoding.UTF8,
							Encoding.UTF8,
							new Tuple<string,string>[]{

								Tuple.Create("Content-Type","application/json; charset=utf-8"),
								Tuple.Create("Accept","application/json; charset=utf-8")
							}
						);

// ################################
Console.WriteLine("A response da POST request a: " + postContext.RequestUri.ToString() + " HEADERS:\n");

foreach( var key in postContext.ResponseHeaders.AllKeys)
Console.WriteLine("Key: \"" + key + "\" Value: \"" + postContext.ResponseHeaders[key] + "\"");

Console.WriteLine("\nBODY:\n\"" + postContext.ResponseBody + "\"");
// ################################

					});
				}
		}
	}

// ################################
// ############# HTTP #############
// ################################

#region HTTP

	private JObject FinishRegister( JObject message){

		// Try parse the client server URI
		var clientHttpServer = new Uri(message.GetValue("location").ToString());

		// Start listening for a single client TCP connection
		var tcpListener = new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp);
		tcpListener.Bind(new IPEndPoint(IPAddress.Any,0));
		tcpListener.ReceiveTimeout = 60000;
		tcpListener.Blocking = true;
		tcpListener.Listen(1);

		ThreadStart connectionControlerDaemon = () => {

			Socket connection = null;

			{	// Starts a timeout coroutine once csharp doesn't have a TimeOut parameter

				var timer = new Thread(() => { Thread.Sleep(60000); if( connection == null ) tcpListener.Close(); });
				timer.IsBackground = true;
				timer.Start();
			}

			try{

				// Blocks waiting for a client to connect, with 60000 ms TimeOut
				connection = tcpListener.Accept();
				connection.ReceiveTimeout = -1;
				connection.Blocking = true;

				// Once connected, it's also registered
				var register = new LinkedListNode<Tuple<Uri,Socket>>(new Tuple<Uri,Socket>(
					new Uri(string.Format("http://{0}:{1}/",clientHttpServer.Host,clientHttpServer.Port)),connection));

				lock(this.clients){

					this.clients.AddLast(register);
				}

				if( this.OnClientConnected != null )
					this.OnClientConnected(register.Value.Item1);

				// Clean up
				tcpListener.Close();
				tcpListener = null;



// ################################################################
Console.WriteLine("\n\nManifestando o registro completo de um cliente localizado em: \"" + register.Value.Item1.ToString() + "\"");
// ################################################################



				try{

					// The client shall never send a single byte, then it blocks until the connection is broken
					while( connection.Receive(new byte[1],1,SocketFlags.None) != 0 );

				}catch( SocketException){

					Console.WriteLine("\n\nConnection closed!");
				}

				// Once disconnected, it's also removed
				lock(this.clients){

					if( register.List != null )
						this.clients.Remove(register);
				}

				if( this.OnClientDisconnected != null )
					this.OnClientDisconnected(register.Value.Item1);

			}catch( SocketException){

				Console.WriteLine("\n\nControl connection timed out!");
			}
		};

		// Starts the control coroutine
		var control = new Thread(connectionControlerDaemon);
		control.IsBackground = true;
		control.Start();

		return new JObject(new JProperty("control",(tcpListener.LocalEndPoint as IPEndPoint).Port.ToString()));
	}

	private JObject ProcessEvent( string relPath, JObject events){

// ################################
Console.WriteLine("################################\nChegou um evento!\n");
Console.WriteLine("relPath: " + relPath);
Console.WriteLine("events: " + events);
// ################################

		return new JObject(new JProperty("message","Event registered!"));
	}

	public void OnHttpServerRequested( HttpListenerContext context){

		// Get a parsed input
		var parsedRequest = new HttpViceVersa.ParsedReceivedRequest(context.Request);



// ################################################################
Console.WriteLine("\n\nA request de método: " + context.Request.HttpMethod + " e URL: " + context.Request.Url.OriginalString +
" vinda de: " + context.Request.RemoteEndPoint.Address.ToString() + ": HEADERS:\n");

foreach( var key in parsedRequest.Headers.AllKeys)
Console.WriteLine("Value: \"" + key + "\" Value: \"" + parsedRequest.Headers[key] + "\"");

Console.WriteLine("\nBODY:\n\"" + parsedRequest.Body + "\"");
// ################################################################



		// ################################
		// Process the input

		JObject responseMsg;

		// It's a register message
		if( context.Request.HttpMethod == HttpMethod.Post.ToString() && context.Request.Url.AbsolutePath == "/dtv/remote-mediaplayer/" &&
			context.Request.ContentType == "application/json; charset=utf-8" && context.Request.AcceptTypes.Contains("application/json; charset=utf-8") &&
			parsedRequest.Body.ContainsKey("location") && parsedRequest.Body.GetValue("location") != null ){

			try{

				responseMsg = this.FinishRegister(parsedRequest.Body);
				context.Response.StatusCode = (int) HttpStatusCode.OK;

			}catch( Exception e){

				responseMsg = new JObject(new JProperty("error","Registration failed:\n\n" + e.ToString()));
				context.Response.StatusCode = (int) HttpStatusCode.BadRequest;
			}

		// It's a event message
		}else if( context.Request.HttpMethod == HttpMethod.Post.ToString() && context.Request.Url.AbsolutePath.StartsWith(EVENT_PREFIX) &&
			context.Request.ContentType == "application/json; charset=utf-8" && context.Request.AcceptTypes.Contains("application/json; charset=utf-8") ){

			try{

				responseMsg = this.ProcessEvent(context.Request.Url.AbsolutePath.Substring(EVENT_PREFIX.Length),parsedRequest.Body);
				context.Response.StatusCode = (int) HttpStatusCode.OK;

			}catch( Exception e){

				responseMsg = new JObject(new JProperty("error","Event not accepted:\n\n" + e.ToString()));
				context.Response.StatusCode = (int) HttpStatusCode.BadRequest;
			}

		}else{

			responseMsg = new JObject(new JProperty("error","unrecognizable request."));
			context.Response.StatusCode = (int) HttpStatusCode.BadRequest;
		}

		// ################################
		// Send the output

		HttpViceVersa.SendRequestResponse(context.Response,responseMsg,Encoding.UTF8,new Tuple<string,string>[]{

			Tuple.Create("Content-Type","application/json; charset=utf-8")
		});

		// Release the context, clean up
		parsedRequest = null;
		context = null;

		// ################################
		// Do some post processing maybe

		Console.WriteLine("Enrolando pra terminar a thread getRequest-sendResponse...");
		Thread.Sleep(10000);
		Console.WriteLine("Terminei!");
	}

#endregion

// ################################
// ############# SSDP #############
// ################################

#region SSDP

	private static readonly byte[] HTTPUDefaultResponse =  Encoding.ASCII.GetBytes(
		"HTTP/1.1 200 OK\r\n" +
		"CACHE-CONTROL: no-cache\r\n" +
		"ST: urn:sbtvd-org:service:GingaCCWebServices:1\r\n" +
		"USN: uuid:" + System.Guid.NewGuid().ToString() + "::urn:sbtvd-org:service:GingaCCWebServices:1\r\n" +
		"EXT: \r\n" +
		"SERVER: Roku UPnP/1.0 MiniUPnPd/1.4\r\n" +
		"LOCATION: http://" + Dns.GetHostName() + ":44642/dtv/\r\n\r\n"
	);

	private static void OnSsdpServerRequested( object sender, Rssdp.Infrastructure.RequestReceivedEventArgs arg){

		string MAN = arg.Message.Headers.GetValues("MAN").First();
		string ST = arg.Message.Headers.GetValues("ST").First();

		if( arg.Message.Method.ToString().Equals("M-SEARCH") && arg.Message.RequestUri.ToString().Equals("*") && MAN.Equals("\"ssdp:discover\"") &&
			( ST.Equals("urn:sbtvd-org:service:GingaCCWebServices:1") || ST.Equals("ssdp:all") ) ){

			// Get the HTTPU response receiver!
			string[] tmp = arg.ReceivedFrom.ToString().Split(':');

			Action sendHTTPUResponseAssync = () =>
				(new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp)).SendTo(HTTPUDefaultResponse,new IPEndPoint(IPAddress.Parse(tmp[0]),Int32.Parse(tmp[1])));

			// Sends the response assyncronously
			Task.Factory.StartNew(sendHTTPUResponseAssync);
		}
	}

#endregion
}