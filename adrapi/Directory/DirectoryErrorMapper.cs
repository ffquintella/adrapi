using System;
using adrapi.domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace adrapi.Directory
{
    /// <summary>
    /// Maps directory-provider exceptions to clear, consistent HTTP responses with
    /// a <see cref="ProblemDetails"/> body. Client mistakes surface as 4xx; backend
    /// faults (Graph 5xx, throttling, token/permission failures) surface as 5xx —
    /// never leaking an opaque upstream error. The Graph <c>request-id</c> is echoed
    /// in the problem extensions so support can cross-correlate with Microsoft.
    /// </summary>
    public static class DirectoryErrorMapper
    {
        /// <summary>Maps an exception to an <see cref="ObjectResult"/> carrying a <see cref="ProblemDetails"/>.</summary>
        public static ObjectResult ToProblem(Exception ex)
        {
            return ex switch
            {
                GraphException ge => GraphProblem(ge),
                WrongParameterException wp => Problem(StatusCodes.Status400BadRequest, "Invalid request", wp.Message),
                NotSupportedException ns => Problem(StatusCodes.Status400BadRequest, "Operation not supported", ns.Message),
                InvalidCredentialsException ic => Problem(StatusCodes.Status502BadGateway, "Upstream authentication failed", ic.Message),
                NullException ne => Problem(StatusCodes.Status400BadRequest, "Invalid request", ne.Message),
                _ => Problem(StatusCodes.Status500InternalServerError, "Internal server error", "An unexpected error occurred."),
            };
        }

        private static ObjectResult GraphProblem(GraphException ge)
        {
            // Map the Graph HTTP status to the adrapi-facing status.
            var status = (int)ge.StatusCode switch
            {
                400 or 409 or 422 => (int)ge.StatusCode,           // client errors pass through
                404 => StatusCodes.Status404NotFound,
                429 => StatusCodes.Status503ServiceUnavailable,    // throttled (retries exhausted)
                401 or 403 => StatusCodes.Status502BadGateway,     // our token/permissions to Graph — a config fault
                >= 500 => StatusCodes.Status502BadGateway,         // upstream Graph fault
                0 => StatusCodes.Status503ServiceUnavailable,      // transport failure / retries exhausted
                _ => StatusCodes.Status502BadGateway,
            };

            var detail = status >= 500
                ? "The Entra ID (Microsoft Graph) backend could not service the request."
                : Sanitize(ge.Message);

            var problem = BuildProblem(status, "Microsoft Graph error", detail);
            if ((int)ge.StatusCode != 0) problem.Extensions["graphStatus"] = (int)ge.StatusCode;
            if (!string.IsNullOrEmpty(ge.RequestId)) problem.Extensions["graphRequestId"] = ge.RequestId;
            if (!string.IsNullOrEmpty(ge.ClientRequestId)) problem.Extensions["graphClientRequestId"] = ge.ClientRequestId;

            return new ObjectResult(problem) { StatusCode = status };
        }

        private static ObjectResult Problem(int status, string title, string detail)
            => new(BuildProblem(status, title, Sanitize(detail))) { StatusCode = status };

        private static ProblemDetails BuildProblem(int status, string title, string detail)
            => new()
            {
                Status = status,
                Title = title,
                Detail = detail,
            };

        // Defensive: never echo control characters / overly long upstream text.
        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            var trimmed = value.Length > 1024 ? value.Substring(0, 1024) : value;
            return trimmed.Replace("\r", " ").Replace("\n", " ");
        }
    }
}
