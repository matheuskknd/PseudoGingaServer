using System;
using System.Text;
using Newtonsoft.Json.Linq;

class MainClass {

	private static ServerEmulator server = null;

	static void Main( string[] args){

		Console.WriteLine("Iniciando servidor SSDP e ouvindo no multcast 239.255.255.250:1900!");

		// Create HTTP server listenning on "http://*:44642/dtv/" and a SSDP server
		using( server = new ServerEmulator(null,OnClientConnected,null) ){

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
						server.SendAction();
						break;

					case "L":
						server.ListClients();
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

	private static JObject OnEventReceived( string relPath, JObject message){

		throw new NotImplementedException();
	}

	private static void OnClientConnected( Uri client){

		// The scene request message
		var body = new JObject(

			new JProperty("appID","appid"),
			new JProperty("documentID","docid"),
			new JProperty("sceneNode","nodeid"),
			new JProperty("sceneUrl","http://acromyrmex/sample.ncl"),
			new JProperty("notifyEvents",new JArray(

				new JValue("selection"),
				new JValue("lookAt"),
				new JValue("drag"),
				new JValue("drop")
			))
		);

		// Send a scene to play
		var postContext = server.HttpViceVersa.Post(new Uri(client,"dtv/remote-mediaplayer/scene/"),body,Encoding.UTF8,Encoding.UTF8,new Tuple<string,string>[]{

			Tuple.Create("Content-Type","application/json; charset=utf-8"),
			Tuple.Create("Accept","application/json; charset=utf-8")
		});
	}

	private static void OnClientDisconnected( Uri client){

		throw new NotImplementedException();
	}
}
