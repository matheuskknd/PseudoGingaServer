﻿using System.Net.Http;
using System;

namespace Rssdp.Infrastructure {
	/// <summary>
	/// Provides arguments for the <see cref="ISsdpCommunicationsServer.ResponseReceived"/> event.
	/// </summary>
	public sealed class ResponseReceivedEventArgs : EventArgs {

		#region Fields

		private readonly HttpResponseMessage _Message;
		private readonly UdpEndPoint _ReceivedFrom;

		#endregion

		#region Constructors

		/// <summary>
		/// Full constructor.
		/// </summary>
		/// <param name="message">The <see cref="HttpResponseMessage"/> that was received.</param>
		/// <param name="receivedFrom">A <see cref="UdpEndPoint"/> representing the sender's address (sometimes used for replies).</param>
		public ResponseReceivedEventArgs( HttpResponseMessage message,UdpEndPoint receivedFrom ) {
			_Message = message;
			_ReceivedFrom = receivedFrom;
		}

		#endregion

		#region Public Properties

		/// <summary>
		/// The <see cref="HttpResponseMessage"/> that was received.
		/// </summary>
		public HttpResponseMessage Message => _Message;

		/// <summary>
		/// The <see cref="UdpEndPoint"/> the response came from.
		/// </summary>
		public UdpEndPoint ReceivedFrom => _ReceivedFrom;

		#endregion
	}
}