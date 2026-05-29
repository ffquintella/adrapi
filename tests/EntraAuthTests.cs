using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Xunit;
using adrapi;
using adrapi.Entra;
using adrapi.Ldap;
using adrapi.domain.Exceptions;

namespace tests
{
    /// <summary>
    /// Stage 2 (Entra ID auth) unit tests: config binding, policy→app-role
    /// mapping, domain-kind detection, and token-provider input validation.
    /// No network calls — real token acquisition is covered by a gated
    /// integration test (ADRAPI_RUN_ENTRA_INTEGRATION).
    /// </summary>
    public class EntraAuthTests
    {
        private static IConfiguration BuildConfig(Dictionary<string, string> values)
            => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        private static IConfigurationSection EntraSection(Dictionary<string, string> values)
            => BuildConfig(values).GetSection("ldap:domains:cloud:entra");

        [Fact]
        public void FromSection_MapsFields_AndDefaults()
        {
            var section = EntraSection(new Dictionary<string, string>
            {
                ["ldap:domains:cloud:entra:tenantId"] = "tenant-1",
                ["ldap:domains:cloud:entra:clientId"] = "client-1",
                ["ldap:domains:cloud:entra:clientSecret"] = "shh",
                ["ldap:domains:cloud:entra:grantedPermissions:0"] = "User.Read.All",
                ["ldap:domains:cloud:entra:grantedPermissions:1"] = "Group.Read.All",
            });

            var cfg = EntraConfig.FromSection(section, "cloud");

            Assert.Equal("cloud", cfg.DomainKey);
            Assert.Equal("tenant-1", cfg.TenantId);
            Assert.Equal("client-1", cfg.ClientId);
            Assert.True(cfg.HasClientSecret);
            Assert.False(cfg.HasCertificate);
            // Defaults applied when not configured.
            Assert.Equal(EntraConfig.DefaultAuthorityHost, cfg.AuthorityHost);
            Assert.Equal(EntraConfig.DefaultGraphBaseUrl, cfg.GraphBaseUrl);
            Assert.Equal(new[] { EntraConfig.DefaultScope }, cfg.Scopes);
            Assert.Equal("https://login.microsoftonline.com/tenant-1", cfg.Authority);
            Assert.Equal(2, cfg.GrantedPermissions.Length);
        }

        [Fact]
        public void Validate_FlagsMissingTenantClientAndCredential()
        {
            var cfg = EntraConfig.FromSection(EntraSection(new Dictionary<string, string>()), "cloud");
            var errors = cfg.Validate().ToList();

            Assert.Contains(errors, e => e.Contains("tenantId"));
            Assert.Contains(errors, e => e.Contains("clientId"));
            Assert.Contains(errors, e => e.Contains("clientSecret or certificatePath"));
        }

        [Fact]
        public void Validate_FlagsBothCredentialsConfigured()
        {
            var section = EntraSection(new Dictionary<string, string>
            {
                ["ldap:domains:cloud:entra:tenantId"] = "t",
                ["ldap:domains:cloud:entra:clientId"] = "c",
                ["ldap:domains:cloud:entra:clientSecret"] = "shh",
                ["ldap:domains:cloud:entra:certificatePath"] = "/tmp/cert.p12",
            });

            var cfg = EntraConfig.FromSection(section, "cloud");
            Assert.Contains(cfg.Validate(), e => e.Contains("exactly one"));
        }

        [Fact]
        public void Validate_PassesForSecretOnlyConfig()
        {
            var section = EntraSection(new Dictionary<string, string>
            {
                ["ldap:domains:cloud:entra:tenantId"] = "t",
                ["ldap:domains:cloud:entra:clientId"] = "c",
                ["ldap:domains:cloud:entra:clientSecret"] = "shh",
            });

            Assert.Empty(EntraConfig.FromSection(section, "cloud").Validate());
        }

        [Fact]
        public void ScopeMap_ReadingRequiresReadRoles_WrittingRequiresWriteRoles()
        {
            Assert.Equal(EntraScopeMap.ReadRoles, EntraScopeMap.RequiredRoles(EntraScopeMap.Reading));
            Assert.Equal(EntraScopeMap.WriteRoles, EntraScopeMap.RequiredRoles(EntraScopeMap.Writting));
        }

        [Fact]
        public void ScopeMap_UnknownPolicy_Throws()
        {
            Assert.Throws<ArgumentException>(() => EntraScopeMap.RequiredRoles("Nope"));
        }

        [Fact]
        public void ScopeMap_ReadWriteGrantSatisfiesReadingPolicy()
        {
            // Only ReadWrite roles granted -> Reading is satisfied because
            // *.ReadWrite.All implies *.Read.All.
            var granted = new[] { "User.ReadWrite.All", "Group.ReadWrite.All", "GroupMember.ReadWrite.All" };

            Assert.True(EntraScopeMap.IsPolicySatisfied(granted, EntraScopeMap.Reading));
            Assert.True(EntraScopeMap.IsPolicySatisfied(granted, EntraScopeMap.Writting));
        }

        [Fact]
        public void ScopeMap_ReadOnlyGrant_DoesNotSatisfyWritting()
        {
            var granted = new[] { "User.Read.All", "Group.Read.All", "GroupMember.Read.All" };

            Assert.True(EntraScopeMap.IsPolicySatisfied(granted, EntraScopeMap.Reading));
            Assert.False(EntraScopeMap.IsPolicySatisfied(granted, EntraScopeMap.Writting));

            var missing = EntraScopeMap.MissingRoles(granted, EntraScopeMap.Writting);
            Assert.Contains("User.ReadWrite.All", missing);
        }

        [Fact]
        public void ScopeMap_EmptyGrant_ReportsAllMissing()
        {
            var missing = EntraScopeMap.MissingRoles(Array.Empty<string>(), EntraScopeMap.Reading);
            Assert.Equal(EntraScopeMap.ReadRoles.Count, missing.Count);
        }

        [Fact]
        public async Task TokenProvider_InvalidConfig_ThrowsWrongParameter()
        {
            // Missing tenant/client/credential — validation fails before any network call.
            var cfg = new EntraConfig { DomainKey = "cloud" };
            await Assert.ThrowsAsync<WrongParameterException>(
                () => EntraTokenProvider.Instance.AcquireTokenAsync(cfg));
        }

        [Fact]
        public async Task TokenProvider_NullConfig_Throws()
        {
            await Assert.ThrowsAsync<NullException>(
                () => EntraTokenProvider.Instance.AcquireTokenAsync(null));
        }

        [Fact]
        public void DomainRegistry_DetectsEntraKind()
        {
            adrapi.ConfigurationManager.Instance.Config = BuildConfig(new Dictionary<string, string>
            {
                ["ldap:defaultDomain"] = "default",
                ["ldap:domains:cloud:kind"] = "entraid",
                ["ldap:domains:corp:kind"] = "ldap",
                ["ldap:domains:legacy:servers:0"] = "127.0.0.1:389",
            });

            var registry = LdapDomainRegistry.Instance;

            Assert.True(registry.IsEntraDomain("cloud"));
            Assert.False(registry.IsEntraDomain("corp"));
            // A domain with no explicit kind defaults to LDAP.
            Assert.Equal(LdapDomainRegistry.KindLdap, registry.GetDomainKind("legacy"));
            // The default domain is always LDAP.
            Assert.Equal(LdapDomainRegistry.KindLdap, registry.GetDomainKind(null));
        }

        // Gated like the LDAP integration tests: set ADRAPI_RUN_ENTRA_INTEGRATION=1
        // with ENTRA_TENANT_ID/ENTRA_CLIENT_ID/ENTRA_CLIENT_SECRET to exercise a
        // real client-credentials token acquisition.
        [Fact]
        public async Task Integration_AcquiresRealGraphToken()
        {
            if (Environment.GetEnvironmentVariable("ADRAPI_RUN_ENTRA_INTEGRATION") != "1")
            {
                return;
            }

            var cfg = new EntraConfig
            {
                DomainKey = "it",
                TenantId = Environment.GetEnvironmentVariable("ENTRA_TENANT_ID"),
                ClientId = Environment.GetEnvironmentVariable("ENTRA_CLIENT_ID"),
                ClientSecret = Environment.GetEnvironmentVariable("ENTRA_CLIENT_SECRET"),
            };

            var token = await EntraTokenProvider.Instance.AcquireTokenAsync(cfg);

            Assert.False(string.IsNullOrWhiteSpace(token.AccessToken));
            Assert.True(token.ExpiresOn > DateTimeOffset.UtcNow);
        }
    }
}
