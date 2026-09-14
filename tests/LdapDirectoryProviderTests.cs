using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using adrapi.Directory;
using adrapi.domain;
using adrapi.Ldap;
using Novell.Directory.Ldap;
using tests.Ldap;
using Xunit;

namespace tests
{
    /// <summary>
    /// <see cref="LdapDirectoryProvider"/> over the in-memory directory fake:
    /// the LDAP half of the backend-neutral provider contract (users, groups,
    /// membership deltas, OUs), including the paging added for
    /// <c>GET /api/users</c>.
    /// </summary>
    public class LdapDirectoryProviderTests
    {
        private static readonly LdapConfig Config = new LdapConfig
        {
            DomainKey = "corp",
            searchBase = "DC=homologa,DC=br",
            ssl = true,
            maxResults = 999
        };

        private static LdapDirectoryProvider Provider() => new LdapDirectoryProvider(Config);

        private static void SeedUsers(LdapFakeScope scope, int count)
        {
            for (var i = 1; i <= count; i++)
            {
                scope.Query.Add(LdapEntries.User(
                    $"CN=user{i},OU=Users,DC=homologa,DC=br",
                    account: $"user{i}",
                    mail: $"user{i}@homologa.br"));
            }
        }

        [Fact]
        public void Metadata_DescribesAnLdapBackend()
        {
            var provider = Provider();

            Assert.Equal("corp", provider.DomainKey);
            Assert.Equal(DirectoryBackend.Ldap, provider.Backend);
            Assert.True(provider.SupportsOrganizationalUnits);
        }

        // ---- Users ----

        [Fact]
        public async Task GetUsersAsync_ReturnsNormalizedUsers()
        {
            using var scope = new LdapFakeScope();
            SeedUsers(scope, 3);

            var users = await Provider().GetUsersAsync();

            Assert.Equal(3, users.Count);
            Assert.Contains(users, u => u.Account == "user1");
        }

        [Fact]
        public async Task GetUsersPageAsync_ReturnsOnePageAndCookie()
        {
            using var scope = new LdapFakeScope();
            SeedUsers(scope, 5);
            scope.Query.PageSize = 2;

            var page = await Provider().GetUsersPageAsync();

            Assert.Equal(2, page.Users.Count);
            Assert.Equal("2", page.Cookie);
        }

        [Fact]
        public async Task GetUsersPageAsync_ContinuesFromCookie_AndFilters()
        {
            using var scope = new LdapFakeScope();
            SeedUsers(scope, 4);
            scope.Query.PageSize = 2;

            var second = await Provider().GetUsersPageAsync("", "2");
            Assert.Equal(2, second.Users.Count);
            Assert.Equal("", second.Cookie);

            var filtered = await Provider().GetUsersPageAsync("user3");
            Assert.Equal("user3", Assert.Single(filtered.Users).Account);
        }

        [Fact]
        public async Task GetUserAsync_AndUserExistsAsync()
        {
            using var scope = new LdapFakeScope();
            SeedUsers(scope, 2);
            var provider = Provider();

            var user = await provider.GetUserAsync("user1");
            Assert.Equal("CN=user1,OU=Users,DC=homologa,DC=br", user.DN);

            Assert.True(await provider.UserExistsAsync("user2"));
            Assert.False(await provider.UserExistsAsync("ghost"));
        }

        [Fact]
        public async Task SearchUsersAsync_MatchesByCn()
        {
            using var scope = new LdapFakeScope();
            SeedUsers(scope, 3);

            var found = await Provider().SearchUsersAsync("user2");

            Assert.Equal("user2", Assert.Single(found).Account);
        }

        [Fact]
        public async Task CreateUpdateDeleteUser_RoundTrip()
        {
            using var scope = new LdapFakeScope();
            var provider = Provider();
            var user = new User
            {
                DN = "CN=ada,OU=Users,DC=homologa,DC=br",
                Account = "ada",
                Name = "Ada"
            };

            Assert.True(await provider.CreateUserAsync(user));
            Assert.Equal(user.DN, Assert.Single(scope.Query.Added).Dn);

            Assert.True(await provider.UpdateUserAsync(user));
            Assert.True(await provider.DeleteUserAsync(user));
            Assert.Equal(user.DN, Assert.Single(scope.Query.Deleted));
        }

        [Fact]
        public async Task SetUserEnabledAsync_ResolvesDnFirst()
        {
            using var scope = new LdapFakeScope();
            SeedUsers(scope, 1);

            Assert.True(await Provider().SetUserEnabledAsync("user1", false));
            Assert.Equal("514", Assert.Single(Assert.Single(scope.Query.Saved).mods).Attribute.StringValue);
        }

        [Fact]
        public async Task SetUserEnabledAsync_UnknownUser_IsFalse()
        {
            using var scope = new LdapFakeScope();

            Assert.False(await Provider().SetUserEnabledAsync("ghost", true));
        }

        [Fact]
        public async Task SetUserPasswordAsync_SetsPassword_WhenStoredEntryHasNoUnicodePwd()
        {
            using var scope = new LdapFakeScope();
            SeedUsers(scope, 1);

            // The stored entry never carries unicodePwd, so SaveUserAsync must
            // look up the existing attribute set with TryGetValue (not the
            // throwing GetAttribute) to add it as a new attribute instead of
            // failing the whole save.
            Assert.True(await Provider().SetUserPasswordAsync("user1", "n3wPass"));
            Assert.Single(scope.Query.Saved);
        }

        [Fact]
        public async Task SetUserPasswordAsync_UnknownUser_IsFalse()
        {
            using var scope = new LdapFakeScope();

            Assert.False(await Provider().SetUserPasswordAsync("ghost", "n3wPass"));
        }

        // ---- Groups ----

        [Fact]
        public async Task GroupReads_ListLookupAndExistence()
        {
            using var scope = new LdapFakeScope();
            scope.Query.Add(LdapEntries.Group("CN=grp1,OU=Groups,DC=homologa,DC=br", "one"));
            scope.Query.Add(LdapEntries.Group("CN=grp2,OU=Groups,DC=homologa,DC=br", "two"));
            var provider = Provider();

            Assert.Equal(2, (await provider.GetGroupsAsync()).Count);
            Assert.Equal("one", (await provider.GetGroupAsync("CN=grp1,OU=Groups,DC=homologa,DC=br")).Description);
            Assert.True(await provider.GroupExistsAsync("CN=grp2,OU=Groups,DC=homologa,DC=br"));
            Assert.False(await provider.GroupExistsAsync("CN=ghost,OU=Groups,DC=homologa,DC=br"));
        }

        [Fact]
        public async Task CreateUpdateDeleteGroup_RoundTrip()
        {
            using var scope = new LdapFakeScope();
            var provider = Provider();
            var group = new Group
            {
                DN = "CN=grp1,OU=Groups,DC=homologa,DC=br",
                Name = "grp1",
                Description = "one"
            };

            Assert.True(await provider.CreateGroupAsync(group));
            Assert.True(await provider.UpdateGroupAsync(group));
            Assert.True(await provider.DeleteGroupAsync(group));
            Assert.Equal(group.DN, Assert.Single(scope.Query.Deleted));
        }

        [Fact]
        public async Task GetGroupMembersAsync_UnknownGroup_IsEmpty()
        {
            using var scope = new LdapFakeScope();

            Assert.Empty(await Provider().GetGroupMembersAsync("CN=ghost,OU=Groups,DC=homologa,DC=br"));
        }

        [Fact]
        public async Task MembershipDeltas_ReadModifyWriteTheGroup()
        {
            using var scope = new LdapFakeScope();
            var dn = "CN=grp1,OU=Groups,DC=homologa,DC=br";
            scope.Query.Add(LdapEntries.Group(dn, "one"));
            var provider = Provider();

            Assert.True(await provider.AddGroupMembersAsync(dn, new[] { "CN=ada,OU=Users,DC=homologa,DC=br" }));
            Assert.True(await provider.RemoveGroupMembersAsync(dn, new[] { "CN=ada,OU=Users,DC=homologa,DC=br" }));
            Assert.True(await provider.ReplaceGroupMembersAsync(dn, new[] { "CN=bob,OU=Users,DC=homologa,DC=br" }));

            Assert.Equal(3, scope.Query.Saved.Count);
            Assert.All(scope.Query.Saved, s => Assert.Equal(dn, s.dn));
        }

        [Fact]
        public async Task MembershipDeltas_UnknownGroup_AreFalse()
        {
            using var scope = new LdapFakeScope();
            var ghost = "CN=ghost,OU=Groups,DC=homologa,DC=br";
            var provider = Provider();

            Assert.False(await provider.AddGroupMembersAsync(ghost, new[] { "CN=ada,DC=homologa,DC=br" }));
            Assert.False(await provider.RemoveGroupMembersAsync(ghost, new[] { "CN=ada,DC=homologa,DC=br" }));
            Assert.False(await provider.ReplaceGroupMembersAsync(ghost, new List<string>()));
            Assert.Empty(scope.Query.Saved);
        }

        // ---- OUs ----

        [Fact]
        public async Task OrganizationalUnits_ListAndLookup()
        {
            using var scope = new LdapFakeScope();
            scope.Query.Add(LdapEntries.OrganizationalUnit("OU=Infra,DC=homologa,DC=br", "Infrastructure"));
            var provider = Provider();

            var ous = await provider.GetOrganizationalUnitsAsync();
            Assert.Equal("OU=Infra,DC=homologa,DC=br", Assert.Single(ous).DN);

            var ou = await provider.GetOrganizationalUnitAsync("OU=Infra,DC=homologa,DC=br");
            Assert.Equal("Infrastructure", ou.Description);
        }

        [Fact]
        public async Task OrganizationalUnits_CreateUpdateDelete()
        {
            using var scope = new LdapFakeScope();
            var provider = Provider();
            var ou = new OU { DN = "OU=New,DC=homologa,DC=br", Name = "New", Description = "desc" };

            Assert.True(await provider.CreateOrganizationalUnitAsync(ou));
            Assert.True(await provider.UpdateOrganizationalUnitAsync(ou));
            Assert.True(await provider.DeleteOrganizationalUnitAsync(ou));
            Assert.Equal(ou.DN, Assert.Single(scope.Query.Deleted));
        }
    }

    /// <summary>
    /// The <see cref="IDirectoryProvider.GetUsersPageAsync"/> default
    /// implementation: a backend that cannot page server-side must still answer
    /// correctly, returning everything in one page with no continuation cookie.
    /// </summary>
    public class DirectoryProviderDefaultPagingTests
    {
        private sealed class UnpagedProvider : IDirectoryProvider
        {
            public readonly List<string> Calls = new();
            public List<User> Users = new();

            public string DomainKey => "stub";
            public DirectoryBackend Backend => DirectoryBackend.Ldap;
            public bool SupportsOrganizationalUnits => false;

            public Task<List<User>> GetUsersAsync(CancellationToken ct = default)
            {
                Calls.Add("GetUsers");
                return Task.FromResult(Users);
            }

            public Task<List<User>> SearchUsersAsync(string query, CancellationToken ct = default)
            {
                Calls.Add("Search:" + query);
                return Task.FromResult(Users.Where(u => u.Account == query).ToList());
            }

            public Task<User> GetUserAsync(string id, CancellationToken ct = default) => throw new System.NotSupportedException();
            public Task<bool> UserExistsAsync(string id, CancellationToken ct = default) => throw new System.NotSupportedException();
            public Task<bool> CreateUserAsync(User u, CancellationToken ct = default) => throw new System.NotSupportedException();
            public Task<bool> UpdateUserAsync(User u, CancellationToken ct = default) => throw new System.NotSupportedException();
            public Task<bool> DeleteUserAsync(User u, CancellationToken ct = default) => throw new System.NotSupportedException();
            public Task<bool> SetUserEnabledAsync(string id, bool enabled, CancellationToken ct = default) => throw new System.NotSupportedException();
            public Task<bool> SetUserPasswordAsync(string id, string pwd, bool force = true, CancellationToken ct = default) => throw new System.NotSupportedException();
            public Task<List<Group>> GetGroupsAsync(CancellationToken ct = default) => throw new System.NotSupportedException();
            public Task<Group> GetGroupAsync(string id, CancellationToken ct = default) => throw new System.NotSupportedException();
            public Task<bool> GroupExistsAsync(string id, CancellationToken ct = default) => throw new System.NotSupportedException();
            public Task<bool> CreateGroupAsync(Group g, CancellationToken ct = default) => throw new System.NotSupportedException();
            public Task<bool> UpdateGroupAsync(Group g, CancellationToken ct = default) => throw new System.NotSupportedException();
            public Task<bool> DeleteGroupAsync(Group g, CancellationToken ct = default) => throw new System.NotSupportedException();
            public Task<List<string>> GetGroupMembersAsync(string id, CancellationToken ct = default) => throw new System.NotSupportedException();
            public Task<bool> AddGroupMembersAsync(string id, IEnumerable<string> m, CancellationToken ct = default) => throw new System.NotSupportedException();
            public Task<bool> RemoveGroupMembersAsync(string id, IEnumerable<string> m, CancellationToken ct = default) => throw new System.NotSupportedException();
            public Task<bool> ReplaceGroupMembersAsync(string id, IEnumerable<string> m, CancellationToken ct = default) => throw new System.NotSupportedException();
            public Task<List<OU>> GetOrganizationalUnitsAsync(CancellationToken ct = default) => throw new System.NotSupportedException();
            public Task<OU> GetOrganizationalUnitAsync(string dn, CancellationToken ct = default) => throw new System.NotSupportedException();
            public Task<bool> CreateOrganizationalUnitAsync(OU ou, CancellationToken ct = default) => throw new System.NotSupportedException();
            public Task<bool> UpdateOrganizationalUnitAsync(OU ou, CancellationToken ct = default) => throw new System.NotSupportedException();
            public Task<bool> DeleteOrganizationalUnitAsync(OU ou, CancellationToken ct = default) => throw new System.NotSupportedException();
        }

        [Fact]
        public async Task DefaultImplementation_ReturnsEverythingInOnePage()
        {
            IDirectoryProvider provider = new UnpagedProvider
            {
                Users = { new User { Account = "ada" }, new User { Account = "bob" } }
            };

            var page = await provider.GetUsersPageAsync();

            Assert.Equal(2, page.Users.Count);
            Assert.Equal("", page.Cookie); // no continuation: the single page is everything
        }

        [Fact]
        public async Task DefaultImplementation_WithFilter_DelegatesToSearch()
        {
            var stub = new UnpagedProvider
            {
                Users = { new User { Account = "ada" }, new User { Account = "bob" } }
            };
            IDirectoryProvider provider = stub;

            var page = await provider.GetUsersPageAsync("ada");

            Assert.Equal("ada", Assert.Single(page.Users).Account);
            Assert.Contains("Search:ada", stub.Calls);
            Assert.DoesNotContain("GetUsers", stub.Calls);
        }
    }
}
