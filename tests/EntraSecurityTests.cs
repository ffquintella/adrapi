using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Xunit;
using adrapi.Directory;
using adrapi.Entra;
using adrapi.domain.Exceptions;

namespace tests
{
    /// <summary>
    /// Stage 7 (security, secrets, tenant config) unit tests: Graph error → HTTP
    /// mapping, tenant-mode validation, and OData input hardening.
    /// </summary>
    public class EntraSecurityTests
    {
        // ---- Graph error → clear 4xx/5xx mapping ----

        [Theory]
        [InlineData(400, 400)]
        [InlineData(409, 409)]
        [InlineData(422, 422)]
        [InlineData(404, 404)]
        [InlineData(429, 503)] // throttled (retries exhausted)
        [InlineData(401, 502)] // our token rejected -> upstream/config fault
        [InlineData(403, 502)] // insufficient app permissions -> config fault
        [InlineData(500, 502)] // upstream Graph fault
        [InlineData(503, 502)]
        public void MapsGraphStatusToClientStatus(int graphStatus, int expected)
        {
            var result = DirectoryErrorMapper.ToProblem(
                new GraphException("boom", (HttpStatusCode)graphStatus, requestId: "req-9"));

            Assert.Equal(expected, result.StatusCode);
            var problem = Assert.IsType<ProblemDetails>(result.Value);
            Assert.Equal(graphStatus, problem.Extensions["graphStatus"]);
            Assert.Equal("req-9", problem.Extensions["graphRequestId"]); // surfaced for support correlation
        }

        [Fact]
        public void TransportFailure_MapsTo503()
        {
            var result = DirectoryErrorMapper.ToProblem(new GraphException("no route", statusCode: 0));
            Assert.Equal(503, result.StatusCode);
            var problem = Assert.IsType<ProblemDetails>(result.Value);
            Assert.False(problem.Extensions.ContainsKey("graphStatus")); // status 0 is not echoed
        }

        [Fact]
        public void ServerSideGraphErrors_DoNotLeakUpstreamMessage()
        {
            var result = DirectoryErrorMapper.ToProblem(
                new GraphException("secret upstream detail", HttpStatusCode.InternalServerError));
            var problem = Assert.IsType<ProblemDetails>(result.Value);
            Assert.DoesNotContain("secret upstream detail", problem.Detail);
        }

        [Theory]
        [InlineData(typeof(WrongParameterException), 400)]
        [InlineData(typeof(NotSupportedException), 400)]
        [InlineData(typeof(InvalidCredentialsException), 502)]
        public void MapsProviderExceptions(Type exType, int expected)
        {
            var ex = (Exception)Activator.CreateInstance(exType, "msg");
            Assert.Equal(expected, DirectoryErrorMapper.ToProblem(ex).StatusCode);
        }

        [Fact]
        public void UnknownException_MapsTo500()
        {
            Assert.Equal(500, DirectoryErrorMapper.ToProblem(new InvalidOperationException("x")).StatusCode);
        }

        // ---- Tenant configuration (single vs multi-tenant) ----

        private static EntraConfig ConfigWithTenant(string tenant) => new()
        {
            DomainKey = "cloud",
            TenantId = tenant,
            ClientId = "c",
            ClientSecret = "s",
        };

        [Theory]
        [InlineData("common")]
        [InlineData("organizations")]
        [InlineData("CONSUMERS")]
        public void Validate_RejectsMultiTenantPlaceholderTenant(string tenant)
        {
            Assert.Contains(ConfigWithTenant(tenant).Validate(), e => e.Contains("client-credentials"));
        }

        [Theory]
        [InlineData("11111111-1111-1111-1111-111111111111")]
        [InlineData("contoso.onmicrosoft.com")]
        public void Validate_AcceptsConcreteTenant(string tenant)
        {
            Assert.Empty(ConfigWithTenant(tenant).Validate());
        }

        // ---- OData input hardening ----

        [Fact]
        public void EscapeODataLiteral_DoublesSingleQuotes()
        {
            Assert.Equal("O''Brien", GraphQuery.EscapeODataLiteral("O'Brien"));
            Assert.Equal("'') or 1 eq ''1", GraphQuery.EscapeODataLiteral("') or 1 eq '1"));
            Assert.Equal("", GraphQuery.EscapeODataLiteral(null));
        }
    }
}
