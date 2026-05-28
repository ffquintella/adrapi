using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using adrapi;
using adrapi.Controllers.V2;
using adrapi.domain.Exceptions;
using adrapi.Ldap;
using adrapi.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace tests
{
    public class NegativePathTests
    {
        private static IConfiguration BuildConfiguration()
        {
            var values = new Dictionary<string, string>
            {
                ["ldap:searchBase"] = "DC=homologa,DC=br",
                ["ldap:servers:0"] = "127.0.0.1:1",
                ["ldap:bindDn"] = "CN=fake,DC=homologa,DC=br",
                ["ldap:bindCredentials"] = "fake"
            };

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(values)
                .Build();
            adrapi.ConfigurationManager.Instance.Config = config;
            return config;
        }

        private static void SetupContext(ControllerBase controller)
        {
            var context = new DefaultHttpContext();
            context.Request.Headers["api-key"] = "dev-local:secret";
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = context
            };
        }

        [Fact]
        public async Task V2Groups_Post_NullPayload_ReturnsBadRequest()
        {
            var controller = new GroupsController(NullLogger<GroupsController>.Instance, BuildConfiguration());
            SetupContext(controller);

            var result = await controller.Post(null, true);

            Assert.IsType<BadRequestResult>(result);
        }

        [Fact]
        public async Task V2Groups_Post_WhenLdapUnavailable_ThrowsWrongParameter()
        {
            var controller = new GroupsController(NullLogger<GroupsController>.Instance, BuildConfiguration());
            SetupContext(controller);

            var ex = await Assert.ThrowsAsync<WrongParameterException>(async () =>
                await controller.Post(new GroupCreateRequest
                {
                    DN = "CN=itgroup,DC=homologa,DC=br",
                    Name = "itgroup",
                    Members = new List<string> { "unknown-user" }
                }, true));

            Assert.Contains("LDAP", ex.Message);
        }

        [Fact]
        public async Task InaccessibleLdap_GetConnection_ThrowsWrongParameter()
        {
            var config = new LdapConfig(
                new[] { "127.0.0.1:1" },
                false,
                1000,
                1,
                "CN=fake,DC=homologa,DC=br",
                "fake",
                "DC=homologa,DC=br",
                "(&(objectClass=user)(sAMAccountName={0}))",
                "CN=admins,DC=homologa,DC=br");

            var ex = await Assert.ThrowsAsync<WrongParameterException>(async () =>
                await LdapConnectionManager.Instance.GetConnectionAsync(config));

            Assert.Contains("Failed to connect to LDAP", ex.Message);
        }
    }
}
