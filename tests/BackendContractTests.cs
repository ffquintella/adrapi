using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;
using adrapi.Directory;
using adrapi.Entra;
using adrapi.domain;

namespace tests
{
    /// <summary>
    /// Stage 9 contract/regression tests: the v2 response models keep a stable
    /// JSON shape, and that shape is identical regardless of which backend
    /// produced the object (LDAP vs Entra ID), per the Stage 6 normalization
    /// contract. A drift here is a deliberate API contract change.
    /// </summary>
    public class BackendContractTests
    {
        private static SortedSet<string> JsonPropertyNames(object value)
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value));
            return new SortedSet<string>(doc.RootElement.EnumerateObject().Select(p => p.Name));
        }

        // The frozen wire shapes. Adding/removing a field must update these on purpose.
        private static readonly SortedSet<string> ExpectedUserShape = new(new[]
        {
            "Name", "GivenName", "Surname", "Login", "Account", "Description",
            "Mail", "Mobile", "ID", "DN", "Password", "IsDisabled", "IsLocked",
            "PasswordExpired", "accountControl", "MemberOf",
        });

        private static readonly SortedSet<string> ExpectedGroupShape = new(new[]
        {
            "Name", "Description", "DN", "ID", "GroupType", "Member", "MemberOf",
        });

        [Fact]
        public void UserShape_IsStable()
        {
            Assert.Equal(ExpectedUserShape, JsonPropertyNames(new User()));
        }

        [Fact]
        public void GroupShape_IsStable()
        {
            Assert.Equal(ExpectedGroupShape, JsonPropertyNames(new Group()));
        }

        [Fact]
        public void UserShape_IsIdenticalAcrossBackends()
        {
            var graphUser = GraphUserMapper.ToUser(JsonDocument.Parse(
                @"{""id"":""obj-1"",""displayName"":""Ada"",""userPrincipalName"":""ada@contoso.com"",""accountEnabled"":true}").RootElement);
            graphUser = DirectoryObjectNormalizer.Normalize(graphUser, DirectoryBackend.EntraId);

            var ldapUser = DirectoryObjectNormalizer.Normalize(
                new User { Name = "Ada", Account = "ada", DN = "CN=Ada,DC=corp,DC=example", ID = "S-1-5-21-1" },
                DirectoryBackend.Ldap);

            Assert.Equal(JsonPropertyNames(ldapUser), JsonPropertyNames(graphUser));
        }

        [Fact]
        public void IdentifierContract_DnAndIdPerBackend()
        {
            var graphUser = DirectoryObjectNormalizer.Normalize(
                new User { ID = "obj-1", DN = "should-be-dropped" }, DirectoryBackend.EntraId);
            Assert.Null(graphUser.DN);          // Entra has no DN
            Assert.Equal("obj-1", graphUser.ID); // durable id retained

            var ldapUser = DirectoryObjectNormalizer.Normalize(
                new User { ID = "S-1-5-21-1", DN = "CN=Ada,DC=corp" }, DirectoryBackend.Ldap);
            Assert.Equal("CN=Ada,DC=corp", ldapUser.DN); // DN preserved
        }

        [Fact]
        public void GroupContract_AlwaysHasGroupType_AcrossBackends()
        {
            var graphGroup = DirectoryObjectNormalizer.Normalize(
                new Group { Name = "Eng", GroupType = "Microsoft365" }, DirectoryBackend.EntraId);
            var ldapGroup = DirectoryObjectNormalizer.Normalize(
                new Group { Name = "Admins" }, DirectoryBackend.Ldap);

            Assert.False(string.IsNullOrEmpty(graphGroup.GroupType));
            Assert.False(string.IsNullOrEmpty(ldapGroup.GroupType)); // defaulted to Security
            Assert.Equal(JsonPropertyNames(ldapGroup), JsonPropertyNames(graphGroup));
        }
    }
}
