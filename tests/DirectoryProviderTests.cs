using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Xunit;
using adrapi.Directory;
using adrapi.Entra;
using adrapi.domain;

namespace tests
{
    /// <summary>
    /// Stage 3 (provider abstraction + backend switch) unit tests: the factory
    /// picks the right backend per domain kind, the Graph provider advertises its
    /// capabilities and defers/refuses operations appropriately. No network.
    /// </summary>
    public class DirectoryProviderTests
    {
        private sealed class StubGraphClient : IGraphClient
        {
            public Task<GraphResult> GetAsync(string relativeUrl, CancellationToken ct = default) => throw new NotImplementedException();
            public Task<List<System.Text.Json.JsonElement>> GetPagedAsync(string relativeUrl, CancellationToken ct = default) => throw new NotImplementedException();
            public Task<GraphResult> PostAsync(string relativeUrl, object body, CancellationToken ct = default) => throw new NotImplementedException();
            public Task<GraphResult> PatchAsync(string relativeUrl, object body, CancellationToken ct = default) => throw new NotImplementedException();
            public Task<GraphResult> DeleteAsync(string relativeUrl, CancellationToken ct = default) => throw new NotImplementedException();
        }

        private static GraphDirectoryProvider GraphProvider()
            => new(new EntraConfig { DomainKey = "cloud" }, new StubGraphClient());

        [Fact]
        public void Factory_SelectsGraphForEntraDomain_AndLdapForLdapDomain()
        {
            adrapi.ConfigurationManager.Instance.Config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["ldap:defaultDomain"] = "default",
                    // Match the default-domain values the other suites use, so priming the
                    // shared (singleton) default-domain config cache here is order-independent.
                    ["ldap:searchBase"] = "DC=homologa,DC=br",
                    ["ldap:servers:0"] = "127.0.0.1:1",
                    ["ldap:domains:cloud:kind"] = "entraid",
                    ["ldap:domains:cloud:entra:tenantId"] = "t",
                    ["ldap:domains:cloud:entra:clientId"] = "c",
                    ["ldap:domains:cloud:entra:clientSecret"] = "s",
                    ["ldap:domains:corp:kind"] = "ldap",
                    ["ldap:domains:corp:servers:0"] = "127.0.0.1:389",
                })
                .Build();

            var cloud = DirectoryProviderFactory.ForDomain("cloud");
            Assert.Equal(DirectoryBackend.EntraId, cloud.Backend);
            Assert.IsType<GraphDirectoryProvider>(cloud);

            var corp = DirectoryProviderFactory.ForDomain("corp");
            Assert.Equal(DirectoryBackend.Ldap, corp.Backend);
            Assert.IsType<LdapDirectoryProvider>(corp);

            // The default (domain-less) route is LDAP-backed per deployment config.
            var def = DirectoryProviderFactory.ForDomain(null);
            Assert.Equal(DirectoryBackend.Ldap, def.Backend);
        }

        [Fact]
        public void GraphProvider_DoesNotSupportOus()
        {
            Assert.False(GraphProvider().SupportsOrganizationalUnits);
        }

        [Fact]
        public async Task GraphProvider_GroupOps_ThrowNotSupportedUntilStage5()
        {
            var p = GraphProvider();
            await Assert.ThrowsAsync<NotSupportedException>(() => p.GetGroupsAsync());
            await Assert.ThrowsAsync<NotSupportedException>(() => p.CreateGroupAsync(new Group()));
        }

        [Fact]
        public async Task GraphProvider_OuOps_AlwaysThrowNotSupported()
        {
            var p = GraphProvider();
            await Assert.ThrowsAsync<NotSupportedException>(() => p.GetOrganizationalUnitsAsync());
            await Assert.ThrowsAsync<NotSupportedException>(() => p.CreateOrganizationalUnitAsync(new OU()));
        }

        [Fact]
        public void GraphProvider_NullArgs_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new GraphDirectoryProvider(null, new StubGraphClient()));
            Assert.Throws<ArgumentNullException>(() => new GraphDirectoryProvider(new EntraConfig(), null));
        }
    }
}
