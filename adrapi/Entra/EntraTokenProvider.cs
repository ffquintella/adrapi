using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;
using adrapi.Ldap;
using adrapi.domain.Exceptions;

namespace adrapi.Entra
{
    /// <summary>An acquired Graph access token and its expiry.</summary>
    public record EntraToken(string AccessToken, DateTimeOffset ExpiresOn);

    public interface IEntraTokenProvider
    {
        Task<EntraToken> AcquireTokenAsync(EntraConfig config, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Acquires Microsoft Graph access tokens via the OAuth2 client-credentials
    /// flow (MSAL confidential client), one app per domain.
    ///
    /// MSAL maintains an in-memory app token cache per <see cref="IConfidentialClientApplication"/>
    /// and transparently refreshes the client-credentials token when it nears
    /// expiry, so callers can request a token on every operation cheaply.
    /// </summary>
    public class EntraTokenProvider : IEntraTokenProvider
    {
        public NLog.Logger logger;

        #region SINGLETON

        private static readonly Lazy<EntraTokenProvider> lazy = new(() => new EntraTokenProvider());

        public static EntraTokenProvider Instance => lazy.Value;

        private EntraTokenProvider()
        {
            logger = NLog.LogManager.GetCurrentClassLogger();
        }

        #endregion

        // One confidential-client app per domain key; MSAL caches tokens within it.
        private readonly ConcurrentDictionary<string, IConfidentialClientApplication> apps = new();

        public async Task<EntraToken> AcquireTokenAsync(EntraConfig config, CancellationToken cancellationToken = default)
        {
            if (config == null) throw new NullException("Entra config cannot be null");

            var errors = config.Validate().ToList();
            if (errors.Count > 0)
            {
                throw new WrongParameterException("Invalid Entra configuration: " + string.Join(" ", errors));
            }

            var key = LdapDomainRegistry.NormalizeKey(config.DomainKey);
            var app = apps.GetOrAdd(key, _ => BuildApp(config));

            try
            {
                var result = await app
                    .AcquireTokenForClient(config.Scopes)
                    .ExecuteAsync(cancellationToken);

                logger.Debug("Acquired Graph token for domain '{domain}'; expires {expires}.", key, result.ExpiresOn);
                return new EntraToken(result.AccessToken, result.ExpiresOn);
            }
            catch (MsalServiceException ex)
            {
                // AADSTS errors (bad secret, missing consent, wrong tenant, etc.).
                logger.Error(ex, "Entra token acquisition failed for domain '{domain}'.", key);
                throw new InvalidCredentialsException(
                    $"Entra token acquisition failed for domain '{key}': {ex.Message}");
            }
        }

        private static IConfidentialClientApplication BuildApp(EntraConfig config)
        {
            var builder = ConfidentialClientApplicationBuilder
                .Create(config.ClientId)
                .WithAuthority($"{config.AuthorityHost.TrimEnd('/')}/{config.TenantId}");

            if (config.HasCertificate)
            {
                builder = builder.WithCertificate(LoadCertificate(config));
            }
            else
            {
                builder = builder.WithClientSecret(config.ClientSecret);
            }

            return builder.Build();
        }

        private static X509Certificate2 LoadCertificate(EntraConfig config)
        {
            try
            {
                return X509CertificateLoader.LoadPkcs12FromFile(config.CertificatePath, config.CertificatePassword);
            }
            catch (Exception ex)
            {
                throw new WrongParameterException(
                    $"Failed to load Entra client certificate '{config.CertificatePath}': {ex.Message}");
            }
        }
    }
}
