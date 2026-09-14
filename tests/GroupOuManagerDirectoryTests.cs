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
    /// <see cref="GroupManager"/> and <see cref="OUManager"/> against the
    /// in-memory directory fake: listing, lookup, and the write paths that were
    /// previously only reachable with a live LDAP server.
    /// </summary>
    public class GroupOuManagerDirectoryTests
    {
        private static readonly LdapConfig Config = new LdapConfig
        {
            searchBase = "DC=homologa,DC=br",
            ssl = true,
            maxResults = 999
        };

        private static void Seed(LdapFakeScope scope, int groups = 3)
        {
            for (var i = 1; i <= groups; i++)
            {
                scope.Query.Add(LdapEntries.Group(
                    $"CN=grp{i},OU=Groups,DC=homologa,DC=br",
                    description: $"Group {i}",
                    members: new[] { $"CN=user{i},OU=Users,DC=homologa,DC=br" }));
            }
        }

        // ---- Groups: listing ----

        [Fact]
        public async Task GetCnListAsync_ReturnsCns()
        {
            using var scope = new LdapFakeScope();
            Seed(scope);

            var cns = await GroupManager.Instance.GetCnListAsync(Config);

            Assert.Equal(new[] { "grp1", "grp2", "grp3" }, cns);
        }

        [Fact]
        public async Task GetListAsync_ReturnsDns()
        {
            using var scope = new LdapFakeScope();
            Seed(scope);

            var dns = await GroupManager.Instance.GetListAsync(Config);

            Assert.All(dns, dn => Assert.StartsWith("CN=grp", dn));
            Assert.Equal(3, dns.Count);
        }

        [Fact]
        public async Task GetCnListAsync_Range_ReturnsSlice()
        {
            using var scope = new LdapFakeScope();
            Seed(scope, 5);

            var cns = await GroupManager.Instance.GetCnListAsync(2, 3, Config);

            Assert.Equal(new[] { "grp2", "grp3" }, cns);
        }

        [Fact]
        public async Task GetListAsync_Range_ReturnsSlice()
        {
            using var scope = new LdapFakeScope();
            Seed(scope, 5);

            var dns = await GroupManager.Instance.GetListAsync(1, 2, Config);

            Assert.Equal(2, dns.Count);
            Assert.Equal("CN=grp1,OU=Groups,DC=homologa,DC=br", dns[0]);
        }

        [Fact]
        public async Task GetGroupsAsync_ReturnsFullObjects()
        {
            using var scope = new LdapFakeScope();
            Seed(scope, 2);

            var groups = await GroupManager.Instance.GetGroupsAsync(Config);

            Assert.Equal(2, groups.Count);
            Assert.Equal("Group 1", groups[0].Description);
            Assert.Equal("CN=grp1,OU=Groups,DC=homologa,DC=br", groups[0].DN);
        }

        // ---- Groups: lookup ----

        [Fact]
        public async Task GetGroupAsync_ByDn_ReturnsGroup()
        {
            using var scope = new LdapFakeScope();
            Seed(scope, 1);

            var group = await GroupManager.Instance.GetGroupAsync("CN=grp1,OU=Groups,DC=homologa,DC=br", config: Config);

            Assert.Equal("grp1", group.Name);
            Assert.Equal("Group 1", group.Description);
        }

        [Fact]
        public async Task GetGroupAsync_ListCn_ReadsTheSameGroup()
        {
            using var scope = new LdapFakeScope();
            Seed(scope, 1);

            var group = await GroupManager.Instance.GetGroupAsync(
                "CN=grp1,OU=Groups,DC=homologa,DC=br", _listCN: true, config: Config);

            Assert.Equal("grp1", group.Name);
            Assert.Equal("CN=grp1,OU=Groups,DC=homologa,DC=br", group.DN);
            // _listCN trims each member DN down to its CN.
            Assert.Equal(new[] { "user1" }, group.Member);
        }

        [Fact]
        public async Task GetGroupAsync_MapsMembersAndMemberOfByAttributeName()
        {
            using var scope = new LdapFakeScope();
            scope.Query.Add(LdapEntries.Group(
                "CN=grp1,OU=Groups,DC=homologa,DC=br",
                members: new[]
                {
                    "CN=user1,OU=Users,DC=homologa,DC=br",
                    "CN=user2,OU=Users,DC=homologa,DC=br"
                },
                memberOf: new[] { "CN=parent,OU=Groups,DC=homologa,DC=br" }));

            var group = await GroupManager.Instance.GetGroupAsync(
                "CN=grp1,OU=Groups,DC=homologa,DC=br", config: Config);

            Assert.Equal(
                new[]
                {
                    "CN=user1,OU=Users,DC=homologa,DC=br",
                    "CN=user2,OU=Users,DC=homologa,DC=br"
                },
                group.Member);
            Assert.Equal(new[] { "CN=parent,OU=Groups,DC=homologa,DC=br" }, group.MemberOf);
        }

        [Fact]
        public async Task GetGroupAsync_ReadsRangedMemberWindow()
        {
            // Active Directory returns "member;range=0-1499" instead of "member"
            // once a group exceeds the MaxValRange limit.
            using var scope = new LdapFakeScope();
            scope.Query.Add(LdapEntries.Group(
                "CN=big,OU=Groups,DC=homologa,DC=br",
                members: new[]
                {
                    "CN=user1,OU=Users,DC=homologa,DC=br",
                    "CN=user2,OU=Users,DC=homologa,DC=br"
                },
                memberAttributeName: "member;range=0-1"));

            var group = await GroupManager.Instance.GetGroupAsync(
                "CN=big,OU=Groups,DC=homologa,DC=br", config: Config);

            Assert.Equal(
                new[]
                {
                    "CN=user1,OU=Users,DC=homologa,DC=br",
                    "CN=user2,OU=Users,DC=homologa,DC=br"
                },
                group.Member);
        }

        [Fact]
        public async Task GetGroupAsync_WithoutMembers_ReturnsEmptyLists()
        {
            using var scope = new LdapFakeScope();
            scope.Query.Add(LdapEntries.Group("CN=empty,OU=Groups,DC=homologa,DC=br"));

            var group = await GroupManager.Instance.GetGroupAsync(
                "CN=empty,OU=Groups,DC=homologa,DC=br", config: Config);

            Assert.Empty(group.Member);
            Assert.Empty(group.MemberOf);
        }

        [Fact]
        public async Task GetGroupAsync_ByCn_SearchesInsteadOfReadingTheDn()
        {
            using var scope = new LdapFakeScope();
            Seed(scope, 2);

            var group = await GroupManager.Instance.GetGroupAsync("grp2", _searchByCN: true, config: Config);

            Assert.Equal("CN=grp2,OU=Groups,DC=homologa,DC=br", group.DN);
            Assert.Contains(scope.Query.Calls, c => c.StartsWith("SearchRaw:"));
        }

        [Fact]
        public async Task GetGroupAsync_Unknown_ReturnsNull()
        {
            using var scope = new LdapFakeScope();
            Seed(scope, 1);

            Assert.Null(await GroupManager.Instance.GetGroupAsync("CN=ghost,DC=homologa,DC=br", config: Config));
            Assert.Null(await GroupManager.Instance.GetGroupAsync("ghost", _searchByCN: true, config: Config));
        }

        // ---- Groups: writes ----

        [Fact]
        public async Task CreateGroupAsync_AddsEntry()
        {
            using var scope = new LdapFakeScope();
            var group = new Group
            {
                DN = "CN=newgrp,OU=Groups,DC=homologa,DC=br",
                Name = "newgrp",
                Description = "brand new"
            };
            group.Member.Add("CN=ada,OU=Users,DC=homologa,DC=br");

            Assert.Equal(0, await GroupManager.Instance.CreateGroupAsync(group, Config));

            var added = Assert.Single(scope.Query.Added);
            Assert.Equal(group.DN, added.Dn);
            Assert.Equal("newgrp", added.GetAttributeSet().GetAttribute("cn").StringValue);
            Assert.Equal("CN=ada,OU=Users,DC=homologa,DC=br", added.GetAttributeSet().GetAttribute("member").StringValue);
        }

        [Fact]
        public async Task CreateGroupAsync_Failure_ReturnsMinusOne()
        {
            using var scope = new LdapFakeScope();
            scope.Query.Throw = new LdapException("boom");

            var group = new Group { DN = "CN=x,DC=homologa,DC=br", Name = "x", Description = "x" };

            Assert.Equal(-1, await GroupManager.Instance.CreateGroupAsync(group, Config));
        }

        [Fact]
        public async Task SaveGroupAsync_ReplacesChangedAttributes_AndRewritesMembers()
        {
            using var scope = new LdapFakeScope();
            Seed(scope, 1);

            var group = new Group
            {
                DN = "CN=grp1,OU=Groups,DC=homologa,DC=br",
                Name = "grp1",
                Description = "changed"
            };
            group.Member.Add("CN=ada,OU=Users,DC=homologa,DC=br");

            Assert.Equal(0, await GroupManager.Instance.SaveGroupAsync(group, Config));

            var (dn, mods) = Assert.Single(scope.Query.Saved);
            Assert.Equal(group.DN, dn);
            Assert.Contains(mods, m => m.Attribute.Name == "description" && m.Attribute.StringValue == "changed");
            Assert.Contains(mods, m => m.Op == LdapModification.Delete && m.Attribute.Name == "member");
            Assert.Contains(mods, m => m.Op == LdapModification.Add && m.Attribute.Name == "member");
        }

        [Fact]
        public async Task SaveGroupAsync_UnknownGroup_ReturnsMinusOne()
        {
            using var scope = new LdapFakeScope();

            var group = new Group { DN = "CN=ghost,OU=Groups,DC=homologa,DC=br", Name = "ghost", Description = "ghost" };

            Assert.Equal(-1, await GroupManager.Instance.SaveGroupAsync(group, Config));
        }

        [Fact]
        public async Task DeleteGroup_RemovesEntry()
        {
            using var scope = new LdapFakeScope();
            Seed(scope, 1);

            var result = await GroupManager.Instance.DeleteGroup(
                new Group { DN = "CN=grp1,OU=Groups,DC=homologa,DC=br" }, Config);

            Assert.Equal(0, result);
            Assert.Equal("CN=grp1,OU=Groups,DC=homologa,DC=br", Assert.Single(scope.Query.Deleted));
        }

        [Fact]
        public async Task DeleteGroup_Failure_ReturnsMinusOne()
        {
            using var scope = new LdapFakeScope();
            scope.Query.Throw = new LdapException("boom");

            Assert.Equal(-1, await GroupManager.Instance.DeleteGroup(new Group { DN = "CN=x,DC=homologa,DC=br" }, Config));
        }

        // ---- OUs ----

        [Fact]
        public async Task OuGetListAsync_ReturnsDns()
        {
            using var scope = new LdapFakeScope();
            scope.Query.Add(LdapEntries.OrganizationalUnit("OU=Infra,DC=homologa,DC=br", "Infrastructure"));
            scope.Query.Add(LdapEntries.OrganizationalUnit("OU=People,DC=homologa,DC=br"));

            var dns = await OUManager.Instance.GetListAsync(Config);

            Assert.Equal(new[] { "OU=Infra,DC=homologa,DC=br", "OU=People,DC=homologa,DC=br" }, dns);
        }

        [Fact]
        public async Task GetOUAsync_ReturnsOu_AndNullWhenMissing()
        {
            using var scope = new LdapFakeScope();
            scope.Query.Add(LdapEntries.OrganizationalUnit("OU=Infra,DC=homologa,DC=br", "Infrastructure"));

            var ou = await OUManager.Instance.GetOUAsync("OU=Infra,DC=homologa,DC=br", Config);

            Assert.Equal("Infra", ou.Name);
            Assert.Equal("Infrastructure", ou.Description);
            Assert.Null(await OUManager.Instance.GetOUAsync("OU=Ghost,DC=homologa,DC=br", Config));
        }

        [Fact]
        public async Task CreateOUAsync_AddsEntry_AndReportsFailure()
        {
            using var scope = new LdapFakeScope();
            var ou = new OU { DN = "OU=New,DC=homologa,DC=br", Name = "New", Description = "desc" };

            Assert.Equal(0, await OUManager.Instance.CreateOUAsync(ou, Config));
            Assert.Equal("OU=New,DC=homologa,DC=br", Assert.Single(scope.Query.Added).Dn);

            scope.Query.Throw = new LdapException("boom");
            Assert.Equal(-1, await OUManager.Instance.CreateOUAsync(ou, Config));
        }

        [Fact]
        public async Task SaveOUAsync_ReplacesChangedDescription()
        {
            using var scope = new LdapFakeScope();
            scope.Query.Add(LdapEntries.OrganizationalUnit("OU=Infra,DC=homologa,DC=br", "Infrastructure"));

            var ou = new OU { DN = "OU=Infra,DC=homologa,DC=br", Name = "Infra", Description = "changed" };

            Assert.Equal(0, await OUManager.Instance.SaveOUAsync(ou, Config));

            var (_, mods) = Assert.Single(scope.Query.Saved);
            Assert.Contains(mods, m => m.Attribute.Name == "description" && m.Attribute.StringValue == "changed");
        }

        [Fact]
        public async Task SaveOUAsync_UnknownOu_ReturnsMinusOne()
        {
            using var scope = new LdapFakeScope();

            var ou = new OU { DN = "OU=Ghost,DC=homologa,DC=br", Name = "Ghost", Description = "ghost" };

            Assert.Equal(-1, await OUManager.Instance.SaveOUAsync(ou, Config));
        }

        [Fact]
        public async Task DeleteOUAsync_RemovesEntry_AndReportsFailure()
        {
            using var scope = new LdapFakeScope();
            scope.Query.Add(LdapEntries.OrganizationalUnit("OU=Infra,DC=homologa,DC=br"));

            Assert.Equal(0, await OUManager.Instance.DeleteOUAsync(new OU { DN = "OU=Infra,DC=homologa,DC=br" }, Config));
            Assert.Equal("OU=Infra,DC=homologa,DC=br", Assert.Single(scope.Query.Deleted));

            scope.Query.Throw = new LdapException("boom");
            Assert.Equal(-1, await OUManager.Instance.DeleteOUAsync(new OU { DN = "OU=Infra,DC=homologa,DC=br" }, Config));
        }
    }
}
