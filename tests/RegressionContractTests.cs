using System.Collections.Generic;
using adrapi.Controllers;
using adrapi.Controllers.V2;
using adrapi.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using V1GroupsController = adrapi.Controllers.GroupsController;
using V1UsersController = adrapi.Controllers.UsersController;
using V2GroupsController = adrapi.Controllers.V2.GroupsController;

namespace tests
{
    public class RegressionContractTests
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
            context.Request.Headers["X-Correlation-ID"] = "test-correlation-id";
            context.Request.Headers["X-Forwarded-For"] = "10.10.10.10";
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = context
            };
        }

        [Fact]
        public async System.Threading.Tasks.Task V1Groups_Get_InvalidDn_ReturnsConflict()
        {
            var controller = new V1GroupsController(NullLogger<V1GroupsController>.Instance, BuildConfiguration());
            SetupContext(controller);

            var result = await controller.Get("invalid-dn");

            Assert.IsType<ConflictResult>(result.Result);
        }

        [Fact]
        public async System.Threading.Tasks.Task V1Groups_Put_NullBody_ReturnsBadRequest()
        {
            var controller = new V1GroupsController(NullLogger<V1GroupsController>.Instance, BuildConfiguration());
            SetupContext(controller);

            var result = await controller.Put("CN=test,DC=homologa,DC=br", null, false);

            Assert.IsType<BadRequestResult>(result);
        }

        [Fact]
        public async System.Threading.Tasks.Task V1Users_IsMemberOf_MissingInputs_ReturnsBadRequest()
        {
            var controller = new V1UsersController(NullLogger<V1UsersController>.Instance, BuildConfiguration());
            SetupContext(controller);

            var result = await controller.IsMemberOf(string.Empty, string.Empty);

            Assert.IsType<BadRequestResult>(result);
        }

        [Fact]
        public async System.Threading.Tasks.Task V2Groups_Get_EmptyLocator_ReturnsBadRequest()
        {
            var controller = new V2GroupsController(NullLogger<V2GroupsController>.Instance, BuildConfiguration());
            SetupContext(controller);

            var result = await controller.Get(string.Empty);

            Assert.IsType<BadRequestResult>(result.Result);
        }

        [Fact]
        public async System.Threading.Tasks.Task V2Groups_PutMembers_InvalidDn_ReturnsConflict()
        {
            var controller = new V2GroupsController(NullLogger<V2GroupsController>.Instance, BuildConfiguration());
            SetupContext(controller);

            var result = await controller.PutMembers("invalid-dn", new[] { "CN=user,DC=homologa,DC=br" }, false);

            Assert.IsType<ConflictResult>(result);
        }

        [Fact]
        public async System.Threading.Tasks.Task V2Groups_PatchMembers_EmptyPatch_ReturnsBadRequest()
        {
            var controller = new V2GroupsController(NullLogger<V2GroupsController>.Instance, BuildConfiguration());
            SetupContext(controller);

            var result = await controller.PatchMembers("CN=grp,DC=homologa,DC=br", new GroupMembersPatchRequest(), false);

            Assert.IsType<BadRequestResult>(result);
        }

        [Fact]
        public async System.Threading.Tasks.Task OUs_Delete_ProtectedOu_ReturnsConflict()
        {
            var controller = new OUsController(NullLogger<V1GroupsController>.Instance, BuildConfiguration());
            SetupContext(controller);

            var result = await controller.Delete("OU=Protected,DC=homologa,DC=br");

            Assert.IsType<ConflictResult>(result);
        }

        [Fact]
        public async System.Threading.Tasks.Task OUs_Put_DnOutsideSearchBase_ReturnsConflict()
        {
            var controller = new OUsController(NullLogger<V1GroupsController>.Instance, BuildConfiguration());
            SetupContext(controller);

            var result = await controller.Put("OU=Infra,DC=other,DC=br", new adrapi.domain.OU
            {
                Name = "Infra"
            });

            Assert.IsType<ConflictResult>(result);
        }
    }
}
