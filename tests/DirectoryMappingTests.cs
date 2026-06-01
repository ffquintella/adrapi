using System.Collections.Generic;
using System.Threading.Tasks;
using adrapi.Controllers;
using adrapi.Directory;
using adrapi.Entra;
using adrapi.Ldap;
using adrapi.domain;
using adrapi.domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace tests
{
    /// <summary>
    /// Stage 6 (directory object mapping & abstraction) unit tests: identifier
    /// classification/translation at the edge, response normalization to a
    /// consistent shape, and the explicit OU non-support on Entra ID.
    /// </summary>
    public class DirectoryMappingTests
    {
        // ---- Identifier classification / translation ----

        [Theory]
        [InlineData("11111111-1111-1111-1111-111111111111", DirectoryIdentifierKind.ObjectId)]
        [InlineData("CN=Ada,OU=People,DC=corp,DC=example", DirectoryIdentifierKind.DistinguishedName)]
        [InlineData("ada@contoso.com", DirectoryIdentifierKind.UserPrincipalName)]
        [InlineData("ada", DirectoryIdentifierKind.Name)]
        public void Classify_RecognizesIdentifierKinds(string id, DirectoryIdentifierKind expected)
        {
            Assert.Equal(expected, DirectoryIdentifiers.Classify(id));
        }

        [Fact]
        public void EnsureGraphAddressable_RejectsDn_AllowsObjectIdAndUpn()
        {
            Assert.Throws<WrongParameterException>(
                () => DirectoryIdentifiers.EnsureGraphAddressable("CN=Ada,DC=corp,DC=example"));

            // No throw for Graph-addressable forms.
            DirectoryIdentifiers.EnsureGraphAddressable("ada@contoso.com");
            DirectoryIdentifiers.EnsureGraphAddressable("11111111-1111-1111-1111-111111111111");
        }

        // ---- Normalization ----

        [Fact]
        public void Normalize_User_TrimsBlanksToNull_AndDropsDnForEntra()
        {
            var entra = DirectoryObjectNormalizer.Normalize(
                new User { Name = "  Ada  ", Surname = "   ", DN = "CN=x", ID = "obj-1" },
                DirectoryBackend.EntraId);

            Assert.Equal("Ada", entra.Name);
            Assert.Null(entra.Surname);   // whitespace -> null
            Assert.Null(entra.DN);        // Entra has no DN

            var ldap = DirectoryObjectNormalizer.Normalize(
                new User { Name = "Bob", DN = "CN=Bob,DC=corp" }, DirectoryBackend.Ldap);
            Assert.Equal("CN=Bob,DC=corp", ldap.DN); // DN preserved for LDAP
        }

        [Fact]
        public void Normalize_Group_DefaultsGroupType_AndPreservesExplicit()
        {
            var defaulted = DirectoryObjectNormalizer.Normalize(new Group { Name = "Admins" }, DirectoryBackend.Ldap);
            Assert.Equal(GraphGroupMapper.KindSecurity, defaulted.GroupType);

            var m365 = DirectoryObjectNormalizer.Normalize(
                new Group { Name = "Mktg", GroupType = "Microsoft365" }, DirectoryBackend.EntraId);
            Assert.Equal("Microsoft365", m365.GroupType);
        }

        // ---- Graph provider rejects DN identifiers at the edge ----

        private sealed class ThrowingGraphClient : IGraphClient
        {
            public Task<GraphResult> GetAsync(string u, System.Threading.CancellationToken ct = default) => throw new System.Exception("should not be called");
            public Task<List<System.Text.Json.JsonElement>> GetPagedAsync(string u, System.Threading.CancellationToken ct = default) => throw new System.Exception("should not be called");
            public Task<GraphResult> PostAsync(string u, object b, System.Threading.CancellationToken ct = default) => throw new System.Exception("should not be called");
            public Task<GraphResult> PatchAsync(string u, object b, System.Threading.CancellationToken ct = default) => throw new System.Exception("should not be called");
            public Task<GraphResult> DeleteAsync(string u, System.Threading.CancellationToken ct = default) => throw new System.Exception("should not be called");
        }

        [Fact]
        public async Task GraphProvider_RejectsDnIdentifier_BeforeAnyCall()
        {
            var p = new GraphDirectoryProvider(new EntraConfig { DomainKey = "cloud" }, new ThrowingGraphClient());

            await Assert.ThrowsAsync<WrongParameterException>(() => p.GetUserAsync("CN=Ada,DC=corp,DC=example"));
            await Assert.ThrowsAsync<WrongParameterException>(() => p.GetGroupMembersAsync("CN=Grp,DC=corp,DC=example"));
        }

        // ---- OU operations rejected on an Entra ID-backed domain ----

        [Fact]
        public async Task OuController_RejectsEntraDomain_WithBadRequest()
        {
            adrapi.ConfigurationManager.Instance.Config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["ldap:defaultDomain"] = "default",
                    ["ldap:searchBase"] = "DC=homologa,DC=br",
                    ["ldap:servers:0"] = "127.0.0.1:1",
                    ["ldap:domains:cloud:kind"] = "entraid",
                    ["ldap:domains:cloud:entra:tenantId"] = "t",
                    ["ldap:domains:cloud:entra:clientId"] = "c",
                    ["ldap:domains:cloud:entra:clientSecret"] = "s",
                })
                .Build();
            LdapDomainRegistry.Instance.ClearCache();

            var controller = new OUsController(NullLogger<GroupsController>.Instance, new ConfigurationBuilder().Build());
            var context = new DefaultHttpContext();
            context.Request.Headers["api-key"] = "dev-local:secret";
            controller.ControllerContext = new ControllerContext { HttpContext = context };

            var result = await controller.Get(domain: "cloud");

            Assert.IsType<BadRequestObjectResult>(result.Result);
        }
    }
}
