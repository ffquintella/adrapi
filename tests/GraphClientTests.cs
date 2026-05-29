using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using adrapi.Entra;
using adrapi.domain.Exceptions;

namespace tests
{
    /// <summary>
    /// Stage 3 (Graph client foundation) unit tests: bearer-token injection,
    /// 429/5xx retry honouring Retry-After, non-retryable error surfacing, and
    /// @odata.nextLink paging. No network — a fake handler scripts responses and
    /// the backoff delay is a no-op recorder.
    /// </summary>
    public class GraphClientTests
    {
        private sealed class FakeTokenProvider : IEntraTokenProvider
        {
            public int Calls;
            public Task<EntraToken> AcquireTokenAsync(EntraConfig config, CancellationToken cancellationToken = default)
            {
                Calls++;
                return Task.FromResult(new EntraToken("fake-token", DateTimeOffset.UtcNow.AddHours(1)));
            }
        }

        private sealed class ScriptedHandler : HttpMessageHandler
        {
            private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> responses;
            public readonly List<HttpRequestMessage> Requests = new();

            public ScriptedHandler(IEnumerable<Func<HttpRequestMessage, HttpResponseMessage>> responses)
                => this.responses = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responses);

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                var factory = responses.Dequeue();
                return Task.FromResult(factory(request));
            }
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body, string requestId = "req-123")
        {
            var resp = new HttpResponseMessage(status)
            {
                Content = new StringContent(body ?? string.Empty, Encoding.UTF8, "application/json"),
            };
            if (requestId != null) resp.Headers.TryAddWithoutValidation("request-id", requestId);
            return resp;
        }

        private static (GraphClient client, ScriptedHandler handler, FakeTokenProvider token, List<TimeSpan> delays) Build(
            params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        {
            var handler = new ScriptedHandler(responses);
            var token = new FakeTokenProvider();
            var delays = new List<TimeSpan>();
            var cfg = new EntraConfig { DomainKey = "cloud", GraphBaseUrl = "https://graph.microsoft.com/v1.0" };
            var options = new GraphClientOptions
            {
                BaseDelay = TimeSpan.FromMilliseconds(10),
                Delay = (d, _) => { delays.Add(d); return Task.CompletedTask; },
            };
            var client = new GraphClient(cfg, token, new HttpClient(handler), options);
            return (client, handler, token, delays);
        }

        [Fact]
        public async Task Get_Success_ParsesBodyAndInjectsBearerToken()
        {
            var (client, handler, token, _) = Build(_ => Json(HttpStatusCode.OK, "{\"id\":\"abc\"}"));

            var result = await client.GetAsync("users/abc");

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Equal("abc", result.Body!.Value.GetProperty("id").GetString());
            Assert.Equal("req-123", result.RequestId);
            Assert.Equal(1, token.Calls);

            var sent = handler.Requests.Single();
            Assert.Equal("https://graph.microsoft.com/v1.0/users/abc", sent.RequestUri!.ToString());
            Assert.Equal("Bearer", sent.Headers.Authorization!.Scheme);
            Assert.Equal("fake-token", sent.Headers.Authorization.Parameter);
        }

        [Fact]
        public async Task Get_ThrottledThenSucceeds_RetriesHonouringRetryAfter()
        {
            var throttled = new Func<HttpRequestMessage, HttpResponseMessage>(_ =>
            {
                var r = Json((HttpStatusCode)429, "{}");
                r.Headers.TryAddWithoutValidation("Retry-After", "7");
                return r;
            });

            var (client, handler, _, delays) = Build(throttled, _ => Json(HttpStatusCode.OK, "{\"ok\":true}"));

            var result = await client.GetAsync("users");

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Equal(2, handler.Requests.Count);
            Assert.Single(delays);
            Assert.Equal(TimeSpan.FromSeconds(7), delays[0]); // Retry-After respected
        }

        [Fact]
        public async Task Get_Transient5xx_RetriesWithExponentialBackoff()
        {
            var (client, handler, _, delays) = Build(
                _ => Json(HttpStatusCode.ServiceUnavailable, "{}"),
                _ => Json(HttpStatusCode.ServiceUnavailable, "{}"),
                _ => Json(HttpStatusCode.OK, "{\"ok\":true}"));

            var result = await client.GetAsync("groups");

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Equal(3, handler.Requests.Count);
            Assert.Equal(2, delays.Count);
            Assert.True(delays[1] > delays[0]); // exponential growth
        }

        [Fact]
        public async Task Get_NonRetryableError_ThrowsGraphExceptionWithStatusAndRequestId()
        {
            var (client, handler, _, _) = Build(_ => Json(HttpStatusCode.NotFound, "{\"error\":\"nope\"}", "req-404"));

            var ex = await Assert.ThrowsAsync<GraphException>(() => client.GetAsync("users/missing"));

            Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
            Assert.Equal("req-404", ex.RequestId);
            Assert.Single(handler.Requests); // not retried
        }

        [Fact]
        public async Task Get_ExhaustsRetries_ThrowsLastStatus()
        {
            var responses = Enumerable.Range(0, 6)
                .Select(_ => new Func<HttpRequestMessage, HttpResponseMessage>(__ => Json((HttpStatusCode)429, "{}")))
                .ToArray();
            var (client, handler, _, _) = Build(responses);

            var ex = await Assert.ThrowsAsync<GraphException>(() => client.GetAsync("users"));

            Assert.Equal((HttpStatusCode)429, ex.StatusCode);
            Assert.Equal(6, handler.Requests.Count); // initial + 5 retries
        }

        [Fact]
        public async Task GetPaged_FollowsNextLink_AggregatesAllItems()
        {
            var page1 = "{\"value\":[{\"id\":\"1\"},{\"id\":\"2\"}],\"@odata.nextLink\":\"https://graph.microsoft.com/v1.0/users?$skiptoken=abc\"}";
            var page2 = "{\"value\":[{\"id\":\"3\"}]}";

            var (client, handler, _, _) = Build(
                _ => Json(HttpStatusCode.OK, page1),
                _ => Json(HttpStatusCode.OK, page2));

            var items = await client.GetPagedAsync("users");

            Assert.Equal(3, items.Count);
            Assert.Equal(new[] { "1", "2", "3" }, items.Select(i => i.GetProperty("id").GetString()));
            // Second request used the absolute nextLink verbatim.
            Assert.Equal("https://graph.microsoft.com/v1.0/users?$skiptoken=abc", handler.Requests[1].RequestUri!.ToString());
        }

        [Fact]
        public async Task Post_SerializesJsonBody()
        {
            string capturedBody = null;
            HttpMethod capturedMethod = null;
            var (client, _, _, _) = Build(req =>
            {
                // Read the body here — the client disposes the request after returning.
                capturedMethod = req.Method;
                capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return Json(HttpStatusCode.Created, "{\"id\":\"new\"}");
            });

            await client.PostAsync("groups", new { displayName = "Engineers", mailEnabled = false });

            Assert.Equal(HttpMethod.Post, capturedMethod);
            Assert.Contains("\"displayName\":\"Engineers\"", capturedBody);
            Assert.Contains("\"mailEnabled\":false", capturedBody);
        }
    }
}
