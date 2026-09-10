using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Xunit;
using adrapi.Directory;
using adrapi.Entra;
using adrapi.Ldap;

namespace tests
{
    /// <summary>
    /// Covers the backend-neutral `directories` configuration schema introduced in
    /// 1.10.0 and its backward compatibility with the deprecated `ldap:domains` /
    /// `ldap:defaultDomain` layout: binding, kind-based dispatch, defaultDomain
    /// resolution across mixed backends, and secret lookup under both key paths.
    /// </summary>
    public class DirectorySchemaTests
    {
        private static void UseConfig(Dictionary<string, string> values)
        {
            adrapi.ConfigurationManager.Instance.Config = new ConfigurationBuilder()
                .AddInMemoryCollection(values)
                .Build();
            LdapDomainRegistry.Instance.ClearCache();
            DirectorySchema.ResetWarnings();
        }

        /// <summary>New schema: two LDAP domains plus one Entra domain, default is LDAP.</summary>
        private static Dictionary<string, string> NewSchema() => new()
        {
            ["directories:defaultDomain"] = "corp",
            ["directories:domains:corp:kind"] = "ldap",
            ["directories:domains:corp:ldap:servers:0"] = "dc1.corp:636",
            ["directories:domains:corp:ldap:ssl"] = "true",
            ["directories:domains:corp:ldap:poolSize"] = "7",
            ["directories:domains:corp:ldap:searchBase"] = "DC=corp",
            ["directories:domains:corp:ldap:bindDn"] = "cn=svc,dc=corp",
            ["directories:domains:corp:ldap:bindCredentials"] = "corp-secret",
            ["directories:domains:corp:ldap:maxResults"] = "42",
            ["directories:domains:lab:kind"] = "ldap",
            ["directories:domains:lab:ldap:servers:0"] = "dc1.lab:389",
            ["directories:domains:cloud:kind"] = "entraid",
            ["directories:domains:cloud:entra:tenantId"] = "tenant-1",
            ["directories:domains:cloud:entra:clientId"] = "client-1",
            ["directories:domains:cloud:entra:clientSecret"] = "cloud-secret",
        };

        /// <summary>Deprecated schema as shipped up to 1.9.0.</summary>
        private static Dictionary<string, string> LegacySchema() => new()
        {
            ["ldap:defaultDomain"] = "default",
            ["ldap:servers:0"] = "dc1.corp:636",
            ["ldap:ssl"] = "true",
            ["ldap:poolSize"] = "7",
            ["ldap:searchBase"] = "DC=corp",
            ["ldap:bindDn"] = "cn=svc,dc=corp",
            ["ldap:bindCredentials"] = "corp-secret",
            ["ldap:maxResults"] = "42",
            ["ldap:domains:lab:servers:0"] = "dc1.lab:389",
            ["ldap:domains:cloud:kind"] = "entraid",
            ["ldap:domains:cloud:entra:tenantId"] = "tenant-1",
            ["ldap:domains:cloud:entra:clientId"] = "client-1",
            ["ldap:domains:cloud:entra:clientSecret"] = "cloud-secret",
        };

        // ===== new schema binding ==============================================

        [Fact]
        public void NewSchema_BindsLdapDomain()
        {
            UseConfig(NewSchema());

            var cfg = LdapDomainRegistry.Instance.GetConfig("corp");

            Assert.Equal(new[] { "dc1.corp:636" }, cfg.servers);
            Assert.True(cfg.ssl);
            Assert.Equal((short)7, cfg.poolSize);
            Assert.Equal("DC=corp", cfg.searchBase);
            Assert.Equal("cn=svc,dc=corp", cfg.bindDn);
            Assert.Equal("corp-secret", cfg.bindCredentials);
            Assert.Equal(42, cfg.maxResults);
            Assert.Equal("corp", cfg.DomainKey);
        }

        [Fact]
        public void NewSchema_BindsEntraDomain()
        {
            UseConfig(NewSchema());

            var entra = EntraConfig.ForDomain("cloud");

            Assert.Equal("tenant-1", entra.TenantId);
            Assert.Equal("client-1", entra.ClientId);
            Assert.Equal("cloud-secret", entra.ClientSecret);
            Assert.Empty(entra.Validate());
        }

        [Fact]
        public void NewSchema_EnumeratesEveryDomain()
        {
            UseConfig(NewSchema());

            Assert.Equal(
                new[] { "cloud", "corp", "lab" },
                LdapDomainRegistry.Instance.EnumerateDomains().OrderBy(n => n).ToArray());
        }

        [Fact]
        public void NewSchema_AcceptsFlatLdapBlock()
        {
            // A legacy block moved across verbatim (settings straight on the domain,
            // no `ldap` sub-object) still binds.
            UseConfig(new Dictionary<string, string>
            {
                ["directories:defaultDomain"] = "corp",
                ["directories:domains:corp:servers:0"] = "dc1.corp:636",
                ["directories:domains:corp:searchBase"] = "DC=corp",
            });

            Assert.Equal("DC=corp", LdapDomainRegistry.Instance.GetConfig("corp").searchBase);
        }

        // ===== legacy schema still binds =======================================

        [Fact]
        public void LegacySchema_StillBindsDefaultAndNamedDomains()
        {
            UseConfig(LegacySchema());

            var def = LdapDomainRegistry.Instance.GetConfig(null);
            Assert.Equal(new[] { "dc1.corp:636" }, def.servers);
            Assert.Equal("corp-secret", def.bindCredentials);
            Assert.Equal("default", def.DomainKey);

            var lab = LdapDomainRegistry.Instance.GetConfig("lab");
            Assert.Equal(new[] { "dc1.lab:389" }, lab.servers);
        }

        [Fact]
        public void LegacySchema_StillBindsEntraDomain()
        {
            UseConfig(LegacySchema());

            var entra = EntraConfig.ForDomain("cloud");
            Assert.Equal("tenant-1", entra.TenantId);
            Assert.Equal("cloud-secret", entra.ClientSecret);
            Assert.Empty(entra.Validate());
        }

        [Fact]
        public void LegacyDefaultDomain_KeepsPointingAtTheTopLevelLdapSection()
        {
            // Pre-1.10.0 behaviour: the default domain IS the top-level `ldap`
            // section even when a same-named child exists under `ldap:domains`.
            UseConfig(new Dictionary<string, string>
            {
                ["ldap:defaultDomain"] = "corp",
                ["ldap:searchBase"] = "DC=top-level",
                ["ldap:servers:0"] = "dc1.corp:636",
                ["ldap:domains:corp:searchBase"] = "DC=child",
                ["ldap:domains:corp:servers:0"] = "dc2.corp:636",
            });

            Assert.Equal("DC=top-level", LdapDomainRegistry.Instance.GetConfig("corp").searchBase);
        }

        [Fact]
        public void NewAndLegacyDomains_Coexist()
        {
            var values = NewSchema();
            values["ldap:domains:onprem:servers:0"] = "dc1.onprem:389";

            UseConfig(values);

            Assert.True(LdapDomainRegistry.Instance.IsKnownDomain("onprem"));
            Assert.Equal(new[] { "dc1.onprem:389" }, LdapDomainRegistry.Instance.GetConfig("onprem").servers);
            Assert.Contains("onprem", LdapDomainRegistry.Instance.EnumerateDomains());
        }

        [Fact]
        public void NewLayout_WinsOverLegacyForTheSameDomainName()
        {
            var values = NewSchema();
            values["ldap:domains:lab:servers:0"] = "stale.lab:389";

            UseConfig(values);

            Assert.Equal(new[] { "dc1.lab:389" }, LdapDomainRegistry.Instance.GetConfig("lab").servers);
        }

        // ===== kind-based dispatch =============================================

        [Theory]
        [InlineData("corp", DirectorySchema.KindLdap)]
        [InlineData("lab", DirectorySchema.KindLdap)]
        [InlineData("cloud", DirectorySchema.KindEntraId)]
        public void NewSchema_DispatchesOnKind(string domain, string expectedKind)
        {
            UseConfig(NewSchema());

            Assert.Equal(expectedKind, LdapDomainRegistry.Instance.GetDomainKind(domain));
            Assert.Equal(
                expectedKind == DirectorySchema.KindEntraId,
                LdapDomainRegistry.Instance.IsEntraDomain(domain));
        }

        [Fact]
        public void NewSchema_FactorySelectsBackendPerKind()
        {
            UseConfig(NewSchema());

            Assert.Equal(DirectoryBackend.EntraId, DirectoryProviderFactory.ForDomain("cloud").Backend);
            Assert.Equal(DirectoryBackend.Ldap, DirectoryProviderFactory.ForDomain("corp").Backend);
        }

        [Fact]
        public void UnknownKind_IsNotSilentlyTreatedAsEntra()
        {
            UseConfig(new Dictionary<string, string>
            {
                ["directories:defaultDomain"] = "corp",
                ["directories:domains:corp:kind"] = "ldap",
                ["directories:domains:corp:ldap:servers:0"] = "dc1.corp:636",
                ["directories:domains:okta:kind"] = "okta",
            });

            Assert.Equal("okta", LdapDomainRegistry.Instance.GetDomainKind("okta"));
            Assert.False(LdapDomainRegistry.Instance.IsEntraDomain("okta"));
        }

        // ===== defaultDomain resolution ========================================

        [Fact]
        public void DefaultDomain_ResolvesFromTheNewKey()
        {
            UseConfig(NewSchema());

            Assert.Equal("corp", LdapDomainRegistry.DefaultDomainName);
            Assert.Equal("corp", LdapDomainRegistry.NormalizeKey(null));
            Assert.Equal("DC=corp", LdapDomainRegistry.Instance.GetConfig(null).searchBase);
        }

        [Fact]
        public void DefaultDomain_FallsBackToTheDeprecatedKey()
        {
            var values = LegacySchema();
            values["ldap:defaultDomain"] = "onprem";
            values["ldap:domains:onprem:servers:0"] = "dc1.onprem:389";

            UseConfig(values);

            Assert.Equal("onprem", LdapDomainRegistry.DefaultDomainName);
        }

        [Fact]
        public void DefaultDomain_MayBeEntraBacked()
        {
            // Mixed deployment whose domain-less routes are served by Entra ID —
            // impossible under the old layout, where the default was always LDAP.
            var values = NewSchema();
            values["directories:defaultDomain"] = "cloud";

            UseConfig(values);

            Assert.Equal("cloud", LdapDomainRegistry.DefaultDomainName);
            Assert.True(LdapDomainRegistry.Instance.IsEntraDomain(null));
            Assert.Equal(DirectoryBackend.EntraId, DirectoryProviderFactory.ForDomain(null).Backend);
            Assert.Equal(DirectoryBackend.Ldap, DirectoryProviderFactory.ForDomain("corp").Backend);
        }

        [Fact]
        public void DefaultDomainWithoutOwnEntry_FallsBackToTheTopLevelLdapSection()
        {
            // Half-migrated config: domains moved to `directories`, the default
            // directory left in the deprecated top-level section.
            UseConfig(new Dictionary<string, string>
            {
                ["ldap:servers:0"] = "dc1.corp:636",
                ["ldap:searchBase"] = "DC=corp",
                ["directories:domains:cloud:kind"] = "entraid",
                ["directories:domains:cloud:entra:tenantId"] = "t",
                ["directories:domains:cloud:entra:clientId"] = "c",
                ["directories:domains:cloud:entra:clientSecret"] = "s",
            });

            Assert.Equal("default", LdapDomainRegistry.DefaultDomainName);
            Assert.Equal("DC=corp", LdapDomainRegistry.Instance.GetConfig(null).searchBase);
            Assert.True(LdapDomainRegistry.Instance.IsEntraDomain("cloud"));
        }

        [Fact]
        public void UnknownDomain_IsRejected()
        {
            UseConfig(NewSchema());

            Assert.False(LdapDomainRegistry.Instance.IsKnownDomain("nope"));
            Assert.Throws<adrapi.domain.Exceptions.WrongParameterException>(
                () => LdapDomainRegistry.Instance.GetConfig("nope"));
        }

        // ===== secret lookup under both key paths ==============================

        [Fact]
        public void EntraSecret_IsReadFromTheNewKeyPath()
        {
            var values = NewSchema();
            // Simulate the encrypted store overlaying the new verbatim key path.
            values.Remove("directories:domains:cloud:entra:clientSecret");
            values["directories:domains:cloud:entra:clientSecret"] = "from-new-store";

            UseConfig(values);

            Assert.Equal("from-new-store", EntraConfig.ForDomain("cloud").ClientSecret);
        }

        [Fact]
        public void EntraSecret_FallsBackToTheLegacyKeyPath()
        {
            // Config already migrated to `directories`, secret still stored under
            // the deprecated `ldap:domains:...` name in the encrypted store.
            var values = NewSchema();
            values.Remove("directories:domains:cloud:entra:clientSecret");
            values["ldap:domains:cloud:entra:clientSecret"] = "from-legacy-store";

            UseConfig(values);

            var entra = EntraConfig.ForDomain("cloud");
            Assert.Equal("from-legacy-store", entra.ClientSecret);
            Assert.Empty(entra.Validate());
        }

        [Fact]
        public void EntraSecret_PrefersTheNewKeyPathOverTheLegacyOne()
        {
            var values = NewSchema();
            values["ldap:domains:cloud:entra:clientSecret"] = "stale";

            UseConfig(values);

            Assert.Equal("cloud-secret", EntraConfig.ForDomain("cloud").ClientSecret);
        }

        [Fact]
        public void EntraCertificatePassword_FallsBackToTheLegacyKeyPath()
        {
            var values = NewSchema();
            values.Remove("directories:domains:cloud:entra:clientSecret");
            values["directories:domains:cloud:entra:certificatePath"] = "/tmp/cloud.p12";
            values["ldap:domains:cloud:entra:certificatePassword"] = "legacy-pfx-password";

            UseConfig(values);

            var entra = EntraConfig.ForDomain("cloud");
            Assert.Equal("legacy-pfx-password", entra.CertificatePassword);
            Assert.True(entra.HasCertificate);
            Assert.False(entra.HasClientSecret);
            Assert.Empty(entra.Validate());
        }

        [Fact]
        public void EntraLegacySecretFallback_DoesNotCreateAmbiguousCredentials()
        {
            // A certificate declared in the new layout must not be paired with a
            // stale legacy client secret — Validate() rejects having both.
            var values = NewSchema();
            values.Remove("directories:domains:cloud:entra:clientSecret");
            values["directories:domains:cloud:entra:certificatePath"] = "/tmp/cloud.p12";
            values["ldap:domains:cloud:entra:clientSecret"] = "stale";

            UseConfig(values);

            var entra = EntraConfig.ForDomain("cloud");
            Assert.False(entra.HasClientSecret);
            Assert.Empty(entra.Validate());
        }

        [Fact]
        public void LdapBindCredentials_FallBackToTheLegacyKeyPath()
        {
            // Per-domain LDAP secret still stored under `ldap:domains:lab:...`.
            var values = NewSchema();
            values["ldap:domains:lab:bindCredentials"] = "legacy-bind";
            values["ldap:domains:lab:bindDn"] = "cn=svc,dc=lab";

            UseConfig(values);

            var lab = LdapDomainRegistry.Instance.GetConfig("lab");
            Assert.Equal("legacy-bind", lab.bindCredentials);
            Assert.Equal("cn=svc,dc=lab", lab.bindDn);
            // The new layout still wins for everything it does declare.
            Assert.Equal(new[] { "dc1.lab:389" }, lab.servers);
        }

        [Fact]
        public void LdapBindCredentials_PreferTheNewKeyPath()
        {
            var values = NewSchema();
            values["ldap:domains:corp:bindCredentials"] = "stale";

            UseConfig(values);

            Assert.Equal("corp-secret", LdapDomainRegistry.Instance.GetConfig("corp").bindCredentials);
        }

        [Fact]
        public void DefaultDomainLdapSecret_FallsBackToTheTopLevelLegacyKey()
        {
            // `ldap:bindCredentials` — the key `secret import-ldap` has always
            // written — keeps working once the default domain moves.
            UseConfig(new Dictionary<string, string>
            {
                ["directories:defaultDomain"] = "corp",
                ["directories:domains:corp:kind"] = "ldap",
                ["directories:domains:corp:ldap:servers:0"] = "dc1.corp:636",
                ["ldap:bindCredentials"] = "legacy-top-level",
                ["ldap:bindDn"] = "cn=svc,dc=corp",
            });

            var cfg = LdapDomainRegistry.Instance.GetConfig(null);
            Assert.Equal("legacy-top-level", cfg.bindCredentials);
            Assert.Equal("cn=svc,dc=corp", cfg.bindDn);
        }
    }
}
