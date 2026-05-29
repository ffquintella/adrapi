using System;
using System.Net.Http;
using adrapi.Entra;
using adrapi.Ldap;

namespace adrapi.Directory
{
    /// <summary>
    /// Selects and builds the <see cref="IDirectoryProvider"/> for a request's
    /// target domain — the configuration switch between the LDAP and Entra ID
    /// backends.
    ///
    /// Selection is driven by the domain's configured <c>kind</c>
    /// (<see cref="LdapDomainRegistry.GetDomainKind"/>):
    /// <list type="bullet">
    /// <item><b>per request</b> — the <c>{domain}</c> route segment picks the domain,
    /// and thus its backend, for that call;</item>
    /// <item><b>per deployment</b> — the default domain's <c>kind</c> (and
    /// <c>ldap:defaultDomain</c>) decides the backend used by the legacy
    /// domain-less routes.</item>
    /// </list>
    /// Unknown domains throw (mirroring <see cref="LdapDomainRegistry.GetConfig"/>);
    /// callers that have already resolved the domain (e.g. via
    /// <c>BaseController.TryResolveDomain</c>) won't hit that path.
    /// </summary>
    public static class DirectoryProviderFactory
    {
        // Graph is HTTP/2-friendly and thread-safe; reuse one client process-wide
        // to avoid socket exhaustion. Per-domain auth lives on the request, not here.
        private static readonly HttpClient SharedHttpClient = new();

        /// <summary>Builds the provider for a route domain value (null/empty = default domain).</summary>
        public static IDirectoryProvider ForDomain(string domain)
        {
            var registry = LdapDomainRegistry.Instance;

            if (registry.IsEntraDomain(domain))
            {
                var entraConfig = EntraConfig.ForDomain(domain);
                var graphClient = new GraphClient(entraConfig, EntraTokenProvider.Instance, SharedHttpClient);
                return new GraphDirectoryProvider(entraConfig, graphClient);
            }

            return new LdapDirectoryProvider(registry.GetConfig(domain));
        }

        /// <summary>
        /// Builds the provider for an already-resolved <see cref="LdapConfig"/>
        /// (the common controller path, where the domain was validated up front).
        /// </summary>
        public static IDirectoryProvider ForLdapConfig(LdapConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            return new LdapDirectoryProvider(config);
        }
    }
}
