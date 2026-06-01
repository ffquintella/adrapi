using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using System.Linq;
using adrapi.Directory;
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
        /// Resolves the directory for an OU operation. Behaves like
        /// <see cref="TryResolveDomain"/> but additionally rejects Entra ID-backed
        /// domains: Entra ID has no OU object (administrative units are a separate
        /// Microsoft Graph concept), so OU endpoints are LDAP/AD-only.
        /// </summary>
        protected bool TryResolveLdapDomain(string domain, out LdapConfig config, out ActionResult error)
        {
            if (!TryResolveDomain(domain, out config, out error))
            {
                return false;
            }

            if (LdapDomainRegistry.Instance.IsEntraDomain(domain))
            {
                logger.LogWarning("Rejected OU operation on Entra ID-backed domain: {domain}", domain);
                config = null;
                error = BadRequest(
                    "Organizational unit operations are not supported on Entra ID-backed domains. " +
                    "Entra ID has no OU object; administrative units are a separate Microsoft Graph concept. " +
                    "Use an LDAP/AD-backed domain for OU operations.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Opens an ambient <see cref="Directory.DirectoryOperationContext"/> scope
        /// carrying the requester, correlation id, and client IP, so directory
        /// backends (e.g. the Graph client) stamp the same audit fields on their
        /// operation logs. Dispose the returned scope when the request completes
        /// (e.g. <c>using (BeginDirectoryScope()) { ... }</c>). Call
        /// <see cref="ProcessRequest"/> first so the requester is populated.
        /// </summary>
        protected IDisposable BeginDirectoryScope()
            => Directory.DirectoryOperationContext.BeginScope(new Directory.DirectoryOperationContext
            {
                Requester = string.IsNullOrWhiteSpace(requesterID) ? "unknown" : requesterID,
                CorrelationId = GetCorrelationId(),
                ClientIp = GetClientIp(),
            });

        /// <summary>True when the request's domain is backed by Entra ID (Microsoft Graph).</summary>
        protected bool IsEntraDomain(string domain) => LdapDomainRegistry.Instance.IsEntraDomain(domain);

        /// <summary>
        /// Resolves the directory provider for a domain. Virtual so tests can
        /// substitute a fake without a live backend.
        /// </summary>
        protected virtual IDirectoryProvider ResolveProvider(string domain)
            => DirectoryProviderFactory.ForDomain(domain);

        /// <summary>
        /// Runs an operation against the domain's directory provider inside an audit
        /// scope, mapping any provider/Graph exception to a clear 4xx/5xx
        /// <see cref="ProblemDetails"/> via <see cref="DirectoryErrorMapper"/>.
        /// </summary>
        protected async System.Threading.Tasks.Task<ActionResult> RunWithProviderAsync(
            string domain, Func<IDirectoryProvider, System.Threading.Tasks.Task<ActionResult>> operation)
        {
            using (BeginDirectoryScope())
            {
                try
                {
                    return await operation(ResolveProvider(domain));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Directory provider operation failed on domain {domain}", domain ?? "<default>");
                    return DirectoryErrorMapper.ToProblem(ex);
                }
            }
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
