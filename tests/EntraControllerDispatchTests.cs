using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using adrapi.Controllers.V2;
using adrapi.Directory;
using adrapi.Models;
using adrapi.domain;
using adrapi.Ldap;
using adrapi.domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace tests
{
    /// <summary>
    /// Verifies the v2 controllers dispatch to the directory provider for an
    /// Entra ID-backed domain (and map provider errors), without a live tenant —
    /// a fake provider is injected via the BaseController.ResolveProvider seam.
    /// </summary>
    public class EntraControllerDispatchTests
    {
        private sealed class FakeProvider : IDirectoryProvider
        {
            public List<User> Users = new();
            public List<Group> Groups = new();
            public readonly List<string> Calls = new();
            public Exception Throw;

            public string DomainKey => "cloud";
            public DirectoryBackend Backend => DirectoryBackend.EntraId;
            public bool SupportsOrganizationalUnits => false;

            private Task<T> Do<T>(string call, T result)
            {
                Calls.Add(call);
                if (Throw != null) throw Throw;
                return Task.FromResult(result);
            }

            public Task<List<User>> GetUsersAsync(CancellationToken ct = default) => Do("GetUsers", Users);
            public Task<User> GetUserAsync(string id, CancellationToken ct = default) => Do("GetUser:" + id, Users.FirstOrDefault(u => u.Login == id || u.ID == id));
            public Task<bool> UserExistsAsync(string id, CancellationToken ct = default) => Do("UserExists:" + id, Users.Any(u => u.Login == id || u.ID == id));
            public Task<List<User>> SearchUsersAsync(string q, CancellationToken ct = default) => Do("SearchUsers:" + q, Users);
            public Task<bool> CreateUserAsync(User u, CancellationToken ct = default) { u.ID ??= "new-user"; return Do("CreateUser:" + u.Login, true); }
            public Task<bool> UpdateUserAsync(User u, CancellationToken ct = default) => Do("UpdateUser:" + u.ID, true);
            public Task<bool> DeleteUserAsync(User u, CancellationToken ct = default) => Do("DeleteUser:" + u.ID, true);
            public Task<bool> SetUserEnabledAsync(string id, bool e, CancellationToken ct = default) => Do("SetEnabled", true);
            public Task<bool> SetUserPasswordAsync(string id, string p, bool f = true, CancellationToken ct = default) => Do("SetPwd", true);

            public Task<List<Group>> GetGroupsAsync(CancellationToken ct = default) => Do("GetGroups", Groups);
            public Task<Group> GetGroupAsync(string id, CancellationToken ct = default) => Do("GetGroup:" + id, Groups.FirstOrDefault(g => g.Name == id || g.ID == id));
            public Task<bool> GroupExistsAsync(string id, CancellationToken ct = default) => Do("GroupExists:" + id, Groups.Any(g => g.Name == id || g.ID == id));
            public Task<bool> CreateGroupAsync(Group g, CancellationToken ct = default) { g.ID ??= "new-group"; return Do("CreateGroup:" + g.Name, true); }
            public Task<bool> UpdateGroupAsync(Group g, CancellationToken ct = default) => Do("UpdateGroup:" + g.ID, true);
            public Task<bool> DeleteGroupAsync(Group g, CancellationToken ct = default) => Do("DeleteGroup:" + g.ID, true);
            public Task<List<string>> GetGroupMembersAsync(string id, CancellationToken ct = default) => Do("GetMembers:" + id, new List<string> { "ada@contoso.com" });
            public Task<bool> AddGroupMembersAsync(string id, IEnumerable<string> m, CancellationToken ct = default) => Do("AddMembers:" + id, true);
            public Task<bool> RemoveGroupMembersAsync(string id, IEnumerable<string> m, CancellationToken ct = default) => Do("RemoveMembers:" + id, true);
            public Task<bool> ReplaceGroupMembersAsync(string id, IEnumerable<string> m, CancellationToken ct = default) => Do("ReplaceMembers:" + id, true);

            public Task<List<OU>> GetOrganizationalUnitsAsync(CancellationToken ct = default) => throw new NotSupportedException();
            public Task<OU> GetOrganizationalUnitAsync(string dn, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<bool> CreateOrganizationalUnitAsync(OU ou, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<bool> UpdateOrganizationalUnitAsync(OU ou, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<bool> DeleteOrganizationalUnitAsync(OU ou, CancellationToken ct = default) => throw new NotSupportedException();
        }

        private sealed class TestUsersController : UsersController
        {
            private readonly IDirectoryProvider provider;
            public TestUsersController(IDirectoryProvider p) : base(NullLogger<adrapi.Controllers.UsersController>.Instance, new ConfigurationBuilder().Build()) => provider = p;
            protected override IDirectoryProvider ResolveProvider(string domain) => provider;
        }

        private sealed class TestGroupsController : GroupsController
        {
            private readonly IDirectoryProvider provider;
            public TestGroupsController(IDirectoryProvider p) : base(NullLogger<GroupsController>.Instance, new ConfigurationBuilder().Build()) => provider = p;
            protected override IDirectoryProvider ResolveProvider(string domain) => provider;
        }

        private static void Wire(ControllerBase c)
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Headers["api-key"] = "dev-local:secret";
            c.ControllerContext = new ControllerContext { HttpContext = ctx };
        }

        private static void EntraConfigured()
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
        }

        [Fact]
        public async Task Users_Get_DispatchesToProvider()
        {
            EntraConfigured();
            var fake = new FakeProvider { Users = { new User { Login = "ada@contoso.com", ID = "u1" } } };
            var c = new TestUsersController(fake); Wire(c);

            var result = await c.Get(user: "ada@contoso.com", _attribute: "", domain: "cloud");

            var user = Assert.IsType<OkObjectResult>(result.Result).Value as User;
            Assert.Equal("u1", user.ID);
            Assert.Contains("GetUser:ada@contoso.com", fake.Calls);
        }

        [Fact]
        public async Task Users_Get_NotFound_Returns404()
        {
            EntraConfigured();
            var c = new TestUsersController(new FakeProvider()); Wire(c);

            var result = await c.Get(user: "ghost@contoso.com", _attribute: "", domain: "cloud");
            Assert.IsType<NotFoundResult>(result.Result);
        }

        [Fact]
        public async Task Users_Put_CreatesViaProvider()
        {
            EntraConfigured();
            var fake = new FakeProvider();
            var c = new TestUsersController(fake); Wire(c);

            var result = await c.Put("ada@contoso.com", new User { Name = "Ada", Account = "ada" }, domain: "cloud");

            Assert.IsType<OkResult>(result);
            Assert.Contains(fake.Calls, x => x.StartsWith("CreateUser:"));
        }

        [Fact]
        public async Task Users_GraphError_MapsToProblemStatus()
        {
            EntraConfigured();
            var fake = new FakeProvider { Throw = new GraphException("throttled", System.Net.HttpStatusCode.TooManyRequests) };
            var c = new TestUsersController(fake); Wire(c);

            var result = await c.Get(user: "ada@contoso.com", _attribute: "", domain: "cloud");
            var obj = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(503, obj.StatusCode); // 429 -> 503
        }

        [Fact]
        public async Task Groups_Post_CreatesViaProvider_NoDnRequired()
        {
            EntraConfigured();
            var fake = new FakeProvider();
            var c = new TestGroupsController(fake); Wire(c);

            var result = await c.Post(new GroupCreateRequest { Name = "Engineering", GroupType = "Security" }, _listCN: false, domain: "cloud");

            Assert.IsType<OkResult>(result);
            Assert.Contains("CreateGroup:Engineering", fake.Calls);
        }

        [Fact]
        public async Task Groups_PatchMembers_DispatchesDelta()
        {
            EntraConfigured();
            var fake = new FakeProvider();
            var c = new TestGroupsController(fake); Wire(c);

            var result = await c.PatchMembers("Engineering",
                new GroupMembersPatchRequest { Add = new() { "ada@contoso.com" }, Remove = new() { "grace@contoso.com" } },
                _listCN: false, domain: "cloud");

            Assert.IsType<OkResult>(result);
            Assert.Contains("AddMembers:Engineering", fake.Calls);
            Assert.Contains("RemoveMembers:Engineering", fake.Calls);
        }

        [Fact]
        public async Task Groups_GetMembers_DispatchesToProvider()
        {
            EntraConfigured();
            var fake = new FakeProvider();
            var c = new TestGroupsController(fake); Wire(c);

            var result = await c.GetMembers("Engineering", _listCN: true, domain: "cloud");
            var members = Assert.IsType<OkObjectResult>(result.Result).Value as List<string>;
            Assert.Contains("ada@contoso.com", members);
        }
    }
}
