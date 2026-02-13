using System.Collections.Generic;
using adrapi.Controllers;
using adrapi.Controllers.V2;
using adrapi.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using V2GroupsController = adrapi.Controllers.V2.GroupsController;
using V1GroupsController = adrapi.Controllers.GroupsController;
using V1UsersController = adrapi.Controllers.UsersController;

namespace tests
{
    public class ControllerValidationTests
    {
        private static IConfiguration BuildConfiguration()
        {
            var values = new Dictionary<string, string>
            {
                ["ldap:searchBase"] = "DC=homologa,DC=br",
                ["ldap:protectedOUs:0"] = "OU=Protected,DC=homologa,DC=br"
            };

            return new ConfigurationBuilder()
                .AddInMemoryCollection(values)
                .Build();
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
        public async System.Threading.Tasks.Task V2Groups_Put_InvalidDn_ReturnsConflict()
        {
            var controller = new V2GroupsController(NullLogger<V2GroupsController>.Instance, BuildConfiguration());
            SetupContext(controller);

            var result = await controller.Put("invalid-dn", new adrapi.domain.Group
            {
                Name = "group",
                Member = new List<string>()
            });

            Assert.IsType<ConflictResult>(result);
        }

        [Fact]
        public async System.Threading.Tasks.Task V2Groups_GetExists_InvalidDn_ReturnsConflict()
        {
            var controller = new V2GroupsController(NullLogger<V2GroupsController>.Instance, BuildConfiguration());
            SetupContext(controller);

            var result = await controller.GetExists("invalid-dn");

            Assert.IsType<ConflictResult>(result);
        }

        [Fact]
        public async System.Threading.Tasks.Task OUs_Post_NameDnMismatch_ReturnsConflict()
        {
            var controller = new OUsController(NullLogger<V1GroupsController>.Instance, BuildConfiguration());
            SetupContext(controller);

            var result = await controller.Post(new OUCreateRequest
            {
                DN = "OU=Infra,DC=homologa,DC=br",
                Name = "DifferentName"
            });

            Assert.IsType<ConflictResult>(result);
        }

        [Fact]
        public async System.Threading.Tasks.Task OUs_Get_InvalidDn_ReturnsConflict()
        {
            var controller = new OUsController(NullLogger<V1GroupsController>.Instance, BuildConfiguration());
            SetupContext(controller);

            var result = await controller.Get("invalid-dn");

            Assert.IsType<ConflictResult>(result.Result);
        }

        [Fact]
        public async System.Threading.Tasks.Task Users_AuthenticateDirect_NullBody_ReturnsBadRequest()
        {
            var controller = new V1UsersController(NullLogger<V1UsersController>.Instance, BuildConfiguration());
            SetupContext(controller);

            var result = await controller.AuthenticateDirect(null);

            Assert.IsType<BadRequestResult>(result);
        }
    }
}
