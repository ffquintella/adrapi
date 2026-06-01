using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using adrapi.Ldap;

namespace adrapi.Entra
{
    /// <summary>
    /// App-registration configuration for an Entra ID-backed domain.
    ///
    /// Lives under <c>ldap:domains:{name}:entra</c> (the domain's <c>kind</c> must
    /// be <c>entraid</c>). The client secret is read from configuration, which is
    /// overlaid by the encrypted secret store (<see cref="SqliteSecretsConfigurationSource"/>),
    /// so it is never required to sit in plaintext appsettings. A client
    /// certificate is supported as an alternative to a secret.
    /// </summary>
    public class EntraConfig
    {
        public const string DefaultAuthorityHost = "https://login.microsoftonline.com";
        public const string DefaultGraphBaseUrl = "https://graph.microsoft.com/v1.0";
        public const string DefaultScope = "https://graph.microsoft.com/.default";

        /// <summary>
        /// Authority placeholders that select a multi-tenant/consumer endpoint.
        /// These are invalid for the OAuth2 client-credentials (app-only) flow,
        /// which requires a tenant-specific authority.
        /// </summary>
        private static readonly string[] MultiTenantPlaceholders = { "common", "organizations", "consumers" };

        /// <summary>Stable per-domain key (matches <see cref="LdapConfig.DomainKey"/>).</summary>
        public string DomainKey { get; set; }
        public string TenantId { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string CertificatePath { get; set; }
        public string CertificatePassword { get; set; }
        public string AuthorityHost { get; set; } = DefaultAuthorityHost;
        public string GraphBaseUrl { get; set; } = DefaultGraphBaseUrl;
        public string[] Scopes { get; set; } = { DefaultScope };

        /// <summary>
        /// Application permissions (app roles) that have been granted admin consent
        /// for this app registration. Used to validate that a request's policy
        /// (Reading/Writting) is covered — see <see cref="EntraScopeMap"/>.
        /// </summary>
        public string[] GrantedPermissions { get; set; } = Array.Empty<string>();

        public string Authority => $"{(AuthorityHost ?? DefaultAuthorityHost).TrimEnd('/')}/{TenantId}";
        public bool HasCertificate => !string.IsNullOrWhiteSpace(CertificatePath);
        public bool HasClientSecret => !string.IsNullOrWhiteSpace(ClientSecret);

        /// <summary>Builds a config from a raw <c>entra</c> configuration section.</summary>
        public static EntraConfig FromSection(IConfigurationSection section, string domainKey)
        {
            var cfg = new EntraConfig { DomainKey = domainKey };
            if (section == null || !section.Exists())
            {
                return cfg;
            }

            cfg.TenantId = section.GetValue<string>("tenantId");
            cfg.ClientId = section.GetValue<string>("clientId");
            cfg.ClientSecret = section.GetValue<string>("clientSecret");
            cfg.CertificatePath = section.GetValue<string>("certificatePath");
            cfg.CertificatePassword = section.GetValue<string>("certificatePassword");

            var authority = section.GetValue<string>("authorityHost");
            if (!string.IsNullOrWhiteSpace(authority)) cfg.AuthorityHost = authority;

            var graph = section.GetValue<string>("graphBaseUrl");
            if (!string.IsNullOrWhiteSpace(graph)) cfg.GraphBaseUrl = graph;

            var scopes = section.GetSection("scopes").Get<string[]>();
            if (scopes != null && scopes.Length > 0) cfg.Scopes = scopes;

            var granted = section.GetSection("grantedPermissions").Get<string[]>();
            if (granted != null) cfg.GrantedPermissions = granted;

            return cfg;
        }

        /// <summary>
        /// Resolves the Entra config for a named domain from the runtime
        /// configuration (<see cref="ConfigurationManager"/>).
        /// </summary>
        public static EntraConfig ForDomain(string domain)
        {
            var key = LdapDomainRegistry.NormalizeKey(domain);
            var config = ConfigurationManager.Instance.Config;
            var section = config?.GetSection($"ldap:domains:{domain}:entra");
            return FromSection(section, key);
        }

        /// <summary>Returns config errors (empty when valid).</summary>
        public IEnumerable<string> Validate()
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(TenantId)) errors.Add("entra.tenantId is required.");
            if (string.IsNullOrWhiteSpace(ClientId)) errors.Add("entra.clientId is required.");
            if (!HasClientSecret && !HasCertificate)
                errors.Add("entra requires either clientSecret or certificatePath.");
            if (HasClientSecret && HasCertificate)
                errors.Add("entra has both clientSecret and certificatePath; configure exactly one.");
            if (Scopes == null || Scopes.Length == 0)
                errors.Add("entra.scopes must contain at least one scope (default 'https://graph.microsoft.com/.default').");

            // Client-credentials (app-only) requires a tenant-specific authority.
            // A multi-tenant app registration is supported by configuring one
            // adrapi domain per customer tenant, each with that tenant's GUID or
            // verified domain — never 'common'/'organizations'/'consumers'.
            if (!string.IsNullOrWhiteSpace(TenantId)
                && MultiTenantPlaceholders.Contains(TenantId.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"entra.tenantId '{TenantId}' is invalid for the client-credentials flow. " +
                    "Use a specific tenant GUID or verified domain; for a multi-tenant app, configure one domain per tenant.");
            }

            return errors;
        }
    }
}
