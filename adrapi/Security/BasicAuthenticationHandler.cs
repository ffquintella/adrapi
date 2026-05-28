using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace adrapi.Security
{
    /// <summary>
    /// Authenticates requests by reading the `api-key: keyID:secretKey` header,
    /// looking up the key in <see cref="ApiKeyManager"/> by keyID, verifying the
    /// supplied secret against the stored Argon2id hash, and (if successful)
    /// emitting a ClaimsPrincipal with the key's claims.
    /// </summary>
    public class BasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly ILogger _logger;

        public BasicAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
            _logger = logger.CreateLogger("BasicAuthenticationHandler");
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("api-key"))
                return Task.FromResult(AuthenticateResult.Fail("Missing api-key Header"));

            string apiKeyHeader = Request.Headers["api-key"];
            var sep = apiKeyHeader?.IndexOf(':') ?? -1;
            if (sep <= 0 || sep >= apiKeyHeader.Length - 1)
                return Task.FromResult(AuthenticateResult.Fail("Invalid api-key format"));

            var keyId = apiKeyHeader.Substring(0, sep);
            var secret = apiKeyHeader.Substring(sep + 1);
            var remoteIp = Request.HttpContext.Connection.RemoteIpAddress?.ToString();

            var record = ApiKeyManager.Authenticate(keyId, secret);
            if (record == null)
            {
                _logger.LogDebug("Invalid api-key (ip={ip}, keyID={keyId}).", remoteIp, keyId);
                return Task.FromResult(AuthenticateResult.Fail("Invalid api-key"));
            }

            if (!IsIpAuthorized(remoteIp, record.authorizedIP))
            {
                _logger.LogWarning("api-key keyID={keyId} used from unauthorized ip={ip} (allowed={allowed}).",
                    keyId, remoteIp, record.authorizedIP);
                return Task.FromResult(AuthenticateResult.Fail("Unauthorized source IP"));
            }

            const string Issuer = "https://fgv.br";
            var claims = new System.Collections.Generic.List<Claim>
            {
                new Claim(ClaimTypes.Name, record.keyID, ClaimValueTypes.String, Issuer),
            };
            if (record.claims != null)
            {
                foreach (var c in record.claims)
                    claims.Add(new Claim(c, "true", ClaimValueTypes.Boolean));
            }

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        /// <summary>
        /// Accepts an exact IP match or, when <c>authorizedIP</c> is "0.0.0.0" /
        /// "*" / empty, allows any source. (The narrower form is strongly
        /// preferred; the wildcards exist for parity with legacy deployments.)
        /// </summary>
        private static bool IsIpAuthorized(string remoteIp, string authorizedIp)
        {
            if (string.IsNullOrWhiteSpace(authorizedIp) || authorizedIp == "*" || authorizedIp == "0.0.0.0")
                return true;
            if (string.IsNullOrEmpty(remoteIp)) return false;
            return string.Equals(remoteIp, authorizedIp, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
