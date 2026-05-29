using System;
using System.Net;

namespace adrapi.domain.Exceptions
{
    /// <summary>
    /// Raised when a Microsoft Graph request fails after the client's retry
    /// policy is exhausted (or for a non-retryable error response). Carries the
    /// HTTP status and the Graph <c>request-id</c>/<c>client-request-id</c> so the
    /// failure can be cross-correlated with Microsoft support and mapped to a
    /// clear 4xx/5xx by the controllers.
    /// </summary>
    public class GraphException : Exception
    {
        /// <summary>HTTP status code returned by Graph (0 when the request never completed).</summary>
        public HttpStatusCode StatusCode { get; }

        /// <summary>Graph server-side request id (<c>request-id</c> header), when present.</summary>
        public string RequestId { get; }

        /// <summary>Client-supplied request id (<c>client-request-id</c> header), when present.</summary>
        public string ClientRequestId { get; }

        /// <summary>Raw response body (truncated by the caller), useful for diagnostics.</summary>
        public string ResponseBody { get; }

        public GraphException(string message, HttpStatusCode statusCode, string requestId = null, string clientRequestId = null, string responseBody = null, Exception inner = null)
            : base(message, inner)
        {
            StatusCode = statusCode;
            RequestId = requestId;
            ClientRequestId = clientRequestId;
            ResponseBody = responseBody;
        }
    }
}
