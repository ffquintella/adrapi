using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using adrapi.Directory;
using adrapi.Entra;
using adrapi.domain.Exceptions;

namespace tests
{
    /// <summary>
    /// Stage 8 (observability & auditability) unit tests: every Graph operation
    /// emits a structured audit record carrying the ambient requester/correlation/
    /// client-IP plus the Graph request-id, for success and failure. No network.
    /// </summary>
    public class GraphAuditTests
    {
        private sealed class CapturingSink : IDirectoryAuditSink
        {
            public readonly List<GraphOperationLog> Logs = new();
            public void Write(GraphOperationLog log) => Logs.Add(log);
        }

        private sealed class FakeTokenProvider : IEntraTokenProvider
        {
            public Task<EntraToken> AcquireTokenAsync(EntraConfig config, CancellationToken ct = default)
                => Task.FromResult(new EntraToken("t", DateTimeOffset.UtcNow.AddHours(1)));
        }

        private sealed class ScriptedHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> responder;
            public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => this.responder = responder;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
                => Task.FromResult(responder(request));
        }

        private static HttpResponseMessage Resp(HttpStatusCode status, string requestId)
        {
            var r = new HttpResponseMessage(status) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            if (requestId != null) r.Headers.TryAddWithoutValidation("request-id", requestId);
            return r;
        }

        private static GraphClient Build(CapturingSink sink, Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            var options = new GraphClientOptions { Delay = (_, __) => Task.CompletedTask };
            return new GraphClient(
                new EntraConfig { DomainKey = "cloud", GraphBaseUrl = "https://graph.microsoft.com/v1.0" },
                new FakeTokenProvider(), new HttpClient(new ScriptedHandler(responder)), options, sink);
        }

        // ---- Ambient context ----

        [Fact]
        public void Context_ScopesAndRestores()
        {
            Assert.Equal("unknown", DirectoryOperationContext.Current.Requester);

            using (DirectoryOperationContext.BeginScope(new DirectoryOperationContext
            {
                Requester = "key-1", CorrelationId = "corr-1", ClientIp = "10.0.0.9",
            }))
            {
                Assert.Equal("key-1", DirectoryOperationContext.Current.Requester);
                Assert.Equal("corr-1", DirectoryOperationContext.Current.CorrelationId);
            }

            Assert.Equal("unknown", DirectoryOperationContext.Current.Requester); // restored
        }

        // ---- Audit emission ----

        [Fact]
        public async Task SuccessfulOperation_EmitsAuditWithContextAndRequestId()
        {
            var sink = new CapturingSink();
            var client = Build(sink, _ => Resp(HttpStatusCode.OK, "graph-req-1"));

            using (DirectoryOperationContext.BeginScope(new DirectoryOperationContext
            {
                Requester = "key-1", CorrelationId = "corr-1", ClientIp = "10.0.0.9",
            }))
            {
                await client.GetAsync("users/ada@contoso.com?$select=id");
            }

            var log = Assert.Single(sink.Logs);
            Assert.Equal("GET", log.Method);
            Assert.Equal("success", log.Outcome);
            Assert.Equal(200, log.StatusCode);
            Assert.Equal("graph-req-1", log.GraphRequestId); // surfaced for MS support correlation
            Assert.Equal("key-1", log.Requester);
            Assert.Equal("corr-1", log.CorrelationId);
            Assert.Equal("10.0.0.9", log.ClientIp);
            // Path is the target object; the query string is not recorded.
            Assert.Contains("users/ada@contoso.com", log.Path);
            Assert.DoesNotContain("$select", log.Path);
        }

        [Fact]
        public async Task FailedOperation_EmitsErrorAudit()
        {
            var sink = new CapturingSink();
            var client = Build(sink, _ => Resp(HttpStatusCode.NotFound, "graph-req-404"));

            await Assert.ThrowsAsync<GraphException>(() => client.GetAsync("users/missing"));

            var log = Assert.Single(sink.Logs);
            Assert.Equal("error", log.Outcome);
            Assert.Equal(404, log.StatusCode);
            Assert.Equal("graph-req-404", log.GraphRequestId);
        }

        [Fact]
        public async Task OutsideScope_AuditUsesUnknownIdentity()
        {
            var sink = new CapturingSink();
            var client = Build(sink, _ => Resp(HttpStatusCode.OK, "r"));

            await client.GetAsync("groups");

            var log = Assert.Single(sink.Logs);
            Assert.Equal("unknown", log.Requester);
            Assert.Equal("unknown", log.ClientIp);
        }
    }
}
