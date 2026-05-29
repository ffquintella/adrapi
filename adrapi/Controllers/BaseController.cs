using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using System.Linq;
using adrapi.Ldap;

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
        /// Resolves the LDAP directory (domain) for a request from the optional
        /// <c>{domain}</c> route segment. A null/empty segment means the default
        /// domain (preserving the legacy domain-less routes). Unknown domains yield
        /// a 404 and reserved/invalid names a 400.
        /// </summary>
        /// <returns>True when resolution succeeded; false with <paramref name="error"/> set otherwise.</returns>
        protected bool TryResolveDomain(string domain, out LdapConfig config, out ActionResult error)
        {
            config = null;
            error = null;

            var registry = LdapDomainRegistry.Instance;

            if (string.IsNullOrWhiteSpace(domain))
            {
                config = registry.GetConfig(null);
                return true;
            }

            if (LdapDomainRegistry.IsReservedName(domain))
            {
                logger.LogWarning("Rejected reserved domain name in route: {domain}", domain);
                error = BadRequest();
                return false;
            }

            if (!registry.IsKnownDomain(domain))
            {
                logger.LogWarning("Request for unknown LDAP domain: {domain}", domain);
                error = NotFound();
                return false;
            }

            config = registry.GetConfig(domain);
            return true;
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
