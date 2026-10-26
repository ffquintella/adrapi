using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using adrapi.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Novell.Directory.Ldap;
using tests.Ldap;
using Xunit;
using Group = adrapi.domain.Group;
using OU = adrapi.domain.OU;
using User = adrapi.domain.User;
using AuthenticationRequest = adrapi.domain.AuthenticationRequest;
using V1GroupsController = adrapi.Controllers.GroupsController;
using V1UsersController = adrapi.Controllers.UsersController;
using V2GroupsController = adrapi.Controllers.V2.GroupsController;

namespace tests
{
    public class ControllerCoverageTests
    {
        private const string SearchBase = "DC=homologa,DC=br";
        private const string GroupDn = "CN=team,OU=Groups,DC=homologa,DC=br";
        private const string UserDn = "CN=ada,OU=Users,DC=homologa,DC=br";

        private static IConfiguration BuildConfiguration()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["ldap:searchBase"] = SearchBase,
                    ["ldap:servers:0"] = "127.0.0.1:1",
                    ["ldap:ssl"] = "true",
                    ["ldap:maxResults"] = "999",
                    ["ldap:protectedOUs:0"] = $"OU=Protected,{SearchBase}"
                })
                .Build();

            adrapi.ConfigurationManager.Instance.Config = configuration;
            adrapi.Ldap.LdapDomainRegistry.Instance.ClearCache();
            return configuration;
        }

        private static T WithHttpContext<T>(T controller) where T : ControllerBase
        {
            var context = new DefaultHttpContext();
            context.Request.Headers["api-key"] = "dev-local:abc1234";
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            controller.ControllerContext = new ControllerContext { HttpContext = context };
            return controller;
        }

        private static V2GroupsController V2Groups()
            => WithHttpContext(new V2GroupsController(
                NullLogger<V2GroupsController>.Instance,
                BuildConfiguration()));

        private static V1GroupsController V1Groups()
            => WithHttpContext(new V1GroupsController(
                NullLogger<V1GroupsController>.Instance,
                BuildConfiguration()));

        private static adrapi.Controllers.OUsController OUs()
            => WithHttpContext(new adrapi.Controllers.OUsController(
                NullLogger<V1GroupsController>.Instance,
                BuildConfiguration()));

        private static V1UsersController V1Users()
            => WithHttpContext(new V1UsersController(
                NullLogger<V1UsersController>.Instance,
                BuildConfiguration()));

        private static void SeedGroup(LdapFakeScope scope, string dn = GroupDn, IEnumerable<string> members = null)
            => scope.Query.Add(LdapEntries.Group(dn, description: "Original", members: members));

        private static void SeedUser(LdapFakeScope scope, string dn = UserDn, string account = "ada")
            => scope.Query.Add(LdapEntries.User(dn, account: account, mail: $"{account}@homologa.br"));

        private static void SeedUser(
            LdapFakeScope scope,
            string dn,
            string account,
            IEnumerable<string> memberOf)
            => scope.Query.Add(LdapEntries.User(
                dn,
                account: account,
                mail: $"{account}@homologa.br",
                memberOf: memberOf));

        [Fact]
        public async Task V2Groups_ListActions_ReturnNamesRangesFullObjectsAndValidationResults()
        {
            using var scope = new LdapFakeScope();
            SeedGroup(scope, "CN=alpha,OU=Groups,DC=homologa,DC=br");
            SeedGroup(scope, "CN=beta,OU=Groups,DC=homologa,DC=br");
            SeedGroup(scope, "CN=gamma,OU=Groups,DC=homologa,DC=br");
            var controller = V2Groups();

            Assert.Equal(new[] { "alpha", "beta", "gamma" }, (await controller.Get(0, 0)).Value);
            Assert.Equal(new[] { "beta", "gamma" }, (await controller.Get(2, 3)).Value);
            Assert.IsType<ConflictResult>((await controller.Get(0, 2)).Result);

            var full = (await controller.Get(true, 0, 0)).Value.ToList();
            Assert.Equal(3, full.Count);
            Assert.Equal("Original", full[0].Description);
            Assert.Empty((await controller.Get(false, 0, 0)).Value);
            Assert.IsType<ConflictResult>((await controller.Get(true, 0, 2)).Result);
        }

        [Fact]
        public async Task V2Groups_List_UnknownAndReservedDomainsAreRejectedBeforeDirectoryAccess()
        {
            using var scope = new LdapFakeScope();
            var controller = V2Groups();

            Assert.IsType<NotFoundResult>((await controller.Get(0, 0, "missing")).Result);
            Assert.IsType<BadRequestResult>((await controller.Get(0, 0, "users")).Result);
            Assert.Empty(scope.Query.Calls);
        }

        [Fact]
        public async Task V2Groups_Get_ResolvesDnAndCnAndReturnsNotFoundForUnknownGroup()
        {
            using var scope = new LdapFakeScope();
            SeedGroup(scope);
            var controller = V2Groups();

            Assert.Equal("team", (await controller.Get(GroupDn)).Value.Name);
            Assert.Equal(GroupDn, (await controller.Get("team")).Value.DN);
            Assert.IsType<NotFoundResult>((await controller.Get("ghost")).Result);
            Assert.IsType<BadRequestResult>((await controller.Get(" ")).Result);
        }

        [Fact]
        public async Task V2Groups_Get_BackendErrorIs500()
        {
            using var scope = new LdapFakeScope();
            scope.Query.Throw = new System.InvalidOperationException("backend unavailable");

            var result = await V2Groups().Get(GroupDn);

            Assert.Equal(500, Assert.IsType<StatusCodeResult>(result.Result).StatusCode);
        }

        [Fact]
        public async Task V2Groups_Exists_CoversFoundMissingMalformedAndBackendError()
        {
            using var scope = new LdapFakeScope();
            SeedGroup(scope);
            var controller = V2Groups();

            Assert.IsType<OkResult>(await controller.GetExists(GroupDn));
            Assert.IsType<NotFoundResult>(await controller.GetExists("CN=ghost,OU=Groups,DC=homologa,DC=br"));
            Assert.IsType<ConflictResult>(await controller.GetExists("team"));

            scope.Query.Throw = new System.InvalidOperationException("backend unavailable");
            Assert.Equal(500, Assert.IsType<StatusCodeResult>(await controller.GetExists(GroupDn)).StatusCode);
        }

        [Fact]
        public async Task V2Groups_GetMembers_ReturnsDnsOrCnsAndValidatesUnknownInput()
        {
            using var scope = new LdapFakeScope();
            SeedGroup(scope, members: new[] { UserDn, "not-a-dn", "" });
            var controller = V2Groups();

            Assert.Equal(new[] { UserDn, "not-a-dn" }, (await controller.GetMembers(GroupDn, false)).Value);
            Assert.Equal(new[] { "ada" }, (await controller.GetMembers("team", true)).Value);
            Assert.IsType<NotFoundResult>((await controller.GetMembers("ghost")).Result);
            Assert.IsType<BadRequestResult>((await controller.GetMembers(" ")).Result);
        }

        [Fact]
        public async Task V2Groups_GetMembers_BackendErrorIs500()
        {
            using var scope = new LdapFakeScope();
            scope.Query.Throw = new System.InvalidOperationException("backend unavailable");

            var result = await V2Groups().GetMembers(GroupDn);

            Assert.Equal(500, Assert.IsType<StatusCodeResult>(result.Result).StatusCode);
        }

        [Fact]
        public async Task V2Groups_Post_CreatesGroupAndResolvesMemberAccounts()
        {
            using var scope = new LdapFakeScope();
            SeedUser(scope);

            var result = await V2Groups().Post(new GroupCreateRequest
            {
                DN = GroupDn,
                Name = "team",
                Description = "Created",
                Members = new List<string> { "ada", "ADA" }
            }, _listCN: true);

            Assert.IsType<OkResult>(result);
            var added = Assert.Single(scope.Query.Added);
            Assert.Equal(GroupDn, added.Dn);
            Assert.Equal(UserDn, added.GetAttributeSet().GetAttribute("member").StringValue);
        }

        [Fact]
        public async Task V2Groups_Post_RejectsInvalidConflictingAndUnresolvedRequests()
        {
            using var scope = new LdapFakeScope();
            SeedGroup(scope);
            var controller = V2Groups();

            Assert.IsType<BadRequestResult>(await controller.Post(null));
            Assert.IsType<BadRequestResult>(await controller.Post(new GroupCreateRequest { Name = "team" }));
            Assert.IsType<ConflictResult>(await controller.Post(new GroupCreateRequest { DN = "invalid", Name = "team" }));
            Assert.IsType<ConflictResult>(await controller.Post(new GroupCreateRequest { DN = GroupDn, Name = "other" }));
            Assert.IsType<ConflictResult>(await controller.Post(new GroupCreateRequest { DN = GroupDn, Name = "team" }));

            var unresolved = await controller.Post(new GroupCreateRequest
            {
                DN = "CN=new,OU=Groups,DC=homologa,DC=br",
                Name = "new",
                Members = new List<string> { "missing-user" }
            });
            Assert.Equal(422, Assert.IsType<StatusCodeResult>(unresolved).StatusCode);
            Assert.Empty(scope.Query.Added);
        }

        [Fact]
        public async Task V2Groups_Post_DirectoryWriteFailureIs500()
        {
            using var scope = new LdapFakeScope();
            scope.Query.Throw = new LdapException("backend unavailable");

            var result = await V2Groups().Post(new GroupCreateRequest
            {
                DN = GroupDn,
                Name = "team",
                Description = "Created"
            });

            Assert.Equal(500, Assert.IsType<StatusCodeResult>(result).StatusCode);
        }

        [Fact]
        public async Task V2Groups_Put_CreatesThenUpdatesAndRecordsDirectoryWrites()
        {
            using var scope = new LdapFakeScope();
            SeedUser(scope);
            var controller = V2Groups();

            Assert.IsType<OkResult>(await controller.Put(GroupDn, new Group
            {
                Name = "team",
                Description = "Created",
                Member = new List<string> { UserDn }
            }));
            Assert.Equal(GroupDn, Assert.Single(scope.Query.Added).Dn);

            Assert.IsType<OkResult>(await controller.Put(GroupDn, new Group
            {
                DN = GroupDn,
                Name = "team",
                Description = "Updated",
                Member = new List<string> { "ada" }
            }, _listCN: true));
            var saved = Assert.Single(scope.Query.Saved);
            Assert.Equal(GroupDn, saved.dn);
            Assert.Contains(saved.mods, modification =>
                modification.Attribute.Name == "description" && modification.Attribute.StringValue == "Updated");
        }

        [Fact]
        public async Task V2Groups_Put_ValidatesBodyDnNameConflictsAndMembers()
        {
            using var scope = new LdapFakeScope();
            var controller = V2Groups();

            Assert.IsType<BadRequestResult>(await controller.Put(GroupDn, null));
            Assert.IsType<ConflictResult>(await controller.Put(GroupDn, new Group
            {
                DN = "CN=other,OU=Groups,DC=homologa,DC=br",
                Name = "team"
            }));
            Assert.IsType<ConflictResult>(await controller.Put("invalid", new Group { Name = "team" }));
            Assert.IsType<ConflictResult>(await controller.Put(GroupDn, new Group { Name = "other" }));

            var unresolved = await controller.Put(GroupDn, new Group
            {
                Name = "team",
                Member = new List<string> { "missing-user" }
            });
            Assert.Equal(422, Assert.IsType<StatusCodeResult>(unresolved).StatusCode);
        }

        [Fact]
        public async Task V2Groups_PutMembers_ReplacesMembersAndCoversValidationBranches()
        {
            using var scope = new LdapFakeScope();
            SeedUser(scope);
            SeedGroup(scope, members: new[] { "CN=old,OU=Users,DC=homologa,DC=br" });
            var controller = V2Groups();

            Assert.IsType<OkResult>(await controller.PutMembers(GroupDn, new[] { "ada" }, _listCN: true));
            var saved = Assert.Single(scope.Query.Saved);
            Assert.Contains(saved.mods, modification =>
                modification.Op == LdapModification.Add &&
                modification.Attribute.Name == "member" &&
                modification.Attribute.StringValue == UserDn);

            Assert.IsType<ConflictResult>(await controller.PutMembers("invalid", new[] { "ada" }));
            Assert.IsType<NotFoundResult>(await controller.PutMembers(
                "CN=ghost,OU=Groups,DC=homologa,DC=br", new[] { "ada" }));
            Assert.Equal(422, Assert.IsType<StatusCodeResult>(
                await controller.PutMembers(GroupDn, new[] { "ghost" })).StatusCode);
        }

        [Fact]
        public async Task V2Groups_PatchMembers_AddsRemovesDeduplicatesAndValidatesRequests()
        {
            using var scope = new LdapFakeScope();
            var oldDn = "CN=old,OU=Users,DC=homologa,DC=br";
            SeedUser(scope);
            SeedUser(scope, oldDn, "old");
            SeedGroup(scope, members: new[] { oldDn });
            var controller = V2Groups();

            var result = await controller.PatchMembers(GroupDn, new GroupMembersPatchRequest
            {
                Add = new List<string> { "ada", "ADA" },
                Remove = new List<string> { "old" }
            }, _listCN: true);

            Assert.IsType<OkResult>(result);
            var saved = Assert.Single(scope.Query.Saved);
            Assert.Contains(saved.mods, modification =>
                modification.Op == LdapModification.Add && modification.Attribute.StringValue == UserDn);

            Assert.IsType<BadRequestResult>(await controller.PatchMembers(GroupDn, null));
            Assert.IsType<BadRequestResult>(await controller.PatchMembers(GroupDn, new GroupMembersPatchRequest()));
            Assert.IsType<ConflictResult>(await controller.PatchMembers("invalid", new GroupMembersPatchRequest
            {
                Add = new List<string> { "ada" }
            }));
            Assert.IsType<NotFoundResult>(await controller.PatchMembers(
                "CN=ghost,OU=Groups,DC=homologa,DC=br",
                new GroupMembersPatchRequest { Add = new List<string> { "ada" } }));
            Assert.Equal(422, Assert.IsType<StatusCodeResult>(await controller.PatchMembers(
                GroupDn,
                new GroupMembersPatchRequest { Add = new List<string> { "missing" } })).StatusCode);
        }

        [Fact]
        public async Task V2Groups_Delete_RemovesExistingAndCoversMalformedMissingAndWriteFailure()
        {
            using var scope = new LdapFakeScope();
            SeedGroup(scope);
            var controller = V2Groups();

            Assert.IsType<ConflictResult>(await controller.Delete("invalid"));
            Assert.IsType<NotFoundResult>(await controller.Delete("CN=ghost,OU=Groups,DC=homologa,DC=br"));
            Assert.IsType<OkResult>(await controller.Delete(GroupDn));
            Assert.Equal(GroupDn, Assert.Single(scope.Query.Deleted));

            SeedGroup(scope);
            scope.Query.Throw = new LdapException("backend unavailable");
            Assert.IsType<NotFoundResult>(await controller.Delete(GroupDn));
        }

        [Fact]
        public async Task LegacyGroups_ReadsAndWritesThroughTheDirectoryFake()
        {
            using var scope = new LdapFakeScope();
            SeedUser(scope);
            SeedGroup(scope, members: new[] { UserDn });
            var controller = V1Groups();

            Assert.Equal(new[] { GroupDn }, (await controller.Get(0, 0)).Value);
            Assert.Equal("team", (await controller.Get(GroupDn)).Value.Name);
            Assert.IsType<OkResult>(await controller.GetExists(GroupDn));
            Assert.Equal(new[] { "ada" }, (await controller.GetMembers(GroupDn, true)).Value);

            Assert.IsType<OkResult>(await controller.PutMembers(GroupDn, new[] { UserDn }));
            Assert.Equal(GroupDn, Assert.Single(scope.Query.Saved).dn);
            Assert.IsType<OkResult>(await controller.Delete(GroupDn));
            Assert.Equal(GroupDn, Assert.Single(scope.Query.Deleted));
        }

        [Fact]
        public async Task LegacyGroups_ListAndLookupCoverRangeFullAndValidationResults()
        {
            using var scope = new LdapFakeScope();
            SeedGroup(scope, "CN=alpha,OU=Groups,DC=homologa,DC=br");
            SeedGroup(scope, "CN=beta,OU=Groups,DC=homologa,DC=br");
            SeedGroup(scope, "CN=gamma,OU=Groups,DC=homologa,DC=br");
            var controller = V1Groups();

            Assert.Equal(3, (await controller.Get(0, 0)).Value.Count());
            Assert.Equal(2, (await controller.Get(2, 3)).Value.Count());
            Assert.IsType<ConflictResult>((await controller.Get(0, 2)).Result);
            Assert.Equal(3, (await controller.Get(true, 0, 0)).Value.Count());
            Assert.Empty((await controller.Get(false, 0, 0)).Value);
            Assert.IsType<ConflictResult>((await controller.Get(true, 0, 2)).Result);

            Assert.IsType<ConflictResult>((await controller.Get("invalid")).Result);
            Assert.IsType<NotFoundResult>((await controller.Get(
                "CN=ghost,OU=Groups,DC=homologa,DC=br")).Result);
            Assert.IsType<NotFoundResult>(await controller.GetExists(
                "CN=ghost,OU=Groups,DC=homologa,DC=br"));
            Assert.IsType<ConflictResult>(await controller.GetExists("invalid"));
            Assert.IsType<BadRequestResult>((await controller.GetMembers(" ")).Result);
            Assert.IsType<NotFoundResult>((await controller.GetMembers("ghost")).Result);
        }

        [Fact]
        public async Task LegacyGroups_PutCreatesUpdatesAndValidatesMemberResolution()
        {
            using var scope = new LdapFakeScope();
            SeedUser(scope);
            var controller = V1Groups();

            Assert.IsType<OkResult>(await controller.Put(GroupDn, new Group
            {
                Name = "team",
                Description = "Created",
                Member = new List<string> { "ada" }
            }, _listCN: true));
            Assert.Equal(UserDn,
                Assert.Single(scope.Query.Added).GetAttributeSet().GetAttribute("member").StringValue);

            Assert.IsType<OkResult>(await controller.Put(GroupDn, new Group
            {
                DN = GroupDn,
                Name = "team",
                Description = "Updated",
                Member = new List<string> { UserDn }
            }));
            Assert.Equal(GroupDn, Assert.Single(scope.Query.Saved).dn);

            Assert.IsType<BadRequestResult>(await controller.Put(GroupDn, null));
            Assert.IsType<ConflictResult>(await controller.Put("invalid", new Group { Name = "team" }));
            Assert.IsType<ConflictResult>(await controller.Put(GroupDn, new Group { Name = "other" }));
            Assert.IsType<ConflictResult>(await controller.Put(GroupDn, new Group
            {
                DN = "CN=other,OU=Groups,DC=homologa,DC=br",
                Name = "team"
            }));
            Assert.Equal(422, Assert.IsType<StatusCodeResult>(await controller.Put(GroupDn, new Group
            {
                Name = "team",
                Member = new List<string> { "missing" }
            }, _listCN: true)).StatusCode);
        }

        [Fact]
        public async Task LegacyGroups_GuardedActionsTranslateBackendExceptionsAndValidateMemberUpdates()
        {
            using var scope = new LdapFakeScope();
            SeedGroup(scope);
            var controller = V1Groups();

            Assert.IsType<ConflictResult>(await controller.PutMembers("invalid", new[] { UserDn }));
            Assert.Equal(422, Assert.IsType<StatusCodeResult>(
                await controller.PutMembers(GroupDn, new[] { "missing" }, _listCN: true)).StatusCode);

            scope.Query.Throw = new System.InvalidOperationException("backend unavailable");
            Assert.Equal(500, Assert.IsType<StatusCodeResult>((await controller.Get(GroupDn)).Result).StatusCode);
            Assert.Equal(500, Assert.IsType<StatusCodeResult>(await controller.GetExists(GroupDn)).StatusCode);
            Assert.Equal(500, Assert.IsType<StatusCodeResult>((await controller.GetMembers(GroupDn)).Result).StatusCode);
            Assert.Equal(500, Assert.IsType<StatusCodeResult>(
                await controller.PutMembers(GroupDn, new[] { UserDn })).StatusCode);
        }

        [Fact]
        public async Task LegacyUsers_ListActionsCoverFiltersRangesAndFullObjects()
        {
            using var scope = new LdapFakeScope();
            SeedUser(scope, UserDn, "ada");
            SeedUser(scope, "CN=grace,OU=Users,DC=homologa,DC=br", "grace");
            SeedUser(scope, "CN=linus,OU=Users,DC=homologa,DC=br", "linus");
            scope.Query.PageSize = 10;
            var controller = V1Users();

            Assert.Equal(new[] { "ada", "grace", "linus" }, (await controller.Get(0, 0)).Value.UserNames);
            Assert.Equal(new[] { "grace@homologa.br" },
                (await controller.Get(0, 0, "mail", "grace@homologa.br")).Value.UserNames);
            Assert.Equal(
                new[] { UserDn, "CN=grace,OU=Users,DC=homologa,DC=br" },
                (await controller.Get(0, 2)).Value.UserNames);
            Assert.Equal(new[] { "linus@homologa.br" },
                (await controller.Get(3, 3, "mail")).Value.UserNames);
            Assert.IsType<ConflictResult>((await controller.Get(-1, 2)).Result);
            Assert.IsType<ConflictResult>((await controller.Get(3, 2)).Result);

            Assert.Equal(3, (await controller.Get(true, 0, 0)).Value.Count());
            Assert.Equal(2, (await controller.Get(true, 1, 2)).Value.Count());
            Assert.Empty((await controller.Get(false, 0, 0)).Value);
            Assert.IsType<ConflictResult>((await controller.Get(true, 0, 2)).Result);
        }

        [Fact]
        public async Task LegacyUsers_ReadMembershipAndInspectionReturnMeaningfulResults()
        {
            using var scope = new LdapFakeScope();
            SeedUser(scope, UserDn, "ada", new[] { GroupDn });
            var controller = V1Users();

            Assert.Equal("ada", (await controller.Get("ada")).Value.Account);
            Assert.Equal("ada", (await controller.Get("ada@homologa.br", "mail")).Value.Account);
            Assert.IsType<NotFoundResult>((await controller.Get("ghost")).Result);
            Assert.IsType<OkResult>(await controller.GetExists("ada"));
            Assert.IsType<NotFoundResult>(await controller.GetExists("ghost"));
            Assert.IsType<BadRequestResult>(await controller.GetExists(" "));

            var inspection = await controller.GetAttributes(UserDn);
            Assert.Equal(UserDn, inspection.Value.DistinguishedName);
            Assert.IsType<BadRequestResult>((await controller.GetAttributes(" ")).Result);
            Assert.IsType<NotFoundResult>((await controller.GetAttributes("ghost")).Result);

            Assert.IsType<OkResult>(await controller.IsMemberOf("ada", GroupDn));
            Assert.IsType<OkResult>(await controller.IsMemberOf("ada", "team"));
            Assert.Equal(250, Assert.IsType<StatusCodeResult>(
                await controller.IsMemberOf("ada", "other")).StatusCode);
            Assert.IsType<NotFoundResult>(await controller.IsMemberOf("ghost", "team"));
            Assert.IsType<BadRequestResult>(await controller.IsMemberOf("ada", " "));

            var groups = await controller.GetGroups("ada");
            Assert.Equal(new[] { GroupDn }, groups.Value.MemberOfDns);
            Assert.Equal(new[] { "team" }, groups.Value.MemberOfCns);
        }

        [Fact]
        public async Task LegacyUsers_AuthenticationUsesTheFakeAuthenticator()
        {
            using var scope = new LdapFakeScope();
            SeedUser(scope);
            scope.Auth.ValidCredentials[UserDn] = "correct";
            scope.Auth.ValidCredentials["ada@homologa.br"] = "direct";
            var controller = V1Users();

            Assert.IsType<OkResult>(await controller.Authenticate(
                "ada", new AuthenticationRequest { Password = "correct" }));
            Assert.Equal(401, Assert.IsType<StatusCodeResult>(await controller.Authenticate(
                "ada", new AuthenticationRequest { Password = "wrong" })).StatusCode);
            Assert.IsType<OkResult>(await controller.Authenticate(
                "ada", new AuthenticationRequest { Login = "ada", Password = "correct" }, _useAccount: true));
            Assert.IsType<BadRequestResult>(await controller.Authenticate("ada", null));
            Assert.IsType<NotFoundResult>(await controller.Authenticate(
                "ghost", new AuthenticationRequest { Password = "correct" }));

            Assert.IsType<OkResult>(await controller.AuthenticateDirect(new AuthenticationRequest
            {
                Login = "ada@homologa.br",
                Password = "direct"
            }));
            Assert.Equal(401, Assert.IsType<StatusCodeResult>(await controller.AuthenticateDirect(
                new AuthenticationRequest { Login = "ada@homologa.br", Password = "wrong" })).StatusCode);
            Assert.IsType<BadRequestResult>(await controller.AuthenticateDirect(null));
        }

        [Fact]
        public async Task LegacyUsers_CreateUpdateAndDeleteRecordDirectoryWrites()
        {
            using var scope = new LdapFakeScope();
            SeedUser(scope);
            var controller = V1Users();
            var graceDn = "CN=grace,OU=Users,DC=homologa,DC=br";

            Assert.IsType<OkResult>(await controller.Put(graceDn, new User
            {
                Account = "grace",
                Name = "Grace Hopper",
                Mail = "grace@homologa.br"
            }));
            Assert.Equal(graceDn, Assert.Single(scope.Query.Added).Dn);

            Assert.IsType<OkResult>(await controller.Put(graceDn, new User
            {
                DN = graceDn,
                Account = "grace",
                Name = "Rear Admiral Grace Hopper",
                Mail = "grace@homologa.br"
            }));
            Assert.Equal(graceDn, Assert.Single(scope.Query.Saved).dn);

            Assert.IsType<ConflictResult>(await controller.Put("invalid", new User { Account = "invalid" }));
            Assert.IsType<ConflictResult>(await controller.Put(graceDn, new User
            {
                DN = UserDn,
                Account = "grace"
            }));
            Assert.IsType<ConflictResult>(await controller.Delete("invalid"));
            Assert.IsType<NotFoundResult>(await controller.Delete(
                "CN=ghost,OU=Users,DC=homologa,DC=br"));

            Assert.IsType<OkResult>(await controller.Delete("ada@homologa.br", "mail"));
            Assert.Equal(UserDn, Assert.Single(scope.Query.Deleted));
        }

        [Fact]
        public async Task LegacyUsers_BackendExceptionsAreTranslatedByGuardedReadActions()
        {
            using var scope = new LdapFakeScope();
            scope.Query.Throw = new System.InvalidOperationException("backend unavailable");
            var controller = V1Users();

            Assert.Equal(500, Assert.IsType<StatusCodeResult>(await controller.GetExists("ada")).StatusCode);
            Assert.Equal(500, Assert.IsType<StatusCodeResult>(await controller.IsMemberOf("ada", "team")).StatusCode);
        }

        [Fact]
        public async Task OUs_CreateUpdateReadAndDeleteHaveObservableDirectoryEffects()
        {
            using var scope = new LdapFakeScope();
            var dn = $"OU=Engineering,{SearchBase}";
            var controller = OUs();

            scope.Query.Add(LdapEntries.OrganizationalUnit($"OU=Existing,{SearchBase}"));
            Assert.Equal(new[] { $"OU=Existing,{SearchBase}" }, (await controller.Get()).Value);

            Assert.IsType<OkResult>(await controller.Post(new OUCreateRequest
            {
                DN = dn,
                Name = "Engineering",
                Description = "Created"
            }));
            Assert.Equal(dn, Assert.Single(scope.Query.Added).Dn);
            Assert.Equal("Engineering", (await controller.Get(DN: dn)).Value.Name);
            Assert.IsType<OkResult>(await controller.GetExists(dn));

            Assert.IsType<OkResult>(await controller.Put(dn, new OU
            {
                Name = "Engineering",
                Description = "Updated"
            }));
            Assert.Equal(dn, Assert.Single(scope.Query.Saved).dn);

            Assert.IsType<OkResult>(await controller.Delete(dn));
            Assert.Equal(dn, Assert.Single(scope.Query.Deleted));
        }

        [Fact]
        public async Task OUs_RejectMalformedOutsideProtectedAndMissingEntries()
        {
            using var scope = new LdapFakeScope();
            var controller = OUs();

            Assert.IsType<ConflictResult>((await controller.Get(DN: "invalid")).Result);
            Assert.IsType<ConflictResult>((await controller.Get(DN: "OU=Other,DC=elsewhere,DC=br")).Result);
            Assert.IsType<NotFoundResult>((await controller.Get(DN: $"OU=Ghost,{SearchBase}")).Result);
            Assert.IsType<ConflictResult>(await controller.GetExists("invalid"));
            Assert.IsType<ConflictResult>(await controller.GetExists("OU=Other,DC=elsewhere,DC=br"));
            Assert.IsType<NotFoundResult>(await controller.GetExists($"OU=Ghost,{SearchBase}"));
            Assert.IsType<ConflictResult>(await controller.Post(new OUCreateRequest
            {
                DN = $"OU=Protected,{SearchBase}",
                Name = "Protected"
            }));
            Assert.IsType<ConflictResult>(await controller.Delete("invalid"));
            Assert.IsType<ConflictResult>(await controller.Delete("OU=Other,DC=elsewhere,DC=br"));
            Assert.IsType<ConflictResult>(await controller.Delete($"OU=Protected,{SearchBase}"));
            Assert.IsType<NotFoundResult>(await controller.Delete($"OU=Ghost,{SearchBase}"));
        }

        [Fact]
        public async Task OUs_PutCreatesMissingEntryAndReportsDirectoryWriteFailure()
        {
            using var scope = new LdapFakeScope();
            var controller = OUs();
            var createdDn = $"OU=CreatedByPut,{SearchBase}";

            Assert.IsType<OkResult>(await controller.Put(createdDn, new OU
            {
                Name = "CreatedByPut",
                Description = "Created"
            }));
            Assert.Equal(createdDn, Assert.Single(scope.Query.Added).Dn);

            scope.Query.Throw = new LdapException("backend unavailable");
            var failedDn = $"OU=Failed,{SearchBase}";
            Assert.Equal(500, Assert.IsType<StatusCodeResult>(await controller.Put(failedDn, new OU
            {
                Name = "Failed",
                Description = "Failure"
            })).StatusCode);
        }

        [Fact]
        public async Task OUs_PostAndPutValidateBodiesNamesLocationsAndExistingEntries()
        {
            using var scope = new LdapFakeScope();
            var dn = $"OU=Engineering,{SearchBase}";
            SeedGroup(scope);
            scope.Query.Add(LdapEntries.OrganizationalUnit(dn, "Existing"));
            var controller = OUs();

            Assert.IsType<BadRequestResult>(await controller.Post(null));
            Assert.IsType<BadRequestResult>(await controller.Post(new OUCreateRequest
            {
                Name = "Engineering"
            }));
            Assert.IsType<ConflictResult>(await controller.Post(new OUCreateRequest
            {
                DN = "invalid",
                Name = "Engineering"
            }));
            Assert.IsType<ConflictResult>(await controller.Post(new OUCreateRequest
            {
                DN = dn,
                Name = "Other"
            }));
            Assert.IsType<ConflictResult>(await controller.Post(new OUCreateRequest
            {
                DN = "OU=Engineering,DC=elsewhere,DC=br",
                Name = "Engineering"
            }));
            Assert.IsType<ConflictResult>(await controller.Post(new OUCreateRequest
            {
                DN = dn,
                Name = "Engineering"
            }));

            Assert.IsType<BadRequestResult>(await controller.Put(dn, null));
            Assert.IsType<ConflictResult>(await controller.Put(dn, new OU
            {
                DN = $"OU=Other,{SearchBase}",
                Name = "Engineering"
            }));
            Assert.IsType<ConflictResult>(await controller.Put("invalid", new OU { Name = "Engineering" }));
            Assert.IsType<ConflictResult>(await controller.Put(dn, new OU { Name = "Other" }));
            Assert.IsType<ConflictResult>(await controller.Put(
                "OU=Engineering,DC=elsewhere,DC=br", new OU { Name = "Engineering" }));
            Assert.IsType<ConflictResult>(await controller.Put(
                $"OU=Protected,{SearchBase}", new OU { Name = "Protected" }));
        }

        [Fact]
        public async Task OUs_BackendFailuresAndUnknownDomainsReturnStableHttpResults()
        {
            using var scope = new LdapFakeScope();
            var dn = $"OU=Engineering,{SearchBase}";
            var controller = OUs();

            Assert.IsType<NotFoundResult>((await controller.Get(domain: "missing")).Result);
            Assert.Empty(scope.Query.Calls);

            scope.Query.Throw = new System.InvalidOperationException("backend unavailable");
            Assert.Equal(500, Assert.IsType<StatusCodeResult>((await controller.Get(DN: dn)).Result).StatusCode);
            Assert.Equal(500, Assert.IsType<StatusCodeResult>(await controller.GetExists(dn)).StatusCode);
        }

        [Fact]
        public async Task OUs_CreateFailureIs500AfterLookupReportsMissing()
        {
            using var scope = new LdapFakeScope();
            scope.Query.Throw = new LdapException("backend unavailable");
            var dn = $"OU=Engineering,{SearchBase}";

            var result = await OUs().Post(new OUCreateRequest
            {
                DN = dn,
                Name = "Engineering",
                Description = "Created"
            });

            Assert.Equal(500, Assert.IsType<StatusCodeResult>(result).StatusCode);
        }
    }
}
