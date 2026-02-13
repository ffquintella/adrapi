using adrapi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace tests
{
    public class BaseControllerAuditTests
    {
        private sealed class ProbeController : BaseController
        {
            public ProbeController()
            {
                logger = NullLogger.Instance;
            }

            public void RunProcessRequest()
            {
                ProcessRequest();
            }

            public string CorrelationId()
            {
                return GetCorrelationId();
            }

            public string ClientIp()
            {
                return GetClientIp();
            }

            public string Requester()
            {
                return requesterID;
            }
        }

        [Fact]
        public void ProcessRequest_WithoutApiKey_SetsUnknownRequester()
        {
            var controller = new ProbeController();
            var context = new DefaultHttpContext();
            controller.ControllerContext = new ControllerContext { HttpContext = context };

            controller.RunProcessRequest();

            Assert.Equal("unknown", controller.Requester());
        }

        [Fact]
        public void CorrelationId_UsesHeader_WhenProvided()
        {
            var controller = new ProbeController();
            var context = new DefaultHttpContext();
            context.Request.Headers["X-Correlation-ID"] = "corr-123";
            controller.ControllerContext = new ControllerContext { HttpContext = context };

            var value = controller.CorrelationId();

            Assert.Equal("corr-123", value);
        }

        [Fact]
        public void ClientIp_PrefersXForwardedFor()
        {
            var controller = new ProbeController();
            var context = new DefaultHttpContext();
            context.Request.Headers["X-Forwarded-For"] = "10.0.0.1, 10.0.0.2";
            controller.ControllerContext = new ControllerContext { HttpContext = context };

            var value = controller.ClientIp();

            Assert.Equal("10.0.0.1", value);
        }
    }
}
