using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace tests.Authentication
{
    /// <summary>
    /// Authentication and authorization gating for every controller action.
    ///
    /// Each test in here is driven by <see cref="EndpointCatalog"/>. The contract
    /// these tests enforce is:
    ///
    ///   1. No `api-key` header                          → 401 Unauthorized
    ///   2. Header with unknown keyID                    → 401
    ///   3. Header with known keyID but wrong secret     → 401
    ///   4. Header with known key but unauthorized IP    → 401
    ///   5. Writting-policy endpoint hit with isMonitor  → 403 Forbidden
    ///   6. Endpoint hit with the matching policy claim  → NOT 401 / NOT 403
    ///      (200/400/404/500 are all acceptable — auth passed, anything past
    ///       that is a business-logic concern, not an auth concern.)
    ///
    /// IMPORTANT: when you add or remove an endpoint, update
    /// <see cref="EndpointCatalog.All"/>. AGENTS.md describes this contract.
    /// </summary>
    public class EndpointAuthenticationTests : IClassFixture<AuthEndpointFixture>
    {
        private readonly AuthEndpointFixture _fixture;

        public EndpointAuthenticationTests(AuthEndpointFixture fixture)
        {
            _fixture = fixture;
        }

        // ====================================================================
        // 1) No api-key → 401 on every endpoint
        // ====================================================================
        [Theory]
        [MemberData(nameof(AllEndpoints))]
        public async Task NoApiKey_returns401(string method, string path, string policy, string apiVersion, bool requiresBody)
        {
            var client = _fixture.CreateClient();
            var req = BuildRequest(method, path, apiVersion, requiresBody);
            // intentionally no api-key header
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        // ====================================================================
        // 2) Unknown keyID → 401
        // ====================================================================
        [Theory]
        [MemberData(nameof(AllEndpoints))]
        public async Task UnknownKeyId_returns401(string method, string path, string policy, string apiVersion, bool requiresBody)
        {
            var client = _fixture.CreateClient();
            var req = BuildRequest(method, path, apiVersion, requiresBody);
            req.Headers.Add("api-key", "does-not-exist:any-secret");
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        // ====================================================================
        // 3) Known keyID, wrong secret → 401
        // ====================================================================
        [Theory]
        [MemberData(nameof(AllEndpoints))]
        public async Task WrongSecret_returns401(string method, string path, string policy, string apiVersion, bool requiresBody)
        {
            var client = _fixture.CreateClient();
            var req = BuildRequest(method, path, apiVersion, requiresBody);
            req.Headers.Add("api-key", $"{AuthEndpointFixture.AdminKeyId}:wrong-secret-value");
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        // ====================================================================
        // 4) Valid key but request comes from an IP outside `authorizedIP` → 401
        //    (the wrong-ip-test fixture key only allows 192.0.2.99, never matches
        //     TestServer's null/loopback)
        // ====================================================================
        [Theory]
        [MemberData(nameof(AllEndpoints))]
        public async Task UnauthorizedSourceIp_returns401(string method, string path, string policy, string apiVersion, bool requiresBody)
        {
            var client = _fixture.CreateClient();
            var req = BuildRequest(method, path, apiVersion, requiresBody);
            req.Headers.Add("api-key", $"{AuthEndpointFixture.WrongIpKeyId}:{AuthEndpointFixture.WrongIpSecret}");
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        // ====================================================================
        // 5) isMonitor claim against a Writting-policy endpoint → 403
        // ====================================================================
        [Theory]
        [MemberData(nameof(WriteEndpoints))]
        public async Task MonitorOnWriteEndpoint_returns403(string method, string path, string policy, string apiVersion, bool requiresBody)
        {
            var client = _fixture.CreateClient();
            var req = BuildRequest(method, path, apiVersion, requiresBody);
            req.Headers.Add("api-key", $"{AuthEndpointFixture.MonitorKeyId}:{AuthEndpointFixture.MonitorSecret}");
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }

        // ====================================================================
        // 6) Admin key passes auth+authz on every endpoint
        //    (status code is anything except 401/403 — the business layer may
        //     still 400/404/500 because LDAP isn't reachable, that's fine)
        // ====================================================================
        [Theory]
        [MemberData(nameof(AllEndpoints))]
        public async Task AdminKey_passesAuth(string method, string path, string policy, string apiVersion, bool requiresBody)
        {
            var client = _fixture.CreateClient();
            var req = BuildRequest(method, path, apiVersion, requiresBody);
            req.Headers.Add("api-key", $"{AuthEndpointFixture.AdminKeyId}:{AuthEndpointFixture.AdminSecret}");
            var resp = await client.SendAsync(req);
            Assert.NotEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
            Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
        }

        // ====================================================================
        // Theory data feeds
        // ====================================================================
        public static System.Collections.Generic.IEnumerable<object[]> AllEndpoints()
            => EndpointCatalog.AllAsTheoryData();

        public static System.Collections.Generic.IEnumerable<object[]> WriteEndpoints()
            => EndpointCatalog.WriteEndpointsAsTheoryData();

        // ====================================================================
        private static HttpRequestMessage BuildRequest(string method, string path, string apiVersion, bool requiresBody)
        {
            var req = new HttpRequestMessage(new HttpMethod(method), path);
            req.Headers.Add("api-version", apiVersion);
            if (requiresBody)
            {
                // Minimal but valid JSON. The controller may still 400 on
                // missing required fields — that's fine; we're only asserting
                // the auth filter ran (or didn't) before model binding.
                req.Content = new StringContent(
                    "{\"Login\":\"x\",\"Password\":\"y\"}",
                    Encoding.UTF8, "application/json");
            }
            return req;
        }
    }
}
