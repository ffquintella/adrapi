using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using adrapi.Directory;
using adrapi.Entra;
using adrapi.domain;

namespace tests
{
    /// <summary>
    /// Stage 9 integration tests against a real Entra ID tenant, gated like the
    /// LDAP integration tests: they no-op unless <c>ADRAPI_RUN_ENTRA_INTEGRATION=1</c>
    /// and the tenant/app credentials are provided via environment variables:
    /// <c>ENTRA_TENANT_ID</c>, <c>ENTRA_CLIENT_ID</c>, <c>ENTRA_CLIENT_SECRET</c>,
    /// and <c>ENTRA_TEST_UPN_DOMAIN</c> (a verified domain for test UPNs, e.g.
    /// <c>contoso.onmicrosoft.com</c>). The app registration needs
    /// <c>User.ReadWrite.All</c> + <c>Group.ReadWrite.All</c> +
    /// <c>GroupMember.ReadWrite.All</c> with admin consent. Each test cleans up
    /// the objects it creates.
    /// </summary>
    public class EntraGraphIntegrationTests
    {
        private static bool Enabled =>
            string.Equals(Environment.GetEnvironmentVariable("ADRAPI_RUN_ENTRA_INTEGRATION"), "1", StringComparison.OrdinalIgnoreCase);

        private static string Env(string name) => Environment.GetEnvironmentVariable(name);

        private static GraphDirectoryProvider BuildProvider()
        {
            var cfg = new EntraConfig
            {
                DomainKey = "it",
                TenantId = Env("ENTRA_TENANT_ID"),
                ClientId = Env("ENTRA_CLIENT_ID"),
                ClientSecret = Env("ENTRA_CLIENT_SECRET"),
            };
            var graph = new GraphClient(cfg, EntraTokenProvider.Instance, new HttpClient());
            return new GraphDirectoryProvider(cfg, graph);
        }

        private static string UpnDomain => Env("ENTRA_TEST_UPN_DOMAIN");

        [Fact]
        public async Task Integration_UserLifecycle()
        {
            if (!Enabled) return;

            var provider = BuildProvider();
            var nick = "adrapi-it-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            var upn = $"{nick}@{UpnDomain}";
            var user = new User
            {
                Name = "adrapi IT " + nick,
                Account = nick,
                Login = upn,
                Password = "P" + Guid.NewGuid().ToString("N") + "!aA",
                IsDisabled = false,
            };

            try
            {
                Assert.True(await provider.CreateUserAsync(user));
                Assert.False(string.IsNullOrWhiteSpace(user.ID)); // server-assigned objectId

                Assert.True(await provider.UserExistsAsync(upn));

                var fetched = await provider.GetUserAsync(upn);
                Assert.NotNull(fetched);
                Assert.Equal(upn, fetched.Login);
                Assert.Null(fetched.DN); // normalized: Entra has no DN

                var found = await provider.SearchUsersAsync(nick);
                Assert.Contains(found, u => u.ID == user.ID);

                Assert.True(await provider.SetUserEnabledAsync(user.ID, false));
                Assert.True(await provider.SetUserPasswordAsync(user.ID, "P" + Guid.NewGuid().ToString("N") + "!aA"));
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(user.ID))
                {
                    await provider.DeleteUserAsync(user);
                }
            }

            Assert.False(await provider.UserExistsAsync(upn));
        }

        [Fact]
        public async Task Integration_GroupAndMembership()
        {
            if (!Enabled) return;

            var provider = BuildProvider();
            var nick = "adrapi-it-grp-" + Guid.NewGuid().ToString("N").Substring(0, 10);

            // A member user to add/remove.
            var memberUpn = "adrapi-it-m-" + Guid.NewGuid().ToString("N").Substring(0, 10) + "@" + UpnDomain;
            var member = new User
            {
                Name = "adrapi member",
                Account = memberUpn.Split('@')[0],
                Login = memberUpn,
                Password = "P" + Guid.NewGuid().ToString("N") + "!aA",
                IsDisabled = false,
            };
            var group = new Group { Name = nick, Description = "adrapi IT group", GroupType = "Security" };

            try
            {
                Assert.True(await provider.CreateUserAsync(member));
                Assert.True(await provider.CreateGroupAsync(group));
                Assert.False(string.IsNullOrWhiteSpace(group.ID));

                Assert.True(await provider.GroupExistsAsync(group.ID));

                // delta add
                Assert.True(await provider.AddGroupMembersAsync(group.ID, new[] { member.ID }));
                var members = await provider.GetGroupMembersAsync(group.ID);
                Assert.Contains(members, m => m == member.ID || m == memberUpn);

                // idempotent re-add
                Assert.True(await provider.AddGroupMembersAsync(group.ID, new[] { member.ID }));

                // replace with empty set clears membership
                Assert.True(await provider.ReplaceGroupMembersAsync(group.ID, Array.Empty<string>()));
                Assert.Empty(await provider.GetGroupMembersAsync(group.ID));
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(group.ID)) await provider.DeleteGroupAsync(group);
                if (!string.IsNullOrWhiteSpace(member.ID)) await provider.DeleteUserAsync(member);
            }
        }
    }
}
