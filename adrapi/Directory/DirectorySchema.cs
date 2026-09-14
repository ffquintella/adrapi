using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace adrapi.Directory
{
    /// <summary>
    /// Where a single directory domain's settings live in the configuration tree,
    /// resolved by <see cref="DirectorySchema"/>. Backend-neutral: the
    /// <see cref="Kind"/> discriminator says which of the backend-specific paths
    /// is meaningful.
    /// </summary>
    public sealed class DirectoryDomainDescriptor
    {
        /// <summary>Domain name as configured (route segment, case-insensitive).</summary>
        public string Name { get; init; }

        /// <summary>Backend discriminator: <c>ldap</c>, <c>entraid</c>, ...</summary>
        public string Kind { get; init; }

        /// <summary>Configuration path of the domain block itself, whatever its kind.</summary>
        public string BasePath { get; init; }

        /// <summary>Configuration path of the LDAP settings block (null for non-LDAP kinds).</summary>
        public string LdapPath { get; init; }

        /// <summary>Configuration path of the Entra ID settings block (null for non-Entra kinds).</summary>
        public string EntraPath { get; init; }

        /// <summary>
        /// Deprecated configuration path that may still hold this domain's LDAP
        /// settings/secrets. Null when the domain is already read from a legacy path.
        /// </summary>
        public string LegacyLdapPath { get; init; }

        /// <summary>
        /// Deprecated configuration path that may still hold this domain's Entra
        /// secrets. Null when the domain is already read from a legacy path.
        /// </summary>
        public string LegacyEntraPath { get; init; }

        /// <summary>True when the domain was found under the deprecated <c>ldap</c> section.</summary>
        public bool IsLegacyLayout { get; init; }
    }

    /// <summary>
    /// Single source of truth for how directory domains are laid out in
    /// configuration.
    ///
    /// Current (1.10.0+) schema — backend-neutral, LDAP is just one <c>kind</c>:
    /// <code>
    /// "directories": {
    ///   "defaultDomain": "corp",
    ///   "domains": {
    ///     "corp":  { "kind": "ldap",    "ldap":  { "servers": [...], "ssl": true, ... } },
    ///     "cloud": { "kind": "entraid", "entra": { "tenantId": "...", ... } }
    ///   }
    /// }
    /// </code>
    ///
    /// Deprecated schema, still read when the new one is absent: the top-level
    /// <c>ldap</c> section is the default domain, extra domains live (flat) under
    /// <c>ldap:domains:{name}</c> and <c>ldap:defaultDomain</c> names the default.
    ///
    /// DEPRECATION: the legacy <c>ldap:domains</c> / <c>ldap:defaultDomain</c>
    /// layout and the legacy <c>ldap:domains:{name}:entra:*</c> secret paths are
    /// supported through the 1.x line and are **removed in 2.0.0**. Migrate with
    /// <c>adrapi-api-keys secret migrate-directories</c> plus the config rewrite
    /// documented in docs/MIGRATION_NOTES.md.
    /// </summary>
    public static class DirectorySchema
    {
        public const string SectionName = "directories";

        /// <summary>Deprecated root section. Removed in 2.0.0.</summary>
        public const string LegacySectionName = "ldap";

        public const string FallbackDefaultDomain = "default";

        /// <summary>Directory backend kinds a domain can declare via <c>kind</c>.</summary>
        public const string KindLdap = "ldap";
        public const string KindEntraId = "entraid";

        private static readonly ConcurrentDictionary<string, byte> WarnedOnce = new();

        private static IConfiguration Config => ConfigurationManager.Instance.Config;

        private static bool Exists(string path)
        {
            var section = Config?.GetSection(path);
            return section != null && section.Exists();
        }

        /// <summary>True when the deployment declares domains under <c>directories:domains</c>.</summary>
        public static bool UsesNewLayout => Exists($"{SectionName}:domains");

        /// <summary>
        /// Emits a deprecation warning once per distinct message for the process.
        /// Legacy configuration must never fail silently, but it must not spam
        /// the log on every request either.
        /// </summary>
        public static void WarnLegacy(string message)
        {
            if (WarnedOnce.TryAdd(message, 0))
            {
                NLog.LogManager.GetCurrentClassLogger().Warn(message);
            }
        }

        /// <summary>Test seam: forget which deprecation warnings were already emitted.</summary>
        public static void ResetWarnings() => WarnedOnce.Clear();

        /// <summary>
        /// Name of the default domain: <c>directories:defaultDomain</c>, else the
        /// deprecated <c>ldap:defaultDomain</c>, else <see cref="FallbackDefaultDomain"/>.
        /// </summary>
        public static string DefaultDomainName
        {
            get
            {
                var configured = Config?.GetValue<string>($"{SectionName}:defaultDomain");
                if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();

                var legacy = Config?.GetValue<string>($"{LegacySectionName}:defaultDomain");
                if (!string.IsNullOrWhiteSpace(legacy))
                {
                    WarnLegacy(
                        "Configuration uses the deprecated 'ldap:defaultDomain' key. Move it to " +
                        "'directories:defaultDomain'; the legacy key is removed in 2.0.0.");
                    return legacy.Trim();
                }

                return FallbackDefaultDomain;
            }
        }

        public static bool IsDefaultDomain(string domain)
            => string.IsNullOrWhiteSpace(domain)
               || string.Equals(domain.Trim(), DefaultDomainName, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// All configured domain names: the default plus every child of
        /// <c>directories:domains</c> and of the deprecated <c>ldap:domains</c>.
        /// </summary>
        public static IEnumerable<string> EnumerateDomains()
        {
            var names = new List<string> { DefaultDomainName };
            var seen = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

            void AddChildren(string path)
            {
                var section = Config?.GetSection(path);
                if (section == null || !section.Exists()) return;
                foreach (var child in section.GetChildren())
                {
                    if (seen.Add(child.Key)) names.Add(child.Key);
                }
            }

            AddChildren($"{SectionName}:domains");
            AddChildren($"{LegacySectionName}:domains");

            return names;
        }

        /// <summary>
        /// Locates a domain's configuration, preferring the new layout and falling
        /// back to the deprecated one. Returns null when the domain is unknown.
        /// </summary>
        public static DirectoryDomainDescriptor Describe(string domain)
        {
            var name = string.IsNullOrWhiteSpace(domain) ? DefaultDomainName : domain.Trim();
            var isDefault = IsDefaultDomain(name);

            // Pure legacy deployment: the default domain IS the top-level `ldap`
            // section, even when a same-named child exists under `ldap:domains`.
            // Resolving it any other way would change behaviour on upgrade.
            if (isDefault && !UsesNewLayout)
            {
                return Exists(LegacySectionName) ? LegacyDefault(name) : null;
            }

            var newBase = $"{SectionName}:domains:{name}";
            if (Exists(newBase)) return FromNewLayout(name, newBase, isDefault);

            var legacyBase = $"{LegacySectionName}:domains:{name}";
            if (Exists(legacyBase))
            {
                WarnLegacy(
                    $"Directory domain '{name}' is configured under the deprecated 'ldap:domains' section. " +
                    $"Move it to 'directories:domains:{name}' (with a 'kind' discriminator); " +
                    "the legacy layout is removed in 2.0.0.");
                return FromLegacyDomain(name, legacyBase);
            }

            // New layout present but the default domain has no entry of its own:
            // the top-level `ldap` section still backs it.
            if (isDefault && Exists(LegacySectionName)) return LegacyDefault(name);

            return null;
        }

        private static DirectoryDomainDescriptor FromNewLayout(string name, string basePath, bool isDefault)
        {
            var kind = Config?.GetValue<string>($"{basePath}:kind");
            kind = string.IsNullOrWhiteSpace(kind) ? KindLdap : kind.Trim().ToLowerInvariant();

            // A `ldap` sub-object is the documented shape; a flat block (settings
            // straight on the domain) is accepted so a legacy block can be moved
            // across verbatim.
            var ldapPath = Exists($"{basePath}:ldap") ? $"{basePath}:ldap" : basePath;
            var legacyDomainBase = $"{LegacySectionName}:domains:{name}";

            return new DirectoryDomainDescriptor
            {
                Name = name,
                Kind = kind,
                BasePath = basePath,
                LdapPath = kind == KindLdap ? ldapPath : null,
                EntraPath = kind == KindEntraId ? $"{basePath}:entra" : null,
                LegacyLdapPath = kind != KindLdap ? null
                    : Exists(legacyDomainBase) ? legacyDomainBase
                    : isDefault && Exists(LegacySectionName) ? LegacySectionName
                    : null,
                LegacyEntraPath = kind == KindEntraId ? $"{legacyDomainBase}:entra" : null,
                IsLegacyLayout = false,
            };
        }

        private static DirectoryDomainDescriptor FromLegacyDomain(string name, string basePath)
        {
            var kind = Config?.GetValue<string>($"{basePath}:kind");
            kind = string.IsNullOrWhiteSpace(kind) ? KindLdap : kind.Trim().ToLowerInvariant();

            return new DirectoryDomainDescriptor
            {
                Name = name,
                Kind = kind,
                BasePath = basePath,
                // Legacy named domains are flat: LDAP settings sit on the domain block.
                LdapPath = kind == KindLdap ? basePath : null,
                EntraPath = kind == KindEntraId ? $"{basePath}:entra" : null,
                IsLegacyLayout = true,
            };
        }

        private static DirectoryDomainDescriptor LegacyDefault(string name)
        {
            WarnLegacy(
                $"Default directory domain '{name}' is backed by the deprecated top-level 'ldap' " +
                $"configuration section. Move it to 'directories:domains:{name}' with " +
                "\"kind\": \"ldap\"; the top-level section is removed in 2.0.0.");

            return new DirectoryDomainDescriptor
            {
                Name = name,
                // The top-level `ldap` section has always meant an LDAP directory.
                Kind = KindLdap,
                BasePath = LegacySectionName,
                LdapPath = LegacySectionName,
                IsLegacyLayout = true,
            };
        }
    }
}
