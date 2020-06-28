using System.Collections.Generic;
using System.Net.Http;
using System.Linq;
using System;
using System.Net.Http.Headers;
using System.Text;

namespace Rssdp.Infrastructure {

	/// <summary>
	/// A base class for the <see cref="HttpResponseParser"/> and <see cref="HttpRequestParser"/> classes. Not intended for direct use.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	public abstract class HttpParserBase<T> where T : new() {

		#region Fields & Constants

		private static readonly string[] ContentHeaderNames = new string[]{

			"Allow", "Content-Disposition", "Content-Encoding", "Content-Language", "Content-Length", "Content-Location", "Content-MD5", "Content-Range", "Content-Type", "Expires", "Last-Modified"
		};

		private static readonly string[] LineTerminators = new string[] { "\r\n","\n" };
		private static readonly char[] SeparatorCharacters = new char[] { ',',';' };

		#endregion

		#region Public Methods

		/// <summary>
		/// Parses the <paramref name="data"/> provided into either a <see cref="System.Net.Http.HttpRequestMessage"/> or <see cref="System.Net.Http.HttpResponseMessage"/> object.
		/// </summary>
		/// <param name="data">A string containing the HTTP message to parse.</param>
		/// <returns>Either a <see cref="System.Net.Http.HttpRequestMessage"/> or <see cref="System.Net.Http.HttpResponseMessage"/> object containing the parsed data.</returns>
		public abstract T Parse( string data );

		/// <summary>
		/// Parses a string containing either an HTTP request or response into a <see cref="System.Net.Http.HttpRequestMessage"/> or <see cref="System.Net.Http.HttpResponseMessage"/> object.
		/// </summary>
		/// <param name="message">A <see cref="System.Net.Http.HttpRequestMessage"/> or <see cref="System.Net.Http.HttpResponseMessage"/> object representing the parsed message.</param>
		/// <param name="headers">A reference to the <see cref="System.Net.Http.Headers.HttpHeaders"/> collection for the <paramref name="message"/> object.</param>
		/// <param name="data">A string containing the data to be parsed.</param>
		/// <returns>An <see cref="System.Net.Http.HttpContent"/> object containing the content of the parsed message.</returns>
		[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage","CA2202:Do not dispose objects multiple times",Justification = "Honestly, it's fine. MemoryStream doesn't mind.")]
		protected virtual HttpContent Parse( T message,System.Net.Http.Headers.HttpHeaders headers,string data ) {
			if( data == null ) {
				throw new ArgumentNullException("data");
			}

			if( data.Length == 0 ) {
				throw new ArgumentException("data cannot be an empty string.","data");
			}

			if( !LineTerminators.Any(data.Contains) ) {
				throw new ArgumentException("data is not a valid request, it does not contain any CRLF/LF terminators.","data");
			}

			HttpContent retVal = null;
			try {
				System.IO.MemoryStream contentStream = new System.IO.MemoryStream();
				try {
					retVal = new StreamContent(contentStream);

					string[] lines = data.Split(LineTerminators,StringSplitOptions.None);

					//First line is the 'request' line containing http protocol details like method, uri, http version etc.
					ParseStatusLine(lines[0],message);

					int lineIndex = ParseHeaders(headers,retVal.Headers,lines);

					if( lineIndex < lines.Length - 1 ) {
						//Read rest of any remaining data as content.
						if( lineIndex < lines.Length - 1 ) {
							//This is inefficient in multiple ways, but not sure of a good way of correcting. Revisit.
							byte[] body = System.Text.UTF8Encoding.UTF8.GetBytes(string.Join(null,lines,lineIndex,lines.Length - lineIndex));
							contentStream.Write(body,0,body.Length);
							contentStream.Seek(0,System.IO.SeekOrigin.Begin);
						}
					}
				} catch {
					if( contentStream != null ) {
						contentStream.Dispose();
					}

					throw;
				}
			} catch {
				if( retVal != null ) {
					retVal.Dispose();
				}

				throw;
			}

			return retVal;
		}

		/// <summary>
		/// Used to parse the first line of an HTTP request or response and assign the values to the appropriate properties on the <paramref name="message"/>.
		/// </summary>
		/// <param name="data">The first line of the HTTP message to be parsed.</param>
		/// <param name="message">Either a <see cref="System.Net.Http.HttpResponseMessage"/> or <see cref="System.Net.Http.HttpRequestMessage"/> to assign the parsed values to.</param>
		protected abstract void ParseStatusLine( string data,T message );

		/// <summary>
		/// Returns a boolean indicating whether the specified HTTP header name represents a content header (true), or a message header (false).
		/// </summary>
		/// <param name="headerName">A string containing the name of the header to return the type of.</param>
		/// <returns>A boolean, true if the specified header relates to HTTP content, otherwise false.</returns>
		public static bool IsContentHeader( string headerName) =>
			ContentHeaderNames.Contains(headerName,StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Parses the HTTP version text from an HTTP request or response status line and returns a <see cref="Version"/> object representing the parsed values.
		/// </summary>
		/// <param name="versionData">A string containing the HTTP version, from the message status line.</param>
		/// <returns>A <see cref="Version"/> object containing the parsed version data.</returns>
		protected static Version ParseHttpVersion( string versionData ) {
			if( versionData == null ) {
				throw new ArgumentNullException("versionData");
			}

			int versionSeparatorIndex = versionData.IndexOf('/');
			if( versionSeparatorIndex <= 0 || versionSeparatorIndex == versionData.Length ) {
				throw new ArgumentException("request header line is invalid. Http Version not supplied or incorrect format.","versionData");
			}

			return Version.Parse(versionData.Substring(versionSeparatorIndex + 1));
		}

#if true

		/// <summary>
		/// Insert a request HTTP header
		/// </summary>
		/// <param name="header">A <see cref="System.Net.Http.Headers.HttpRequestHeaders"/> object on wich the request header will be recorded.</param>
		/// <param name="name">The header attribute name.</param>
		/// <param name="value">The header attribute values.</param>
		public void AddRequestHeader( HttpRequestHeaders header, string name, IEnumerable<string> values) =>
			AddRequestHeader(header,name,string.Join(" ",values));

		/// <summary>
		/// Insert a request HTTP header
		/// </summary>
		/// <param name="header">A <see cref="System.Net.Http.Headers.HttpRequestHeaders"/> object on wich the request header will be recorded.</param>
		/// <param name="name">The header attribute name.</param>
		/// <param name="value">The header attribute value.</param>
		public void AddRequestHeader( HttpRequestHeaders header, string name, string value){

			if( name.Equals("Authorization",StringComparison.OrdinalIgnoreCase) )
				header.Authorization = new AuthenticationHeaderValue(value);

			else if( name.Equals("Cache-Control",StringComparison.OrdinalIgnoreCase) )
				header.CacheControl = CacheControlHeaderValue.Parse(value);

			else if( name.Equals("Date",StringComparison.OrdinalIgnoreCase) )
				header.Date = DateTimeOffset.Parse(value);

			else if( name.Equals("If-Modified-Since",StringComparison.OrdinalIgnoreCase) )
				header.IfModifiedSince = DateTimeOffset.Parse(value);

			else if( name.Equals("If-Range",StringComparison.OrdinalIgnoreCase) )
				header.IfRange = RangeConditionHeaderValue.Parse(value);

			else if( name.Equals("If-Unmodified-Since",StringComparison.OrdinalIgnoreCase) )
				header.IfUnmodifiedSince = DateTimeOffset.Parse(value);

			else if( name.Equals("Max-Forwards",StringComparison.OrdinalIgnoreCase) )
				header.MaxForwards = int.Parse(value);

			else if( name.Equals("Proxy-Authorization",StringComparison.OrdinalIgnoreCase) )
				header.ProxyAuthorization = AuthenticationHeaderValue.Parse(value);

			else if( name.Equals("Range",StringComparison.OrdinalIgnoreCase) )
				header.Range = RangeHeaderValue.Parse(value);

			else if( name.Equals("Referrer",StringComparison.OrdinalIgnoreCase) )
				header.Referrer = new Uri(value);

			else{

				try{

					header.Add(name,value);

				}catch( ArgumentException){

					if( header.GetType().GetProperty(name.Replace("-","")) != null )
						header.GetType().GetProperty(name.Replace("-","")).SetValue(header,value);
					else
						throw;
				}
			}
		}

		/// <summary>
		/// Insert a response HTTP header
		/// </summary>
		/// <param name="header">A <see cref="System.Net.Http.Headers.HttpResponseHeaders"/> object on wich the response header will be recorded.</param>
		/// <param name="name">The header attribute name.</param>
		/// <param name="values">The header attribute values.</param>
		public void AddResponseHeader( HttpResponseHeaders header, string name, IEnumerable<string> values) =>
			AddResponseHeader(header,name,string.Join(" ",values));

		/// <summary>
		/// Insert a response HTTP header
		/// </summary>
		/// <param name="header">A <see cref="System.Net.Http.Headers.HttpResponseHeaders"/> object on wich the response header will be recorded.</param>
		/// <param name="name">The header attribute name.</param>
		/// <param name="value">The header attribute value.</param>
		public void AddResponseHeader( HttpResponseHeaders header, string name, string value){

			if( name.Equals("Age",StringComparison.OrdinalIgnoreCase) )
				header.Age = TimeSpan.Parse(value);

			else if( name.Equals("Cache-Control",StringComparison.OrdinalIgnoreCase) )
				header.CacheControl = CacheControlHeaderValue.Parse(value);

			else if( name.Equals("Date",StringComparison.OrdinalIgnoreCase) )
				header.Date = DateTimeOffset.Parse(value);

			else if( name.Equals("ETag",StringComparison.OrdinalIgnoreCase) )
				header.ETag = EntityTagHeaderValue.Parse(value);

			else if( name.Equals("Location",StringComparison.OrdinalIgnoreCase) )
				header.Location = new Uri(value);

			else if( name.Equals("Retry-After",StringComparison.OrdinalIgnoreCase) )
				header.RetryAfter = RetryConditionHeaderValue.Parse(value);

			else
				header.Add(name,value);
		}

		/// <summary>
		/// Insert a content HTTP header
		/// </summary>
		/// <param name="header">A <see cref="System.Net.Http.Headers.HttpContentHeaders"/> object on wich the content header will be recorded.</param>
		/// <param name="name">The header attribute name.</param>
		/// <param name="values">The header attribute values.</param>
		public void AddContentHeader( HttpContentHeaders header, string name, IEnumerable<string> values) =>
			AddContentHeader(header,name,string.Join(" ",values));

		/// <summary>
		/// Insert a content HTTP header
		/// </summary>
		/// <param name="header">A <see cref="System.Net.Http.Headers.HttpContentHeaders"/> object on wich the content header will be recorded.</param>
		/// <param name="name">The header attribute name.</param>
		/// <param name="value">The header attribute value.</param>
		public void AddContentHeader( HttpContentHeaders header, string name, string value){

			if( name.Equals("Allow",StringComparison.OrdinalIgnoreCase) )
				header.Add("Allow",value);

			else if( name.Equals("Content-Disposition",StringComparison.OrdinalIgnoreCase) )
				header.ContentDisposition = new ContentDispositionHeaderValue(value);

			else if( name.Equals("Content-Encoding",StringComparison.OrdinalIgnoreCase) )
				header.Add("Content-Encoding",value);

			else if( name.Equals("Content-Language",StringComparison.OrdinalIgnoreCase) )
				header.Add("Content-Language",value);

			else if( name.Equals("Content-Length",StringComparison.OrdinalIgnoreCase) )
				header.ContentLength = long.Parse(value);

			else if( name.Equals("Content-Length",StringComparison.OrdinalIgnoreCase) )
				header.ContentLength = long.Parse(value);

			else if( name.Equals("Content-Location",StringComparison.OrdinalIgnoreCase) )
				header.ContentLocation = new Uri(value);

			else if( name.Equals("Content-MD5",StringComparison.OrdinalIgnoreCase) )
				header.ContentMD5 = Encoding.ASCII.GetBytes(value);

			else if( name.Equals("Content-Range",StringComparison.OrdinalIgnoreCase) )
				header.ContentRange = ContentRangeHeaderValue.Parse(value);

			else if( name.Equals("Content-Type",StringComparison.OrdinalIgnoreCase) )
				header.Add("Content-Type",value);

//				C# does not allow any value here... Don't know why
//				header.ContentType = new MediaTypeHeaderValue(value);

			else if( name.Equals("Expires",StringComparison.OrdinalIgnoreCase) )
				header.Expires = DateTimeOffset.Parse(value);

			else if( name.Equals("Last-Modified",StringComparison.OrdinalIgnoreCase) )
				header.LastModified = DateTimeOffset.Parse(value);

			else
				throw new ArgumentException("Content has no attribute named: " + name);
		}

#endif

		#endregion

		#region Private Methods

		/// <summary>
		/// Parses a line from an HTTP request or response message containing a header name and value pair.
		/// </summary>
		/// <param name="line">A string containing the data to be parsed.</param>
		/// <param name="headers">A reference to a <see cref="System.Net.Http.Headers.HttpHeaders"/> collection to which the parsed header will be added.</param>
		/// <param name="contentHeaders">A reference to a <see cref="System.Net.Http.Headers.HttpHeaders"/> collection for the message content, to which the parsed header will be added.</param>
		private void ParseHeader( string line,System.Net.Http.Headers.HttpHeaders headers,System.Net.Http.Headers.HttpHeaders contentHeaders ) {
			//Header format is
			//name: value
			int headerKeySeparatorIndex = line.IndexOf(":",StringComparison.OrdinalIgnoreCase);
			string headerName = line.Substring(0,headerKeySeparatorIndex).Trim();
			string headerValue = line.Substring(headerKeySeparatorIndex + 1).Trim();

			//Not sure how to determine where request headers and and content headers begin,
			//at least not without a known set of headers (general headers first the content headers)
			//which seems like a bad way of doing it. So we'll assume if it's a known content header put it there
			//else use request headers.

			IList<string> values = ParseValues(headerValue);
			System.Net.Http.Headers.HttpHeaders headersToAddTo = IsContentHeader(headerName) ? contentHeaders : headers;

			if( values.Count > 1 ) {
				headersToAddTo.TryAddWithoutValidation(headerName,values);
			} else {
				headersToAddTo.TryAddWithoutValidation(headerName,values.First());
			}
		}

		private int ParseHeaders( System.Net.Http.Headers.HttpHeaders headers,System.Net.Http.Headers.HttpHeaders contentHeaders,string[] lines ) {
			//Blank line separates headers from content, so read headers until we find blank line.
			int lineIndex = 1;
			string line = null, nextLine = null;
			while( lineIndex + 1 < lines.Length && !string.IsNullOrEmpty((line = lines[lineIndex++])) ) {
				//If the following line starts with space or tab (or any whitespace), it is really part of this header but split for human readability.
				//Combine these lines into a single comma separated style header for easier parsing.
				while( lineIndex < lines.Length && !string.IsNullOrEmpty((nextLine = lines[lineIndex])) ) {
					if( nextLine.Length > 0 && char.IsWhiteSpace(nextLine[0]) ) {
						line += "," + nextLine.TrimStart();
						lineIndex++;
					} else {
						break;
					}
				}

				ParseHeader(line,headers,contentHeaders);
			}
			return lineIndex;
		}

		private static IList<string> ParseValues( string headerValue ) {
			// This really should be better and match the HTTP 1.1 spec,
			// but this should actually be good enough for SSDP implementations
			// I think.
			List<string> values = new List<string>();

			if( headerValue == "\"\"" ) {
				values.Add(string.Empty);
				return values;
			}

			int indexOfSeparator = headerValue.IndexOfAny(SeparatorCharacters);
			if( indexOfSeparator <= 0 ) {
				values.Add(headerValue);
			} else {
				string[] segments = headerValue.Split(SeparatorCharacters);
				if( headerValue.Contains("\"") ) {
					for( int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++ ) {
						string segment = segments[segmentIndex];
						if( segment.Trim().StartsWith("\"",StringComparison.OrdinalIgnoreCase) ) {
							segment = CombineQuotedSegments(segments,ref segmentIndex,segment);
						}

						values.Add(segment);
					}
				} else {
					values.AddRange(segments);
				}
			}

			return values;
		}

		private static string CombineQuotedSegments( string[] segments,ref int segmentIndex,string segment ) {
			string trimmedSegment = segment.Trim();
			for( int index = segmentIndex; index < segments.Length; index++ ) {
				if( trimmedSegment == "\"\"" ||
					(
						trimmedSegment.EndsWith("\"",StringComparison.OrdinalIgnoreCase)
						&& !trimmedSegment.EndsWith("\"\"",StringComparison.OrdinalIgnoreCase)
						&& !trimmedSegment.EndsWith("\\\"",StringComparison.OrdinalIgnoreCase))
					) {
					segmentIndex = index;
					return trimmedSegment.Substring(1,trimmedSegment.Length - 2);
				}

				if( index + 1 < segments.Length ) {
					trimmedSegment += "," + segments[index + 1].TrimEnd();
				}
			}

			segmentIndex = segments.Length;
			if( trimmedSegment.StartsWith("\"",StringComparison.OrdinalIgnoreCase) && trimmedSegment.EndsWith("\"",StringComparison.OrdinalIgnoreCase) ) {
				return trimmedSegment.Substring(1,trimmedSegment.Length - 2);
			} else {
				return trimmedSegment;
			}
		}

		#endregion
	}
}