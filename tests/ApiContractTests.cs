using System.Linq;
using System.ComponentModel.DataAnnotations;
using adrapi.Controllers.V2;
using adrapi.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace tests
{
    public class ApiContractTests
    {
        [Fact]
        public void GroupCreateRequest_HasRequiredContractFields()
        {
            var dn = typeof(GroupCreateRequest).GetProperty(nameof(GroupCreateRequest.DN));
            var name = typeof(GroupCreateRequest).GetProperty(nameof(GroupCreateRequest.Name));

            Assert.NotNull(dn.GetCustomAttributes(typeof(RequiredAttribute), true).FirstOrDefault());
            Assert.NotNull(name.GetCustomAttributes(typeof(RequiredAttribute), true).FirstOrDefault());
        }

        [Fact]
        public void OUCreateRequest_HasRequiredContractFields()
        {
            var dn = typeof(OUCreateRequest).GetProperty(nameof(OUCreateRequest.DN));
            var name = typeof(OUCreateRequest).GetProperty(nameof(OUCreateRequest.Name));

            Assert.NotNull(dn.GetCustomAttributes(typeof(RequiredAttribute), true).FirstOrDefault());
            Assert.NotNull(name.GetCustomAttributes(typeof(RequiredAttribute), true).FirstOrDefault());
        }

        [Fact]
        public void GroupMembersPatchRequest_DefaultCollections_AreNotNull()
        {
            var request = new GroupMembersPatchRequest();
            Assert.NotNull(request.Add);
            Assert.NotNull(request.Remove);
            Assert.Empty(request.Add);
            Assert.Empty(request.Remove);
        }

        [Fact]
        public void V2GroupsController_DeclaresExpectedMemberRoutes()
        {
            var methods = typeof(GroupsController).GetMethods();

            var getMembers = methods.First(m => m.Name == nameof(GroupsController.GetMembers));
            var putMembers = methods.First(m => m.Name == nameof(GroupsController.PutMembers));
            var patchMembers = methods.First(m => m.Name == nameof(GroupsController.PatchMembers));

            var getRoute = (HttpGetAttribute)getMembers.GetCustomAttributes(typeof(HttpGetAttribute), true).First();
            var putRoute = (HttpPutAttribute)putMembers.GetCustomAttributes(typeof(HttpPutAttribute), true).First();
            var patchRoute = (HttpPatchAttribute)patchMembers.GetCustomAttributes(typeof(HttpPatchAttribute), true).First();

            Assert.Equal("{groupId}/members", getRoute.Template);
            Assert.Equal("{DN}/members", putRoute.Template);
            Assert.Equal("{DN}/members", patchRoute.Template);
        }
    }
}
