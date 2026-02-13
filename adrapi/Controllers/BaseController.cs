using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using System.Linq;

namespace adrapi.Controllers
{

    /// <summary>
    /// Shared controller base with request metadata extraction.
    /// </summary>
    public class BaseController: ControllerBase
    {
        protected string requesterID { get; set; }

        protected ILogger logger;

        protected IConfiguration configuration;

        /// <summary>
        /// Extracts the request key identifier from the <c>api-key</c> header.
        /// </summary>
        protected void ProcessRequest()
        {
            var apiKey = this.Request.Headers["api-key"].ToString();
            requesterID = string.IsNullOrWhiteSpace(apiKey) ? "unknown" : apiKey.Split(':')[0];
        }

        /// <summary>
        /// Gets the request correlation ID from header or falls back to ASP.NET trace identifier.
        /// </summary>
        protected string GetCorrelationId()
        {
            var correlationId = Request.Headers["X-Correlation-ID"].FirstOrDefault();
            return string.IsNullOrWhiteSpace(correlationId) ? HttpContext.TraceIdentifier : correlationId;
        }

        /// <summary>
        /// Gets the best available client IP for audit logs.
        /// </summary>
        protected string GetClientIp()
        {
            var xff = Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(xff))
            {
                return xff.Split(',')[0].Trim();
            }

            return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }

        /// <summary>
        /// Emits a structured audit log record with correlation and requester metadata.
        /// </summary>
        protected void LogAudit(string action, string targetDn, string changeSummary)
        {
            logger.LogInformation(
                "AUDIT action={action} requester={requester} correlationId={correlationId} clientIp={clientIp} targetDn={targetDn} change={changeSummary}",
                action,
                requesterID ?? "unknown",
                GetCorrelationId(),
                GetClientIp(),
                targetDn ?? string.Empty,
                changeSummary ?? string.Empty
            );
        }
    }
}
