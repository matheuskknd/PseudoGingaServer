using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class TccServer : IDisposable{

	// Stores a list of: <BaseUrl,Connection>
	private LinkedList<Tuple<Uri,Socket>> clients = new LinkedList<Tuple<Uri,Socket>>();

	private Rssdp.Infrastructure.SsdpCommunicationsServer ssdpServer;
	private HttpViceVersa httpServer;

	public TccServer(){

		this.ssdpServer = new Rssdp.Infrastructure.SsdpCommunicationsServer(new Rssdp.SocketFactory(IPAddress.Any.ToString()),1900,1);
		this.httpServer = new HttpViceVersa(this.onHttpServerRequested,44642);
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

		this.httpServer.Dispose();
	}

// ################################
// ############# MAIN #############
// ################################

#region MAIN

	static void Main( string[] args){

		Console.WriteLine("Iniciando servidor SSDP e ouvindo no multcast 239.255.255.250:1900!");

		// Create HTTP server listenning on "http://*:44642/dtv/" and a SSDP server
		using( var self = new TccServer() ){

			// Starts to listen
			self.ssdpServer.Request​Received += onSsdpServerRequested;
			self.ssdpServer.BeginListeningForBroadcasts();

			// Sleep waiting command
			Action writeOutOptions = () => {

				Console.WriteLine("GINGA SERVER - Signal sending service");
				Console.WriteLine("Commands");
				Console.WriteLine("--------------------------------");
				Console.WriteLine("S to send Signal");
				Console.WriteLine("L to list all listeners");
				Console.WriteLine("X to exit");
				Console.WriteLine();
			};

			writeOutOptions();
			var key = new ConsoleKeyInfo();

			while( true ){

				Console.WriteLine();
				Console.Write("Enter command: ");
				key = Console.ReadKey();
				Console.WriteLine();
				Console.WriteLine();

				string command = key.KeyChar.ToString().ToUpperInvariant();

				switch( command ){

					case "S":

						lock(self.clients){

							if( self.clients.Count == 0 )
								break;

							foreach( var client in self.clients){

								Task.Factory.StartNew(() => {

									var postContext = self.httpServer.Post(

										new Uri(client.Item1,"dtv/remote-mediaplayer/scene/nodes/node-id/"),
										"{\"Message:\": \"Está é minha mensagem de ação!\"}",
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

						break;

					case "L":

						lock(self.clients){

							if( self.clients.Count == 0 )
								Console.WriteLine("No clients registered");

							else
								foreach( var client in self.clients)
									Console.WriteLine("Client location: " + client.Item1.ToString());
						}

						break;

					case "X":
						Environment.Exit(0);
						break;

					default:
						Console.WriteLine("Unknown command. Press ? for a list of valid commands.");
						break;
				}
			}
		}
	}

#endregion

// ################################
// ############# HTTP #############
// ################################

#region HTTP

	public void onHttpServerRequested( HttpListenerContext context){

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

		if( context.Request.HttpMethod == HttpMethod.Post.ToString() && context.Request.Url.AbsolutePath == "/dtv/remote-mediaplayer/" &&
			parsedRequest.ContainsHeader("LOCATION") && !string.IsNullOrEmpty(parsedRequest.Headers["LOCATION"]) &&
			context.Request.ContentType == "application/json; charset=utf-8" && context.Request.AcceptTypes.Contains("application/json; charset=utf-8") ){

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
						new Uri(string.Format("http://{0}:{1}/",
							(new Uri(parsedRequest.Headers["LOCATION"])).Host,
							(new Uri(parsedRequest.Headers["LOCATION"])).Port
						)),connection));

					lock(this.clients){

						this.clients.AddLast(register);
					}

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

				}catch( SocketException){

					Console.WriteLine("\n\nControl connection timed out!");
				}
			};

			// Starts the control coroutine
			var control = new Thread(connectionControlerDaemon);
			control.IsBackground = true;
			control.Start();

			// ################################
			// Send the output

			HttpViceVersa.SendRequestResponse(context.Response,"{\"Message:\": \"Parabéns! Você pode terminar de se registrar agora!\"}",Encoding.UTF8,new Tuple<string, string>[]{

				Tuple.Create("CONTROL",(tcpListener.LocalEndPoint as IPEndPoint).Port.ToString()),
				Tuple.Create("Content-Type","application/json; charset=utf-8")
			});

		}else if( context.Request.HttpMethod == HttpMethod.Post.ToString() && context.Request.Url.AbsolutePath == "/dtv/current-service/apps/appid/nodes/document-id/node-id/" &&
			context.Request.ContentType == "application/json; charset=utf-8" && context.Request.AcceptTypes.Contains("application/json; charset=utf-8") ){

			// ################################
			// Send the output


// ################################
Console.WriteLine("################################\nChegou um evento!\n");
// ################################

			HttpViceVersa.SendRequestResponse(context.Response,"{\"Message:\": \"Ok, seu evento chegou!\"}",Encoding.UTF8,new Tuple<string, string>[]{

				Tuple.Create("Content-Type","application/json; charset=utf-8")
			});

		}else{

			Console.WriteLine("Processando input genérica...");
			Thread.Sleep(10000);

			// ################################
			// Send the output

			//HttpViceVersa.RemoveEmptyRequestResponseHeaders(context.Response);

			HttpViceVersa.SendRequestResponse(context.Response,"{\"Message:\": \"Olá! Obrigado por esperar 1000 ms, esta é minha response!\"}",Encoding.UTF8,new Tuple<string, string>[]{

				Tuple.Create("Content-Type","application/json; charset=utf-8")
			});
		}

		// ################################
		// The context shall be released

		context = null;

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

	private static void onSsdpServerRequested( object sender, Rssdp.Infrastructure.RequestReceivedEventArgs arg){

		Dictionary<string,string> attribute = arg.Message.Headers.ToDictionary(kpv => kpv.Key,kpv => string.Join(" ",kpv.Value));

		if( arg.Message.Method.ToString().Equals("M-SEARCH") && arg.Message.RequestUri.ToString().Equals("*") && attribute["MAN"].Equals("\"ssdp:discover\"") &&
			( attribute["ST"].Equals("ssdp:all") || attribute["ST"].Equals("urn:sbtvd-org:service:GingaCCWebServices:1") ) ){

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