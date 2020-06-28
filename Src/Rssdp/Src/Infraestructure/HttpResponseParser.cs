using System.Net.Http;
using System.Linq;
using System.Net;
using System;

namespace Rssdp.Infrastructure {

	/// <summary>
	/// Parses a string into a <see cref="System.Net.Http.HttpResponseMessage"/> or throws an exception.
	/// </summary>
	public sealed class HttpResponseParser : HttpParserBase<System.Net.Http.HttpResponseMessage> {

		#region Public Methods

		/// <summary>
		/// Parses the specified data into a <see cref="System.Net.Http.HttpResponseMessage"/> instance.
		/// </summary>
		/// <param name="data">A string containing the data to parse.</param>
		/// <returns>A <see cref="System.Net.Http.HttpResponseMessage"/> instance containing the parsed data.</returns>
		public override HttpResponseMessage Parse( string data ) {
			System.Net.Http.HttpResponseMessage retVal = null;
			try {

				retVal = new System.Net.Http.HttpResponseMessage();
				retVal.Content = Parse(retVal,retVal.Headers,data);
				return retVal;

			} catch {
				if( retVal != null ) {
					retVal.Dispose();
				}

				throw;
			}
		}

		#endregion

		#region Overrides Methods

		/// <summary>
		/// Used to parse the first line of an HTTP request or response and assign the values to the appropriate properties on the <paramref name="message"/>.
		/// </summary>
		/// <param name="data">The first line of the HTTP message to be parsed.</param>
		/// <param name="message">Either a <see cref="System.Net.Http.HttpResponseMessage"/> or <see cref="System.Net.Http.HttpRequestMessage"/> to assign the parsed values to.</param>
		protected override void ParseStatusLine( string data,HttpResponseMessage message ) {
			if( data == null ) {
				throw new ArgumentNullException("data");
			}

			if( message == null ) {
				throw new ArgumentNullException("message");
			}

			string[] parts = data.Split(' ');
			if( parts.Length < 3 ) {
				throw new ArgumentException("data status line is invalid. Insufficient status parts.","data");
			}

			message.Version = ParseHttpVersion(parts[0].Trim());

			int statusCode = -1;
			if( !int.TryParse(parts[1].Trim(),out statusCode) ) {
				throw new ArgumentException("data status line is invalid. Status code is not a valid integer.","data");
			}

			message.StatusCode = (HttpStatusCode) statusCode;
			message.ReasonPhrase = parts[2].Trim();
		}

		#endregion
	}
}