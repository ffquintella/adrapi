using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using adrapi.domain;
using adrapi.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Novell.Directory.Ldap;
using tests.Ldap;
using Xunit;
using V1UsersController = adrapi.Controllers.UsersController;
using V2UsersController = adrapi.Controllers.V2.UsersController;

namespace tests
{
    /// <summary>
    /// The v2 users controller over the in-memory directory fake: the LDAP half
    /// of every action, including the pagination contract (one page by default,
    /// <c>all=true</c> to opt out) and the range/validation branches.
    /// </summary>
    public class UsersControllerV2DirectoryTests
    {
        private static V2UsersController Controller()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["ldap:searchBase"] = "DC=homologa,DC=br",
                    ["ldap:servers:0"] = "127.0.0.1:1",
                    ["ldap:ssl"] = "true",
                    ["ldap:maxResults"] = "999"
                })
                .Build();

            adrapi.ConfigurationManager.Instance.Config = config;
            adrapi.Ldap.LdapDomainRegistry.Instance.ClearCache();

            var controller = new V2UsersController(NullLogger<V1UsersController>.Instance, config);
            var context = new DefaultHttpContext();
            context.Request.Headers["api-key"] = "dev-local:abc1234";
            context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
            controller.ControllerContext = new ControllerContext { HttpContext = context };

            return controller;
        }

        private static void SeedUsers(LdapFakeScope scope, int count)
        {
            for (var i = 1; i <= count; i++)
            {
                scope.Query.Add(LdapEntries.User(
                    $"CN=user{i},OU=Users,DC=homologa,DC=br",
                    account: $"user{i}",
                    mail: $"user{i}@homologa.br",
                    memberOf: new[] { "CN=Admins,OU=Groups,DC=homologa,DC=br" }));
            }
        }

        private static UserListResponse ListOf(ActionResult<UserListResponse> result)
            => result.Value ?? Assert.IsType<UserListResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);

        // ---- GET /api/users ----

        [Fact]
        public async Task List_ReturnsOnePageWithCookieByDefault()
        {
            using var scope = new LdapFakeScope();
            SeedUsers(scope, 5);
            scope.Query.PageSize = 2;

            var response = ListOf(await Controller().Get());

            Assert.Equal(2, response.UserNames.Count);
            Assert.Equal("2", response.Cookie);
        }

        [Fact]
        public async Task List_ContinuesFromCookie()
        {
            using var scope = new LdapFakeScope();
            SeedUsers(scope, 3);
            scope.Query.PageSize = 2;

            var response = ListOf(await Controller().Get(_cookie: "2"));

            Assert.Equal(new[] { "user3" }, response.UserNames);
            Assert.Equal("", response.Cookie);
        }

        [Fact]
        public async Task List_AllTrue_WalksEveryPage()
        {
            using var scope = new LdapFakeScope();
            SeedUsers(scope, 5);
            scope.Query.PageSize = 2;

            var response = ListOf(await Controller().Get(all: true));

            Assert.Equal(5, response.UserNames.Count);
            Assert.Equal("", response.Cookie);
        }

        [Fact]
        public async Task List_WithFilterAndAttribute_NarrowsAndProjects()
        {
            using var scope = new LdapFakeScope();
            SeedUsers(scope, 3);
            scope.Query.PageSize = 10;

            // With _attribute set, _filter is matched against that same attribute.
            var response = ListOf(await Controller().Get(_attribute: "mail", _filter: "user2@homologa.br"));

            Assert.Equal(new[] { "user2@homologa.br" }, response.UserNames);
        }

        [Fact]
        public async Task List_Range_ReturnsRequestedSlice()
        {
            using var scope = new LdapFakeScope();
            SeedUsers(scope, 5);
            scope.Query.PageSize = 2;

            var response = ListOf(await Controller().Get(_start: 2, _end: 3));

            Assert.Equal(2, response.UserNames.Count);
        }

        [Fact]
        public async Task List_Range_ZeroStartIsTreatedAsFirstItem()
        {
            using var scope = new LdapFakeScope();
            SeedUsers(scope, 3);
            scope.Query.PageSize = 5;

            var response = ListOf(await Controller().Get(_start: 0, _end: 2));

            Assert.Equal(2, response.UserNames.Count);
        }

        [Fact]
        public async Task List_InvalidRange_IsConflict()
        {
            using var scope = new LdapFakeScope();
            var controller = Controller();

            Assert.IsType<ConflictResult>((await controller.Get(_start: -2, _end: 3)).Result);
            Assert.IsType<ConflictResult>((await controller.Get(_start: 5, _end: 2)).Result);
        }

        // ---- GET /api/users?_full=true ----

        [Fact]
        public async Task FullList_ReturnsOnePageOfObjects()
        {
            using var scope = new LdapFakeScope();
            SeedUsers(scope, 5);
            scope.Query.PageSize = 2;

            var response = ListOf(await Controller().Get(_full: true, _start: 0, _end: 0));

            Assert.Equal(2, response.Users.Count);
            Assert.Equal("2", response.Cookie);
            Assert.Equal("user1@homologa.br", response.Users[0].Mail);
        }

        [Fact]
        public async Task FullList_AllTrue_ReturnsEveryUser()
        {
            using var scope = new LdapFakeScope();
            SeedUsers(scope, 4);
            scope.Query.PageSize = 2;

            var response = ListOf(await Controller().Get(_full: true, _start: 0, _end: 0, all: true));

            Assert.Equal(4, response.Users.Count);
        }

        [Fact]
        public async Task FullList_Range_HonoursBounds()
        {
            using var scope = new LdapFakeScope();
            SeedUsers(scope, 5);

            var response = ListOf(await Controller().Get(_full: true, _start: 2, _end: 3));

            Assert.Equal(2, response.Users.Count);
            Assert.Equal("user2", response.Users[0].Account);
        }

        [Fact]
        public async Task FullList_EndWithoutStart_IsConflict()
        {
            using var scope = new LdapFakeScope();

            var result = await Controller().Get(_full: true, _start: 0, _end: 5);

            Assert.IsType<ConflictResult>(result.Result);
        }

        // ---- GET /api/users/{user} and friends ----

        [Fact]
        public async Task GetUser_FoundAndNotFound()
        {
            using var scope = new LdapFakeScope();
            SeedUsers(scope, 2);
            var controller = Controller();

            var found = await controller.Get("user1");
            Assert.Equal("user1", found.Value.Account);

            var byAttribute = await controller.Get("user2@homologa.br", _attribute: "mail");
            Assert.Equal("user2", byAttribute.Value.Account);

            Assert.IsType<NotFoundResult>((await controller.Get("ghost")).Result);
        }

        [Fact]
        public async Task GetExists_OkAndNotFound()
        {
            using var scope = new LdapFakeScope();
            SeedUsers(scope, 1);
            var controller = Controller();

            Assert.IsType<OkResult>(await controller.GetExists("user1"));
            Assert.IsType<OkResult>(await controller.GetExists("user1@homologa.br", _attribute: "mail"));
            Assert.IsType<NotFoundResult>(await controller.GetExists("ghost"));
        }

        [Fact]
        public async Task GetAttributes_ReturnsInspection_AndValidatesInput()
        {
            using var scope = new LdapFakeScope();
            SeedUsers(scope, 1);
            var controller = Controller();

            var inspection = await controller.GetAttributes("CN=user1,OU=Users,DC=homologa,DC=br");
            Assert.Equal("CN=user1,OU=Users,DC=homologa,DC=br", inspection.Value.DistinguishedName);

            Assert.IsType<BadRequestResult>((await controller.GetAttributes("  ")).Result);
            Assert.IsType<NotFoundResult>((await controller.GetAttributes("ghost")).Result);
        }

        [Fact]
        public async Task IsMemberOf_MatchesByDnAndCn_AndReports250WhenNotAMember()
        {
            using var scope = new LdapFakeScope();
            SeedUsers(scope, 1);
            var controller = Controller();

            Assert.IsType<OkResult>(await controller.IsMemberOf("user1", "CN=Admins,OU=Groups,DC=homologa,DC=br"));
            Assert.IsType<OkResult>(await controller.IsMemberOf("user1", "Admins"));

            var notMember = Assert.IsType<StatusCodeResult>(await controller.IsMemberOf("user1", "Auditors"));
            Assert.Equal(250, notMember.StatusCode);

            Assert.IsType<NotFoundResult>(await controller.IsMemberOf("ghost", "Admins"));
            Assert.IsType<BadRequestResult>(await controller.IsMemberOf("user1", " "));
        }

        [Fact]
        public async Task GetGroups_ListsDnsAndCns()
        {
            using var scope = new LdapFakeScope();
            SeedUsers(scope, 1);
            var controller = Controller();

            var groups = await controller.GetGroups("user1");

            Assert.Equal(new[] { "CN=Admins,OU=Groups,DC=homologa,DC=br" }, groups.Value.MemberOfDns);
            Assert.Equal(new[] { "Admins" }, groups.Value.MemberOfCns);

            Assert.IsType<BadRequestResult>((await controller.GetGroups(" ")).Result);
            Assert.IsType<NotFoundResult>((await controller.GetGroups("ghost")).Result);
        }

        // ---- Authentication ----

        [Fact]
        public async Task Authenticate_ValidatesCredentialsForAKnownUser()
        {
            using var scope = new LdapFakeScope();
            SeedUsers(scope, 1);
            // The manager resolves the account to a DN before binding.
            scope.Auth.ValidCredentials["CN=user1,OU=Users,DC=homologa,DC=br"] = "pass";
            var controller = Controller();

            Assert.IsType<OkResult>(await controller.Authenticate("user1", new AuthenticationRequest { Password = "pass" }));

            var wrong = Assert.IsType<StatusCodeResult>(
                await controller.Authenticate("user1", new AuthenticationRequest { Password = "nope" }));
            Assert.Equal(401, wrong.StatusCode);
        }

        [Fact]
        public async Task Authenticate_UseAccount_AndMissingInput()
        {
            using var scope = new LdapFakeScope();
            SeedUsers(scope, 1);
            scope.Auth.ValidCredentials["CN=user1,OU=Users,DC=homologa,DC=br"] = "pass";
            var controller = Controller();

            Assert.IsType<OkResult>(await controller.Authenticate(
                "user1", new AuthenticationRequest { Password = "pass" }, _useAccount: true));

            Assert.IsType<BadRequestResult>(await controller.Authenticate("user1", null));
            Assert.IsType<NotFoundResult>(await controller.Authenticate(
                "ghost", new AuthenticationRequest { Password = "pass" }));
        }

        [Fact]
        public async Task AuthenticateDirect_ChecksCredentialsWithoutLookup()
        {
            using var scope = new LdapFakeScope();
            scope.Auth.ValidCredentials["ada@homologa.br"] = "pass";
            var controller = Controller();

            Assert.IsType<OkResult>(await controller.AuthenticateDirect(
                new AuthenticationRequest { Login = "ada@homologa.br", Password = "pass" }));

            var wrong = Assert.IsType<StatusCodeResult>(await controller.AuthenticateDirect(
                new AuthenticationRequest { Login = "ada@homologa.br", Password = "nope" }));
            Assert.Equal(401, wrong.StatusCode);

            Assert.IsType<BadRequestResult>(await controller.AuthenticateDirect(
                new AuthenticationRequest { Password = "pass" }));
            Assert.IsType<BadRequestResult>(await controller.AuthenticateDirect(null));
        }

        // ---- PUT ----

        [Fact]
        public async Task Put_CreatesWhenAbsent()
        {
            using var scope = new LdapFakeScope();
            var controller = Controller();
            var dn = "CN=ada,OU=Users,DC=homologa,DC=br";

            var result = await controller.Put(dn, new User { Account = "ada", Name = "Ada" });

            Assert.IsType<OkResult>(result);
            Assert.Equal(dn, Assert.Single(scope.Query.Added).Dn);
        }

        [Fact]
        public async Task Put_UpdatesWhenPresent()
        {
            using var scope = new LdapFakeScope();
            SeedUsers(scope, 1);
            var dn = "CN=user1,OU=Users,DC=homologa,DC=br";

            var result = await Controller().Put(dn, new User { Account = "user1", Name = "User 1", Mail = "new@homologa.br" });

            Assert.IsType<OkResult>(result);
            Assert.Equal(dn, Assert.Single(scope.Query.Saved).dn);
        }

        [Fact]
        public async Task Put_RejectsMismatchedOrMalformedDn()
        {
            using var scope = new LdapFakeScope();
            var controller = Controller();

            Assert.IsType<ConflictResult>(await controller.Put(
                "CN=ada,OU=Users,DC=homologa,DC=br",
                new User { DN = "CN=other,OU=Users,DC=homologa,DC=br", Account = "ada", Name = "Ada" }));

            Assert.IsType<ConflictResult>(await controller.Put(
                "not-a-dn", new User { Account = "ada", Name = "Ada" }));
        }

        [Fact]
        public async Task Put_DirectoryFailure_Is500()
        {
            using var scope = new LdapFakeScope();
            var controller = Controller();
            scope.Query.Throw = new LdapException("boom");

            var result = await controller.Put(
                "CN=ada,OU=Users,DC=homologa,DC=br", new User { Account = "ada", Name = "Ada" });

            Assert.Equal(500, Assert.IsType<StatusCodeResult>(result).StatusCode);
        }

        // ---- DELETE ----

        [Fact]
        public async Task Delete_RemovesByDn()
        {
            using var scope = new LdapFakeScope();
            SeedUsers(scope, 1);
            var dn = "CN=user1,OU=Users,DC=homologa,DC=br";

            var result = await Controller().Delete(dn);

            Assert.IsType<OkResult>(result);
            Assert.Equal(dn, Assert.Single(scope.Query.Deleted));
        }

        [Fact]
        public async Task Delete_ByAttribute_AndUnknownUser()
        {
            using var scope = new LdapFakeScope();
            SeedUsers(scope, 2);
            var controller = Controller();

            Assert.IsType<OkResult>(await controller.Delete("user2@homologa.br", _attribute: "mail"));
            Assert.IsType<NotFoundResult>(await controller.Delete("CN=ghost,OU=Users,DC=homologa,DC=br"));
        }
    }
}
