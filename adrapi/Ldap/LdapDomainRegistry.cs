using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.Configuration;
using adrapi.domain.Exceptions;

namespace adrapi.Ldap
{
    /// <summary>
    /// Resolves and caches one <see cref="LdapConfig"/> per directory (domain).
    ///
    /// The legacy top-level <c>ldap</c> configuration section is the default
    /// domain. Additional domains live under <c>ldap:domains:{name}</c> with the
    /// same shape. <c>ldap:defaultDomain</c> names the default domain (for explicit
    /// selection); when absent it is <see cref="FallbackDefaultDomain"/>.
    ///
    /// Mirrors the singleton style of the other managers so it can be reached from
    /// the static manager chain without DI wiring.
    /// </summary>
    public class LdapDomainRegistry
    {
        public const string FallbackDefaultDomain = "default";

        /// <summary>Directory backend kinds a domain can declare via <c>kind</c>.</summary>
        public const string KindLdap = "ldap";
        public const string KindEntraId = "entraid";

        /// <summary>
        /// Resource names that may not double as domain names — they would make
        /// routes like <c>/api/users/users</c> ambiguous.
        /// </summary>
        private static readonly HashSet<string> ReservedNames =
            new(StringComparer.OrdinalIgnoreCase) { "users", "groups", "ous", "infos" };

        #region SINGLETON

        private static readonly Lazy<LdapDomainRegistry> lazy = new(() => new LdapDomainRegistry());

        public static LdapDomainRegistry Instance => lazy.Value;

        private LdapDomainRegistry()
        {
        }

        #endregion

        private readonly ConcurrentDictionary<string, LdapConfig> cache =
            new(StringComparer.OrdinalIgnoreCase);

        private static IConfiguration Config => ConfigurationManager.Instance.Config;

        /// <summary>
        /// Name of the default domain, from <c>ldap:defaultDomain</c> or
        /// <see cref="FallbackDefaultDomain"/>.
        /// </summary>
        public static string DefaultDomainName
        {
            get
            {
                var configured = Config?.GetValue<string>("ldap:defaultDomain");
                return string.IsNullOrWhiteSpace(configured)
                    ? FallbackDefaultDomain
                    : configured.Trim();
            }
        }

        /// <summary>
        /// Normalizes a route domain value to the stable key used for config
        /// caching and connection-pool bucketing. Null/empty maps to the default.
        /// </summary>
        public static string NormalizeKey(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain))
            {
                return DefaultDomainName.ToLowerInvariant();
            }

            return domain.Trim().ToLowerInvariant();
        }

        private static bool IsDefault(string domain)
        {
            return string.IsNullOrWhiteSpace(domain)
                || string.Equals(domain.Trim(), DefaultDomainName, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsReservedName(string domain)
        {
            return !string.IsNullOrWhiteSpace(domain) && ReservedNames.Contains(domain.Trim());
        }

        /// <summary>
        /// All configured domain names: the default plus any under <c>ldap:domains</c>.
        /// </summary>
        public IEnumerable<string> EnumerateDomains()
        {
            var names = new List<string> { DefaultDomainName };

            var domains = Config?.GetSection("ldap:domains");
            if (domains != null && domains.Exists())
            {
                foreach (var child in domains.GetChildren())
                {
                    if (!string.Equals(child.Key, DefaultDomainName, StringComparison.OrdinalIgnoreCase))
                    {
                        names.Add(child.Key);
                    }
                }
            }

            return names;
        }

        /// <summary>
        /// Backend kind for a domain (<see cref="KindLdap"/> or <see cref="KindEntraId"/>),
        /// from <c>ldap:domains:{name}:kind</c>. The default domain is always LDAP.
        /// </summary>
        public string GetDomainKind(string domain)
        {
            if (IsDefault(domain))
            {
                return KindLdap;
            }

            var kind = Config?.GetValue<string>($"ldap:domains:{domain.Trim()}:kind");
            return string.IsNullOrWhiteSpace(kind) ? KindLdap : kind.Trim().ToLowerInvariant();
        }

        /// <summary>True when the domain is backed by Entra ID rather than LDAP.</summary>
        public bool IsEntraDomain(string domain)
            => string.Equals(GetDomainKind(domain), KindEntraId, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True when <paramref name="domain"/> is null/empty (default), the default
        /// domain name, or a key present under <c>ldap:domains</c>.
        /// </summary>
        public bool IsKnownDomain(string domain)
        {
            if (IsDefault(domain))
            {
                return true;
            }

            var section = Config?.GetSection($"ldap:domains:{domain.Trim()}");
            return section != null && section.Exists();
        }

        /// <summary>
        /// Resolves the <see cref="LdapConfig"/> for a route domain value (null/empty
        /// = default). Cached per normalized key. Throws for unknown domains.
        /// </summary>
        public LdapConfig GetConfig(string domain)
        {
            var key = NormalizeKey(domain);

            return cache.GetOrAdd(key, _ =>
            {
                if (IsDefault(domain))
                {
                    return LdapConfig.ForSection("ldap", key);
                }

                if (!IsKnownDomain(domain))
                {
                    throw new WrongParameterException($"Unknown LDAP domain '{domain}'.");
                }

                return LdapConfig.ForSection($"ldap:domains:{domain.Trim()}", key);
            });
        }
    }
}
