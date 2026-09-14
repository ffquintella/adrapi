using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using adrapi;
using adrapi.domain;
using adrapi.Ldap;
using Novell.Directory.Ldap;
using tests.Ldap;
using Xunit;

namespace tests
{
    /// <summary>
    /// Exercises <see cref="UserManager"/> against the in-memory directory fake
    /// (<see cref="LdapFakeScope"/>) rather than a live server: listing, paging,
    /// lookup, membership ranging, and the write paths. These cover the logic
    /// that used to be reachable only through the opt-in LDAP integration run.
    /// </summary>
    public class UserManagerDirectoryTests
    {
        private static readonly LdapConfig Config = new LdapConfig
        {
            searchBase = "DC=homologa,DC=br",
            ssl = true,
            maxResults = 999
        };

        private static FakeLdapQueryManager WithUsers(int count, LdapFakeScope scope)
        {
            for (var i = 1; i <= count; i++)
            {
                scope.Query.Add(LdapEntries.User(
                    $"CN=user{i},OU=Users,DC=homologa,DC=br",
                    account: $"user{i}",
                    name: $"User {i}",
                    mail: $"user{i}@homologa.br"));
            }

            return scope.Query;
        }

        // ---- Listing: name list ----

        [Fact]
        public async Task GetListAsync_ReturnsFirstPageAndCookie()
        {
            using var scope = new LdapFakeScope();
            WithUsers(5, scope);
            scope.Query.PageSize = 2;

            var page = await UserManager.Instance.GetListAsync("", "", "", Config);

            Assert.Equal(2, page.UserNames.Count);
            Assert.Equal(new[] { "user1", "user2" }, page.UserNames);
            Assert.Equal("2", page.Cookie);
            Assert.Equal(LdapSearchMethod.Paged, page.SearchMethod);
            Assert.Equal("User", page.SearchType);
        }

        [Fact]
        public async Task GetListAsync_ContinuesFromCookie_AndEndsWithEmptyCookie()
        {
            using var scope = new LdapFakeScope();
            WithUsers(3, scope);
            scope.Query.PageSize = 2;

            var second = await UserManager.Instance.GetListAsync("", "", "2", Config);

            Assert.Equal(new[] { "user3" }, second.UserNames);
            Assert.Equal("", second.Cookie);
        }

        [Fact]
        public async Task GetListAsync_SingleAttribute_ProjectsThatAttributeOnly()
        {
            using var scope = new LdapFakeScope();
            WithUsers(2, scope);
            scope.Query.PageSize = 10;

            var page = await UserManager.Instance.GetListAsync("mail", "", "", Config);

            Assert.Equal(new[] { "user1@homologa.br", "user2@homologa.br" }, page.UserNames);
            Assert.Empty(page.Users);
        }

        [Fact]
        public async Task GetListAsync_MultipleAttributes_ProjectUserObjects()
        {
            using var scope = new LdapFakeScope();
            WithUsers(2, scope);
            scope.Query.PageSize = 10;

            var page = await UserManager.Instance.GetListAsync("sAMAccountName,mail", "", "", Config);

            Assert.Equal(2, page.Users.Count);
            Assert.Equal("user1", page.Users[0].Account);
            Assert.Equal("user1@homologa.br", page.Users[0].Mail);
            Assert.Equal(new[] { "user1", "user2" }, page.UserNames);
        }

        [Fact]
        public async Task GetListAsync_WithFilter_NarrowsByCn()
        {
            using var scope = new LdapFakeScope();
            WithUsers(3, scope);
            scope.Query.PageSize = 10;

            var page = await UserManager.Instance.GetListAsync("", "user2", "", Config);

            Assert.Equal(new[] { "user2" }, page.UserNames);
            Assert.Contains(scope.Query.Calls, c => c.Contains("cn=user2"));
        }

        [Fact]
        public async Task GetListAsync_NoAttribute_BuildsUsersWithMembership()
        {
            using var scope = new LdapFakeScope();
            scope.Query.Add(LdapEntries.User(
                "CN=ada,OU=Users,DC=homologa,DC=br",
                account: "ada",
                memberOf: new[] { "CN=Admins,OU=Groups,DC=homologa,DC=br" }));

            var page = await UserManager.Instance.GetListAsync("", "", "", Config);

            var user = Assert.Single(page.Users);
            Assert.Equal("ada", user.Account);
            Assert.Equal("Admins", Assert.Single(user.MemberOf).Name);
        }

        // ---- Listing: range mode ----

        [Fact]
        public async Task GetListAsync_Range_SkipsToStartAndStopsAtEnd()
        {
            using var scope = new LdapFakeScope();
            WithUsers(6, scope);
            scope.Query.PageSize = 2;

            var page = await UserManager.Instance.GetListAsync(2, 4, "", "", Config);

            Assert.Equal(3, page.UserNames.Count);
            Assert.All(page.UserNames, dn => Assert.Contains("CN=user", dn));
            Assert.Contains("CN=user2,OU=Users,DC=homologa,DC=br", page.UserNames);
            Assert.Contains("CN=user4,OU=Users,DC=homologa,DC=br", page.UserNames);
        }

        [Fact]
        public async Task GetListAsync_Range_NormalizesInvertedAndZeroBounds()
        {
            using var scope = new LdapFakeScope();
            WithUsers(3, scope);
            scope.Query.PageSize = 5;

            var page = await UserManager.Instance.GetListAsync(0, -5, "", "", Config);

            Assert.Single(page.UserNames);
        }

        [Fact]
        public async Task GetListAsync_Range_WithAttributes_ProjectsUsers()
        {
            using var scope = new LdapFakeScope();
            WithUsers(3, scope);
            scope.Query.PageSize = 5;

            var page = await UserManager.Instance.GetListAsync(1, 2, "sAMAccountName,mail", "", Config);

            Assert.Equal(2, page.Users.Count);
            Assert.Equal(new[] { "user1", "user2" }, page.UserNames);
        }

        [Fact]
        public async Task GetListAsync_Range_EmptyDirectoryReturnsEmptyCookie()
        {
            using var scope = new LdapFakeScope();

            var page = await UserManager.Instance.GetListAsync(1, 10, "", "", Config);

            Assert.Empty(page.UserNames);
            Assert.Equal(string.Empty, page.Cookie);
        }

        // ---- Listing: full objects, paged, and all ----

        [Fact]
        public async Task GetUsersAsync_ReturnsEveryUserUnpaged()
        {
            using var scope = new LdapFakeScope();
            WithUsers(4, scope);
            scope.Query.PageSize = 1; // ignored: this path is not paged

            var response = await UserManager.Instance.GetUsersAsync(Config);

            Assert.Equal(4, response.Users.Count);
            Assert.Equal(LdapSearchMethod.Simple, response.SearchMethod);
            Assert.Contains(scope.Query.Calls, c => c.StartsWith("Search:User"));
        }

        [Fact]
        public async Task GetUsersPagedAsync_ReturnsOnePageOfFullObjects()
        {
            using var scope = new LdapFakeScope();
            WithUsers(5, scope);
            scope.Query.PageSize = 2;

            var page = await UserManager.Instance.GetUsersPagedAsync("", "", Config);

            Assert.Equal(2, page.Users.Count);
            Assert.Equal("2", page.Cookie);
            Assert.Equal(LdapSearchMethod.Paged, page.SearchMethod);
            Assert.Equal("user1@homologa.br", page.Users[0].Mail); // full object, not a name
        }

        [Fact]
        public async Task GetUsersPagedAsync_WithFilter_AppliesCnFilter()
        {
            using var scope = new LdapFakeScope();
            WithUsers(3, scope);

            var page = await UserManager.Instance.GetUsersPagedAsync("user3", "", Config);

            Assert.Single(page.Users);
            Assert.Equal("user3", page.Users[0].Account);
        }

        [Fact]
        public async Task GetUsersPagedAsync_LastPageHasEmptyCookie()
        {
            using var scope = new LdapFakeScope();
            WithUsers(2, scope);
            scope.Query.PageSize = 5;

            var page = await UserManager.Instance.GetUsersPagedAsync("", "", Config);

            Assert.Equal(2, page.Users.Count);
            Assert.Equal("", page.Cookie);
        }

        [Fact]
        public async Task GetListAllAsync_WalksEveryPage()
        {
            using var scope = new LdapFakeScope();
            WithUsers(5, scope);
            scope.Query.PageSize = 2;

            var all = await UserManager.Instance.GetListAllAsync("", "", Config);

            Assert.Equal(5, all.UserNames.Count);
            Assert.Equal("", all.Cookie);
            Assert.Equal(LdapSearchMethod.Simple, all.SearchMethod);
            Assert.Equal(3, scope.Query.Calls.Count(c => c.StartsWith("Paged:User")));
        }

        [Fact]
        public async Task GetListAllAsync_WithFilter_WalksOnlyMatches()
        {
            using var scope = new LdapFakeScope();
            WithUsers(4, scope);
            scope.Query.PageSize = 1;

            var all = await UserManager.Instance.GetListAllAsync("", "user2", Config);

            Assert.Equal(new[] { "user2" }, all.UserNames);
        }

        [Fact]
        public async Task GetUsers_RangeReturnsSlice()
        {
            using var scope = new LdapFakeScope();
            WithUsers(5, scope);

            var response = await UserManager.Instance.GetUsers(2, 3, Config);

            Assert.Equal(2, response.Users.Count);
            Assert.Equal("user2", response.Users[0].Account);
            Assert.Equal(LdapSearchMethod.Paged, response.SearchMethod);
        }

        // ---- Single-user lookup ----

        [Fact]
        public async Task GetUserAsync_ByDn_ReturnsFullUser()
        {
            using var scope = new LdapFakeScope();
            WithUsers(2, scope);

            var user = await UserManager.Instance.GetUserAsync("CN=user1,OU=Users,DC=homologa,DC=br", "", Config);

            Assert.NotNull(user);
            Assert.Equal("user1", user.Account);
            Assert.Equal("user1@homologa.br", user.Mail);
        }

        [Fact]
        public async Task GetUserAsync_BySamAccountName_ResolvesThroughSearch()
        {
            using var scope = new LdapFakeScope();
            WithUsers(2, scope);

            var user = await UserManager.Instance.GetUserAsync("user2", "", Config);

            Assert.Equal("CN=user2,OU=Users,DC=homologa,DC=br", user.DN);
        }

        [Fact]
        public async Task GetUserAsync_WithExplicitAttribute_UsesThatAttribute()
        {
            using var scope = new LdapFakeScope();
            WithUsers(2, scope);

            var user = await UserManager.Instance.GetUserAsync("user1@homologa.br", "mail", Config);

            Assert.Equal("user1", user.Account);
        }

        [Fact]
        public async Task GetUserAsync_Unknown_ReturnsNull()
        {
            using var scope = new LdapFakeScope();
            WithUsers(1, scope);

            Assert.Null(await UserManager.Instance.GetUserAsync("ghost", "", Config));
            Assert.Null(await UserManager.Instance.GetUserAsync("   ", "", Config));
        }

        [Fact]
        public async Task GetUserAsync_MembershipIsRangeExpanded()
        {
            using var scope = new LdapFakeScope();
            var dn = "CN=ada,OU=Users,DC=homologa,DC=br";

            var attrs = new LdapAttributeSet
            {
                new LdapAttribute("objectclass", new[] { "top", "person", "user" }),
                new LdapAttribute("objectCategory", "person"),
                new LdapAttribute("distinguishedName", dn),
                new LdapAttribute("name", "Ada"),
                new LdapAttribute("sAMAccountName", "ada"),
                new LdapAttribute("objectSid", "S-1-5-21-9"),
                new LdapAttribute("memberOf;range=0-1", new[]
                {
                    "CN=GrpA,OU=Groups,DC=homologa,DC=br",
                    "CN=GrpB,OU=Groups,DC=homologa,DC=br"
                })
            };
            scope.Query.Add(new LdapEntry(dn, attrs));

            // The follow-up ranged read returns the terminal window.
            var terminal = new LdapAttributeSet
            {
                new LdapAttribute("distinguishedName", dn),
                new LdapAttribute("memberOf;range=2-*", new[] { "CN=GrpC,OU=Groups,DC=homologa,DC=br" })
            };
            scope.Query.RangedRegisterOverride = new LdapEntry(dn, terminal);

            var user = await UserManager.Instance.GetUserAsync(dn, "", Config);

            Assert.Equal(
                new[] { "GrpA", "GrpB", "GrpC" },
                user.MemberOf.Select(g => g.Name).OrderBy(n => n).ToArray());
        }

        // ---- Attribute inspection ----

        [Fact]
        public async Task InspectUserAttributesAsync_ByDn_ReturnsAttributesAndGroups()
        {
            using var scope = new LdapFakeScope();
            var dn = "CN=ada,OU=Users,DC=homologa,DC=br";
            scope.Query.Add(LdapEntries.User(dn, account: "ada", memberOf: new[] { "CN=Admins,OU=Groups,DC=homologa,DC=br" }));

            var result = await UserManager.Instance.InspectUserAttributesAsync(dn, "sAMAccountName", Config);

            Assert.Equal(dn, result.DistinguishedName);
            Assert.True(result.Attributes.ContainsKey("sAMAccountName"));
            Assert.Equal(new[] { "Admins" }, result.MemberOfCns);
            Assert.Equal(new[] { "CN=Admins,OU=Groups,DC=homologa,DC=br" }, result.MemberOfDns);
        }

        [Fact]
        public async Task InspectUserAttributesAsync_ByAccount_ResolvesEntry()
        {
            using var scope = new LdapFakeScope();
            WithUsers(1, scope);

            var result = await UserManager.Instance.InspectUserAttributesAsync("user1", "sAMAccountName", Config);

            Assert.Equal("CN=user1,OU=Users,DC=homologa,DC=br", result.DistinguishedName);
        }

        [Fact]
        public async Task InspectUserAttributesAsync_BlankOrMissing_ReturnsNull()
        {
            using var scope = new LdapFakeScope();

            Assert.Null(await UserManager.Instance.InspectUserAttributesAsync("", "sAMAccountName", Config));
            Assert.Null(await UserManager.Instance.InspectUserAttributesAsync("ghost", "", Config));
        }

        // ---- Writes ----

        [Fact]
        public async Task CreateUserAsync_AddsEntryWithCoreAttributes()
        {
            using var scope = new LdapFakeScope();
            var user = new User
            {
                DN = "CN=new,OU=Users,DC=homologa,DC=br",
                Account = "new",
                Name = "New User",
                Mail = "new@homologa.br",
                Surname = "User",
                GivenName = "New",
                Description = "created by test"
            };

            Assert.Equal(0, await UserManager.Instance.CreateUserAsync(user, Config));

            var added = Assert.Single(scope.Query.Added);
            Assert.Equal(user.DN, added.Dn);
            Assert.Equal("new", added.GetAttributeSet().GetAttribute("sAMAccountName").StringValue);
            Assert.Equal("new@homologa.br", added.GetAttributeSet().GetAttribute("mail").StringValue);
        }

        [Fact]
        public async Task CreateUserAsync_WithPasswordOverPlainLdap_IsRejected()
        {
            using var scope = new LdapFakeScope();
            var plain = new LdapConfig { searchBase = Config.searchBase, ssl = false };
            var user = new User { DN = "CN=new,OU=Users,DC=homologa,DC=br", Account = "new", Name = "New", Password = "secret" };

            await Assert.ThrowsAsync<adrapi.domain.Exceptions.SSLRequiredException>(
                () => UserManager.Instance.CreateUserAsync(user, plain));
        }

        [Fact]
        public async Task CreateUserAsync_DirectoryFailure_ReturnsMinusOne()
        {
            using var scope = new LdapFakeScope();
            scope.Query.Throw = new LdapException("boom");

            var user = new User { DN = "CN=new,OU=Users,DC=homologa,DC=br", Account = "new", Name = "New" };

            Assert.Equal(-1, await UserManager.Instance.CreateUserAsync(user, Config));
        }

        [Fact]
        public async Task SaveUserAsync_WritesOnlyChangedAttributes()
        {
            using var scope = new LdapFakeScope();
            var dn = "CN=user1,OU=Users,DC=homologa,DC=br";
            WithUsers(1, scope);

            var user = new User
            {
                DN = dn,
                Account = "user1",
                Name = "User 1",
                Mail = "changed@homologa.br"
            };

            Assert.Equal(0, await UserManager.Instance.SaveUserAsync(user, Config));

            var (savedDn, mods) = Assert.Single(scope.Query.Saved);
            Assert.Equal(dn, savedDn);
            Assert.Contains(mods, m => m.Attribute.Name == "mail" && m.Attribute.StringValue == "changed@homologa.br");
            Assert.DoesNotContain(mods, m => m.Attribute.Name == "cn");
            Assert.DoesNotContain(mods, m => m.Attribute.Name == "userAccountControl");
        }

        [Fact]
        public async Task SaveUserAsync_AddsAttribute_WhenStoredEntryLacksIt()
        {
            using var scope = new LdapFakeScope();
            var dn = "CN=user1,OU=Users,DC=homologa,DC=br";
            // No mail on the stored entry: dattrs.TryGetValue("mail", ...) must
            // report "not found" instead of GetAttribute throwing
            // KeyNotFoundException, or the whole save fails and returns -1.
            scope.Query.Add(LdapEntries.User(dn, account: "user1", name: "User 1"));

            var user = new User
            {
                DN = dn,
                Account = "user1",
                Name = "User 1",
                Mail = "new@homologa.br"
            };

            Assert.Equal(0, await UserManager.Instance.SaveUserAsync(user, Config));

            var (savedDn, mods) = Assert.Single(scope.Query.Saved);
            Assert.Equal(dn, savedDn);
            Assert.Contains(mods, m => m.Attribute.Name == "mail" && m.Attribute.StringValue == "new@homologa.br");
        }

        [Fact]
        public async Task SaveUserAsync_UnknownUser_ReturnsMinusOne()
        {
            using var scope = new LdapFakeScope();

            var user = new User { DN = "CN=ghost,OU=Users,DC=homologa,DC=br", Account = "ghost", Name = "Ghost" };

            Assert.Equal(-1, await UserManager.Instance.SaveUserAsync(user, Config));
            Assert.Empty(scope.Query.Saved);
        }

        [Fact]
        public async Task DeleteUser_RemovesEntry()
        {
            using var scope = new LdapFakeScope();
            WithUsers(1, scope);

            var result = await UserManager.Instance.DeleteUser(
                new User { DN = "CN=user1,OU=Users,DC=homologa,DC=br" }, Config);

            Assert.Equal(0, result);
            Assert.Equal("CN=user1,OU=Users,DC=homologa,DC=br", Assert.Single(scope.Query.Deleted));
        }

        [Fact]
        public async Task DeleteUser_Failure_ReturnsMinusOne()
        {
            using var scope = new LdapFakeScope();
            scope.Query.Throw = new LdapException("boom");

            Assert.Equal(-1, await UserManager.Instance.DeleteUser(new User { DN = "CN=x,DC=homologa,DC=br" }, Config));
        }

        // ---- Account enable/disable ----

        [Fact]
        public async Task SetAccountEnabledAsync_DisableFlipsTheBit()
        {
            using var scope = new LdapFakeScope();
            WithUsers(1, scope);
            var dn = "CN=user1,OU=Users,DC=homologa,DC=br";

            Assert.Equal(0, await UserManager.Instance.SetAccountEnabledAsync(dn, false, Config));

            var (_, mods) = Assert.Single(scope.Query.Saved);
            Assert.Equal("514", Assert.Single(mods).Attribute.StringValue); // 512 | 0x2
        }

        [Fact]
        public async Task SetAccountEnabledAsync_AlreadyInStateSkipsWrite()
        {
            using var scope = new LdapFakeScope();
            WithUsers(1, scope);

            Assert.Equal(0, await UserManager.Instance.SetAccountEnabledAsync(
                "CN=user1,OU=Users,DC=homologa,DC=br", true, Config));

            Assert.Empty(scope.Query.Saved);
        }

        [Fact]
        public async Task SetAccountEnabledAsync_BlankDnOrUnreadableEntry_ReturnsMinusOne()
        {
            using var scope = new LdapFakeScope();

            Assert.Equal(-1, await UserManager.Instance.SetAccountEnabledAsync("", true, Config));
            Assert.Equal(-1, await UserManager.Instance.SetAccountEnabledAsync(
                "CN=ghost,OU=Users,DC=homologa,DC=br", true, Config));
        }

        // ---- Authentication ----

        [Fact]
        public async Task ValidateAuthenticationAsync_ResolvesAccountToDnBeforeBinding()
        {
            using var scope = new LdapFakeScope();
            WithUsers(1, scope);
            scope.Auth.ValidCredentials["CN=user1,OU=Users,DC=homologa,DC=br"] = "pass";

            Assert.True(await UserManager.Instance.ValidateAuthenticationAsync("user1", "pass", Config));
            Assert.Equal("Validate:CN=user1,OU=Users,DC=homologa,DC=br", Assert.Single(scope.Auth.Calls));
        }

        [Fact]
        public async Task ValidateAuthenticationAsync_UpnBindsVerbatim()
        {
            using var scope = new LdapFakeScope();
            scope.Auth.ValidCredentials["ada@homologa.br"] = "pass";

            Assert.True(await UserManager.Instance.ValidateAuthenticationAsync("ada@homologa.br", "pass", Config));
            Assert.Equal("Validate:ada@homologa.br", Assert.Single(scope.Auth.Calls));
        }

        [Fact]
        public async Task ValidateAuthenticationAsync_WrongPasswordOrBlankInput_IsFalse()
        {
            using var scope = new LdapFakeScope();
            scope.Auth.ValidCredentials["ada@homologa.br"] = "pass";

            Assert.False(await UserManager.Instance.ValidateAuthenticationAsync("ada@homologa.br", "nope", Config));
            Assert.False(await UserManager.Instance.ValidateAuthenticationAsync("", "pass", Config));
            Assert.False(await UserManager.Instance.ValidateAuthenticationAsync("ada@homologa.br", "", Config));
        }
    }
}
