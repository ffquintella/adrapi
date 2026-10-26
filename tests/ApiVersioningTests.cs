using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using tests.Authentication;
using Xunit;

namespace tests
{
    public class ApiVersioningTests : IClassFixture<AuthEndpointFixture>
    {
        private readonly AuthEndpointFixture _fixture;

        public ApiVersioningTests(AuthEndpointFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task ExplicitV1Header_RoutesDeprecatedVersion()
        {
            using var request = CreateAuthorizedRequest(
                HttpMethod.Get,
                "/api/users?_full=false&_start=0&_end=0",
                "1.0");

            using var response = await _fixture.CreateClient().SendAsync(
                request,
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                "[]",
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Contains(
                response.Headers.GetValues("api-deprecated-versions"),
                value => value.Contains("1.0"));
        }

        [Fact]
        public async Task ExplicitV2Header_RoutesCurrentVersion()
        {
            using var request = CreateAuthorizedJsonRequest(HttpMethod.Post, "/api/lab/Groups", "2.0");

            using var response = await _fixture.CreateClient().SendAsync(
                request,
                TestContext.Current.CancellationToken);
            var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Name", content);
        }

        [Fact]
        public async Task MissingVersionHeader_UsesV2Default()
        {
            using var request = CreateAuthorizedJsonRequest(HttpMethod.Post, "/api/lab/Groups");

            using var response = await _fixture.CreateClient().SendAsync(
                request,
                TestContext.Current.CancellationToken);
            var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Name", content);
        }

        [Fact]
        public async Task UnsupportedVersionHeader_ReturnsVersioningError()
        {
            using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/Infos", "99.0");

            using var response = await _fixture.CreateClient().SendAsync(
                request,
                TestContext.Current.CancellationToken);
            var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("UnsupportedApiVersion", content);
        }

        [Theory]
        [InlineData("v1")]
        [InlineData("v2")]
        public async Task SwaggerDocument_IsPublished(string groupName)
        {
            using var response = await _fixture.CreateClient().GetAsync(
                $"/swagger/{groupName}/swagger.json",
                TestContext.Current.CancellationToken);
            var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            Assert.True(response.StatusCode == HttpStatusCode.OK, content);
            Assert.Contains("\"openapi\"", content);
            Assert.Contains("\"/api/Infos\"", content);
        }

        private static HttpRequestMessage CreateAuthorizedJsonRequest(
            HttpMethod method,
            string path,
            string apiVersion = null)
        {
            var request = CreateAuthorizedRequest(method, path, apiVersion);
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            return request;
        }

        private static HttpRequestMessage CreateAuthorizedRequest(
            HttpMethod method,
            string path,
            string apiVersion = null)
        {
            var request = new HttpRequestMessage(method, path);
            request.Headers.Add(
                "api-key",
                $"{AuthEndpointFixture.AdminKeyId}:{AuthEndpointFixture.AdminSecret}");
            if (apiVersion != null)
            {
                request.Headers.Add("api-version", apiVersion);
            }

            return request;
        }
    }
}
