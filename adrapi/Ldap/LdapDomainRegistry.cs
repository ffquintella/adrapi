using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.Configuration;
using adrapi.Directory;
using adrapi.domain.Exceptions;

namespace adrapi.Ldap
{
    /// <summary>
    /// Resolves and caches one directory configuration per domain.
    ///
    /// Where a domain's settings live in the configuration tree — and which of
    /// the new <c>directories</c> / deprecated <c>ldap</c> layouts they came from
    /// — is decided by <see cref="DirectorySchema"/>; this registry only turns a
    /// route domain value into a typed, cached config and answers the routing
    /// questions the controllers ask.
    ///
    /// The name is historical (LDAP was the only backend): a domain may be backed
    /// by any <c>kind</c>. Mirrors the singleton style of the other managers so it
    /// can be reached from the static manager chain without DI wiring.
    /// </summary>
    public class LdapDomainRegistry
    {
        public const string FallbackDefaultDomain = DirectorySchema.FallbackDefaultDomain;

        /// <summary>Directory backend kinds a domain can declare via <c>kind</c>.</summary>
        public const string KindLdap = DirectorySchema.KindLdap;
        public const string KindEntraId = DirectorySchema.KindEntraId;

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

        /// <summary>
        /// Drops the per-domain <see cref="LdapConfig"/> cache so subsequent
        /// resolutions re-read the current configuration. Useful after a config
        /// reload and for test isolation (configs are otherwise cached for the
        /// process lifetime).
        /// </summary>
        public void ClearCache() => cache.Clear();

        /// <summary>
        /// Name of the default domain, from <c>directories:defaultDomain</c> (or the
        /// deprecated <c>ldap:defaultDomain</c>), else <see cref="FallbackDefaultDomain"/>.
        /// </summary>
        public static string DefaultDomainName => DirectorySchema.DefaultDomainName;

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

        public static bool IsReservedName(string domain)
        {
            return !string.IsNullOrWhiteSpace(domain) && ReservedNames.Contains(domain.Trim());
        }

        /// <summary>All configured domain names: the default plus every named domain.</summary>
        public IEnumerable<string> EnumerateDomains() => DirectorySchema.EnumerateDomains();

        /// <summary>
        /// Backend kind for a domain (<see cref="KindLdap"/> or <see cref="KindEntraId"/>),
        /// from the domain's <c>kind</c> discriminator. Unknown domains — and the
        /// legacy top-level <c>ldap</c> default — are LDAP.
        /// </summary>
        public string GetDomainKind(string domain)
            => DirectorySchema.Describe(domain)?.Kind ?? KindLdap;

        /// <summary>True when the domain is backed by Entra ID rather than LDAP.</summary>
        public bool IsEntraDomain(string domain)
            => string.Equals(GetDomainKind(domain), KindEntraId, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True when <paramref name="domain"/> is null/empty (default), the default
        /// domain name, or a configured named domain in either layout.
        /// </summary>
        public bool IsKnownDomain(string domain)
        {
            return DirectorySchema.IsDefaultDomain(domain) || DirectorySchema.Describe(domain) != null;
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
                var descriptor = DirectorySchema.Describe(domain);

                if (descriptor == null)
                {
                    // No configuration at all (isolated unit tests) still yields a
                    // defaulted config for the default domain, as it always has.
                    if (DirectorySchema.IsDefaultDomain(domain))
                    {
                        return LdapConfig.ForSection(DirectorySchema.LegacySectionName, key);
                    }

                    throw new WrongParameterException($"Unknown directory domain '{domain}'.");
                }

                // Non-LDAP domains still hand back a (stamped, mostly empty)
                // LdapConfig: controllers resolve the domain before they know the
                // backend, and only then branch to the Graph provider.
                return LdapConfig.ForSection(
                    descriptor.LdapPath ?? descriptor.BasePath, key, descriptor.LegacyLdapPath);
            });
        }
    }
}
