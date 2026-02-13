using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using adrapi;
using adrapi.Controllers;
using adrapi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Novell.Directory.Ldap;
using Xunit;
using V1GroupsController = adrapi.Controllers.GroupsController;
using V1UsersController = adrapi.Controllers.UsersController;
using V2GroupsController = adrapi.Controllers.V2.GroupsController;
using V2UsersController = adrapi.Controllers.V2.UsersController;

namespace tests
{
    public class LdapIntegrationTests
    {
        private static bool IntegrationEnabled =>
            string.Equals(Environment.GetEnvironmentVariable("ADRAPI_RUN_LDAP_INTEGRATION"), "1", StringComparison.OrdinalIgnoreCase);

        private static IConfiguration BuildConfiguration()
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings-tests.json", optional: false)
                .Build();
            adrapi.ConfigurationManager.Instance.Config = config;
            return config;
        }

        private static void SetupContext(ControllerBase controller)
        {
            var context = new DefaultHttpContext();
            context.Request.Headers["api-key"] = "dev-local:abc1234";
            context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = context
            };
        }

        private static async Task<bool> CanConnectAsync(IConfiguration config)
        {
            var server = config.GetSection("ldap:servers").Get<string[]>()?.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(server))
            {
                return false;
            }

            var parts = server.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
            {
                return false;
            }

            var bindDn = config["ldap:bindDn"];
            var bindCredentials = config["ldap:bindCredentials"];

            try
            {
                using var conn = new LdapConnection();
                await conn.ConnectAsync(parts[0], port);
                await conn.BindAsync(bindDn, bindCredentials);
                return conn.Bound;
            }
            catch
            {
                return false;
            }
        }

        [Fact]
        public async Task Integration_GroupLifecycleAndMembership_Works()
        {
            if (!IntegrationEnabled)
            {
                return;
            }

            var config = BuildConfiguration();
            Assert.True(await CanConnectAsync(config), "LDAP integration is enabled but connection/bind failed.");

            var users = await UserManager.Instance.GetListAsync();
            var account = users.UserNames?.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(account))
            {
                return;
            }

            var baseDn = config["ldap:searchBase"];
            var groupName = $"it-{Guid.NewGuid():N}".Substring(0, 10);
            var groupDn = $"CN={groupName},{baseDn}";

            var groupsController = new V2GroupsController(NullLogger<V2GroupsController>.Instance, config);
            SetupContext(groupsController);

            try
            {
                var create = await groupsController.Post(new GroupCreateRequest
                {
                    DN = groupDn,
                    Name = groupName,
                    Members = new List<string>()
                });
                Assert.IsType<OkResult>(create);

                var exists = await groupsController.GetExists(groupDn);
                Assert.IsType<OkResult>(exists);

                var patchAdd = await groupsController.PatchMembers(groupDn, new GroupMembersPatchRequest
                {
                    Add = new List<string> { account }
                }, true);
                Assert.IsType<OkResult>(patchAdd);

                var membersAfterAdd = await groupsController.GetMembers(groupDn, false);
                Assert.NotEmpty(membersAfterAdd.Value);

                var replace = await groupsController.PutMembers(groupDn, new[] { account }, true);
                Assert.IsType<OkResult>(replace);

                var patchRemove = await groupsController.PatchMembers(groupDn, new GroupMembersPatchRequest
                {
                    Remove = new List<string> { account }
                }, true);
                Assert.IsType<OkResult>(patchRemove);
            }
            finally
            {
                await groupsController.Delete(groupDn);
            }
        }

        [Fact]
        public async Task Integration_GroupUnknownMember_Returns422()
        {
            if (!IntegrationEnabled)
            {
                return;
            }

            var config = BuildConfiguration();
            Assert.True(await CanConnectAsync(config), "LDAP integration is enabled but connection/bind failed.");

            var baseDn = config["ldap:searchBase"];
            var groupName = $"it-{Guid.NewGuid():N}".Substring(0, 10);
            var groupDn = $"CN={groupName},{baseDn}";

            var groupsController = new V2GroupsController(NullLogger<V2GroupsController>.Instance, config);
            SetupContext(groupsController);

            try
            {
                var create = await groupsController.Post(new GroupCreateRequest
                {
                    DN = groupDn,
                    Name = groupName,
                    Members = new List<string>()
                });
                Assert.IsType<OkResult>(create);

                var patchAddUnknown = await groupsController.PatchMembers(groupDn, new GroupMembersPatchRequest
                {
                    Add = new List<string> { $"unknown-{Guid.NewGuid():N}" }
                }, true);

                var status = Assert.IsType<StatusCodeResult>(patchAddUnknown);
                Assert.Equal(422, status.StatusCode);
            }
            finally
            {
                await groupsController.Delete(groupDn);
            }
        }

        [Fact]
        public async Task Integration_OuLifecycle_Works()
        {
            if (!IntegrationEnabled)
            {
                return;
            }

            var config = BuildConfiguration();
            Assert.True(await CanConnectAsync(config), "LDAP integration is enabled but connection/bind failed.");

            var baseDn = config["ldap:searchBase"];
            var ouName = $"it-{Guid.NewGuid():N}".Substring(0, 10);
            var ouDn = $"OU={ouName},{baseDn}";

            var controller = new OUsController(NullLogger<V1GroupsController>.Instance, config);
            SetupContext(controller);

            try
            {
                var create = await controller.Post(new OUCreateRequest
                {
                    DN = ouDn,
                    Name = ouName,
                    Description = "integration"
                });
                Assert.IsType<OkResult>(create);

                var exists = await controller.GetExists(ouDn);
                Assert.IsType<OkResult>(exists);

                var get = await controller.Get(ouDn);
                Assert.NotNull(get.Value);

                var update = await controller.Put(ouDn, new adrapi.domain.OU
                {
                    DN = ouDn,
                    Name = ouName,
                    Description = "integration-updated"
                });
                Assert.IsType<OkResult>(update);

                var list = await controller.Get();
                Assert.Contains(ouDn, list.Value);
            }
            finally
            {
                await controller.Delete(ouDn);
            }
        }

        [Fact]
        public void Integration_AuthPolicies_AreDeclaredForReadWrite()
        {
            // 7.2 auth behavior contract check: read endpoints inherit class-level Reading;
            // write endpoints declare Writting.
            var typePolicies = new[]
            {
                typeof(V2GroupsController),
                typeof(V1GroupsController),
                typeof(OUsController),
                typeof(V1UsersController),
                typeof(V2UsersController)
            };

            foreach (var controller in typePolicies)
            {
                var classAuthorize = controller.GetCustomAttributes(typeof(AuthorizeAttribute), true)
                    .Cast<AuthorizeAttribute>()
                    .FirstOrDefault();
                Assert.NotNull(classAuthorize);
                Assert.Equal("Reading", classAuthorize.Policy);
            }

            var writeMethods = new (Type Type, string Method)[]
            {
                (typeof(V2GroupsController), nameof(V2GroupsController.Post)),
                (typeof(V2GroupsController), nameof(V2GroupsController.Put)),
                (typeof(V2GroupsController), nameof(V2GroupsController.PatchMembers)),
                (typeof(V2GroupsController), nameof(V2GroupsController.PutMembers)),
                (typeof(V2GroupsController), nameof(V2GroupsController.Delete)),
                (typeof(V1GroupsController), nameof(V1GroupsController.Put)),
                (typeof(V1GroupsController), nameof(V1GroupsController.PutMembers)),
                (typeof(V1GroupsController), nameof(V1GroupsController.Delete)),
                (typeof(OUsController), nameof(OUsController.Post)),
                (typeof(OUsController), nameof(OUsController.Put)),
                (typeof(OUsController), nameof(OUsController.Delete))
            };

            foreach (var (type, methodName) in writeMethods)
            {
                var method = type.GetMethods().FirstOrDefault(m => m.Name == methodName);
                Assert.NotNull(method);
                var authorize = method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
                    .Cast<AuthorizeAttribute>()
                    .FirstOrDefault();
                Assert.NotNull(authorize);
                Assert.Equal("Writting", authorize.Policy);
            }
        }
    }
}
