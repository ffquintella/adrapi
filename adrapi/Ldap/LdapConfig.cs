using System;
using System.Configuration;
using Microsoft.Extensions.Configuration;

namespace adrapi.Ldap
{
    public class LdapConfig
    {
        public bool ssl { get; set; }
        public string bindDn { get; set; }
        public string bindCredentials { get; set; }
        public string searchBase { get; set; }
        public string searchFilter { get; set; }
        public string adminCn { get; set; }
        public short poolSize { get; set; }
        public int maxResults { get; set; }
        public string[] servers { get; set; }
        public string trustedCertificatesFile { get; set; }

        /// <summary>
        /// Stable key identifying which directory (domain) this config targets.
        /// Used to bucket connection pools per-domain. Stamped by
        /// <see cref="LdapDomainRegistry"/>; the parameterless ctor stamps the
        /// default domain so legacy callers share the default pool bucket.
        /// </summary>
        public string DomainKey { get; set; }

        private LdapConfig(bool deferLoad)
        {
            // Marker ctor used by ForSection; skips automatic load so the caller
            // controls which configuration section is read.
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="T:adrapi.Ldap.LdapConfig"/> class
        /// from the default domain's configuration.
        /// </summary>
        public LdapConfig()
        {
            var descriptor = Directory.DirectorySchema.Describe(null);
            LoadFromSection(
                descriptor?.LdapPath ?? Directory.DirectorySchema.LegacySectionName,
                descriptor?.LegacyLdapPath);
            DomainKey = LdapDomainRegistry.NormalizeKey(null);
        }

        /// <summary>
        /// Builds an <see cref="LdapConfig"/> from an arbitrary configuration section
        /// (e.g. <c>directories:domains:corp:ldap</c>, or the deprecated <c>ldap</c> /
        /// <c>ldap:domains:lab</c>), stamping the supplied domain key for pool bucketing.
        /// </summary>
        /// <param name="legacySectionPath">
        /// Optional deprecated section consulted per-setting when the primary one
        /// leaves a value unset. This is what lets a deployment move its config to
        /// <c>directories</c> while its per-domain secrets (notably
        /// <c>bindCredentials</c>) are still stored under the old key paths.
        /// Removed in 2.0.0 along with the legacy layout.
        /// </param>
        public static LdapConfig ForSection(string sectionPath, string domainKey, string legacySectionPath = null)
        {
            var cfg = new LdapConfig(true);
            cfg.LoadFromSection(sectionPath, legacySectionPath);
            cfg.DomainKey = domainKey;
            return cfg;
        }

        private void LoadFromSection(string sectionPath, string legacySectionPath = null)
        {
            var config = ConfigurationManager.Instance.Config;
            if (config == null)
            {
                // No configuration loaded (e.g. isolated unit tests). Leave defaults.
                trustedCertificatesFile = "cfg/ldap-trusted-certs.json";
                return;
            }

            var section = config.GetSection(sectionPath);
            var legacy = string.IsNullOrEmpty(legacySectionPath) || legacySectionPath == sectionPath
                ? null
                : config.GetSection(legacySectionPath);

            string Value(string name)
            {
                var value = section.GetValue<string>(name);
                if (!string.IsNullOrEmpty(value)) return value;

                var fallback = legacy?.GetValue<string>(name);
                if (string.IsNullOrEmpty(fallback)) return value;

                Directory.DirectorySchema.WarnLegacy(
                    $"'{sectionPath}:{name}' is unset; falling back to the deprecated " +
                    $"'{legacySectionPath}:{name}'. Re-key it (see docs/MIGRATION_NOTES.md); " +
                    "the legacy path is removed in 2.0.0.");
                return fallback;
            }

            servers = section.GetSection("servers").Get<string[]>()
                ?? legacy?.GetSection("servers").Get<string[]>();
            ssl = section.GetValue<bool>("ssl");
            poolSize = section.GetValue<short>("poolSize");
            bindDn = Value("bindDn");
            bindCredentials = Value("bindCredentials");
            searchBase = section.GetValue<string>("searchBase");
            searchFilter = section.GetValue<string>("searchFilter");
            maxResults = section.GetValue<int>("maxResults");
            adminCn = section.GetValue<string>("adminCn");
            trustedCertificatesFile = section.GetValue<string>("trustedCertificatesFile")
                ?? "cfg/ldap-trusted-certs.json";
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="T:adrapi.Ldap.LdapConfig"/> class.
        /// </summary>
        /// <param name="servers">Servers.</param>
        /// <param name="poolSize">Pool size.</param>
        /// <param name="bindDn">Bind dn.</param>
        /// <param name="bindCredentials">Bind credentials.</param>
        /// <param name="searchBase">Search base.</param>
        /// <param name="searchFilter">Search filter.</param>
        /// <param name="adminCn">Admin cn.</param>
        public LdapConfig(String[] servers, bool ssl, int maxResults, short poolSize, string bindDn, string bindCredentials, string searchBase, string searchFilter, string adminCn)
        {

            this.servers = servers;
            this.ssl = ssl;
            this.maxResults = maxResults;
            this.poolSize = poolSize;
            this.bindDn = bindDn;
            this.bindCredentials = bindCredentials;
            this.searchBase = searchBase;
            this.searchFilter = searchFilter;
            this.adminCn = adminCn;
            this.trustedCertificatesFile = "cfg/ldap-trusted-certs.json";


        }
    }
}
