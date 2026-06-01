using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using adrapi.Directory;
using adrapi.domain.Exceptions;

namespace adrapi.Entra
{
    /// <summary>
    /// Tunables for the Graph client's retry/throttling behaviour.
    /// </summary>
    public class GraphClientOptions
    {
        /// <summary>Maximum retry attempts for throttled (429) or transient (5xx) responses.</summary>
        public int MaxRetries { get; set; } = 5;

        /// <summary>Base delay for the exponential backoff fallback (when no <c>Retry-After</c> is supplied).</summary>
        public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

        /// <summary>Upper bound on any single backoff/Retry-After wait.</summary>
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>Safety cap on pages followed by <see cref="IGraphClient.GetPagedAsync"/>.</summary>
        public int MaxPages { get; set; } = 1000;

        /// <summary>
        /// Delay primitive. Injected so tests can run the retry path without
        /// real waits. Defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.
        /// </summary>
        public Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;
    }

    /// <summary>A single Graph response: status, parsed JSON body (may be null), and correlation ids.</summary>
    public record GraphResult(HttpStatusCode StatusCode, JsonElement? Body, string RequestId, string ClientRequestId);

    public interface IGraphClient
    {
        Task<GraphResult> GetAsync(string relativeUrl, CancellationToken cancellationToken = default);

        /// <summary>
        /// Follows <c>@odata.nextLink</c> paging and returns every item across the
        /// <c>value</c> arrays as a flat list.
        /// </summary>
        Task<List<JsonElement>> GetPagedAsync(string relativeUrl, CancellationToken cancellationToken = default);

        Task<GraphResult> PostAsync(string relativeUrl, object body, CancellationToken cancellationToken = default);
        Task<GraphResult> PatchAsync(string relativeUrl, object body, CancellationToken cancellationToken = default);
        Task<GraphResult> DeleteAsync(string relativeUrl, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Thin Microsoft Graph HTTP wrapper with bearer-token injection, automatic
    /// retry with throttling (429) and transient (5xx) handling honouring the
    /// <c>Retry-After</c> header, and <c>@odata.nextLink</c> paging.
    ///
    /// One instance is bound to a single Entra-backed domain (<see cref="EntraConfig"/>)
    /// and reuses a shared <see cref="HttpClient"/>. Tokens are obtained from the
    /// injected <see cref="IEntraTokenProvider"/> per request — MSAL caches them,
    /// so this is cheap. This is the reusable foundation that Stage 4/5 user and
    /// group operations build on; it performs no object mapping itself.
    /// </summary>
    public class GraphClient : IGraphClient
    {
        // 5xx responses worth retrying (transient service / gateway faults).
        private static readonly HashSet<HttpStatusCode> RetryableStatuses = new()
        {
            HttpStatusCode.InternalServerError, // 500
            (HttpStatusCode)502,                // Bad Gateway
            HttpStatusCode.ServiceUnavailable,  // 503
            HttpStatusCode.GatewayTimeout,      // 504
        };

        private readonly HttpClient httpClient;
        private readonly IEntraTokenProvider tokenProvider;
        private readonly EntraConfig config;
        private readonly GraphClientOptions options;
        private readonly IDirectoryAuditSink auditSink;
        private readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        public GraphClient(
            EntraConfig config,
            IEntraTokenProvider tokenProvider,
            HttpClient httpClient,
            GraphClientOptions options = null,
            IDirectoryAuditSink auditSink = null)
        {
            this.config = config ?? throw new NullException("Entra config cannot be null");
            this.tokenProvider = tokenProvider ?? throw new NullException("Token provider cannot be null");
            this.httpClient = httpClient ?? throw new NullException("HttpClient cannot be null");
            this.options = options ?? new GraphClientOptions();
            this.auditSink = auditSink ?? NLogDirectoryAuditSink.Instance;
        }

        public Task<GraphResult> GetAsync(string relativeUrl, CancellationToken cancellationToken = default)
            => SendAsync(HttpMethod.Get, relativeUrl, null, cancellationToken);

        public Task<GraphResult> PostAsync(string relativeUrl, object body, CancellationToken cancellationToken = default)
            => SendAsync(HttpMethod.Post, relativeUrl, body, cancellationToken);

        public Task<GraphResult> PatchAsync(string relativeUrl, object body, CancellationToken cancellationToken = default)
            => SendAsync(HttpMethod.Patch, relativeUrl, body, cancellationToken);

        public Task<GraphResult> DeleteAsync(string relativeUrl, CancellationToken cancellationToken = default)
            => SendAsync(HttpMethod.Delete, relativeUrl, null, cancellationToken);

        public async Task<List<JsonElement>> GetPagedAsync(string relativeUrl, CancellationToken cancellationToken = default)
        {
            var items = new List<JsonElement>();
            string nextUrl = ResolveUrl(relativeUrl);
            int pages = 0;

            while (!string.IsNullOrEmpty(nextUrl))
            {
                if (++pages > options.MaxPages)
                {
                    logger.Warn("Graph paging hit MaxPages={max} for '{url}'; stopping early (results truncated).", options.MaxPages, relativeUrl);
                    break;
                }

                // nextLink is an absolute URL; pass it through verbatim.
                var result = await SendAsync(HttpMethod.Get, nextUrl, null, cancellationToken);
                if (result.Body is not { } body)
                {
                    break;
                }

                if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("value", out var valueArray)
                    && valueArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in valueArray.EnumerateArray())
                    {
                        items.Add(item.Clone());
                    }
                }

                nextUrl = body.ValueKind == JsonValueKind.Object
                    && body.TryGetProperty("@odata.nextLink", out var link)
                    && link.ValueKind == JsonValueKind.String
                        ? link.GetString()
                        : null;
            }

            return items;
        }

        private async Task<GraphResult> SendAsync(HttpMethod method, string url, object body, CancellationToken cancellationToken)
        {
            var absoluteUrl = ResolveUrl(url);
            Exception lastError = null;

            for (int attempt = 0; attempt <= options.MaxRetries; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var token = await tokenProvider.AcquireTokenAsync(config, cancellationToken);

                using var request = new HttpRequestMessage(method, absoluteUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
                if (body != null)
                {
                    var json = JsonSerializer.Serialize(body);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                HttpResponseMessage response;
                try
                {
                    response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
                }
                catch (HttpRequestException ex) when (attempt < options.MaxRetries)
                {
                    // Transport-level failure (DNS, socket reset, ...) — back off and retry.
                    lastError = ex;
                    logger.Warn(ex, "Graph request transport error (attempt {attempt}/{max}); retrying.", attempt + 1, options.MaxRetries);
                    await options.Delay(BackoffDelay(attempt, null), cancellationToken);
                    continue;
                }

                using (response)
                {
                    var requestId = FirstHeader(response, "request-id");
                    var clientRequestId = FirstHeader(response, "client-request-id");
                    var content = response.Content != null
                        ? await response.Content.ReadAsStringAsync(cancellationToken)
                        : string.Empty;

                    if (response.IsSuccessStatusCode)
                    {
                        Audit(method, absoluteUrl, (int)response.StatusCode, "success", requestId, clientRequestId);
                        return new GraphResult(response.StatusCode, ParseBody(content), requestId, clientRequestId);
                    }

                    var isThrottled = (int)response.StatusCode == 429;
                    var isTransient = RetryableStatuses.Contains(response.StatusCode);

                    if ((isThrottled || isTransient) && attempt < options.MaxRetries)
                    {
                        var delay = BackoffDelay(attempt, response.Headers.RetryAfter);
                        logger.Warn(
                            "Graph {method} {url} -> {status} (attempt {attempt}/{max}); retrying in {delay}ms. request-id={requestId}",
                            method, absoluteUrl, (int)response.StatusCode, attempt + 1, options.MaxRetries, (int)delay.TotalMilliseconds, requestId);
                        await options.Delay(delay, cancellationToken);
                        continue;
                    }

                    Audit(method, absoluteUrl, (int)response.StatusCode, "error", requestId, clientRequestId);
                    throw new GraphException(
                        $"Graph {method} {absoluteUrl} failed with {(int)response.StatusCode} {response.ReasonPhrase}.",
                        response.StatusCode,
                        requestId,
                        clientRequestId,
                        Truncate(content, 2048));
                }
            }

            // Retries exhausted on transport errors.
            Audit(method, absoluteUrl, 0, "error", null, null);
            throw new GraphException(
                $"Graph {method} {absoluteUrl} failed after {options.MaxRetries} retries.",
                statusCode: 0,
                inner: lastError);
        }

        /// <summary>
        /// Emits a structured audit record for a completed Graph operation,
        /// enriched with the ambient <see cref="DirectoryOperationContext"/>. The
        /// path (target object) is logged without its query string so search/
        /// filter values are not recorded.
        /// </summary>
        private void Audit(HttpMethod method, string absoluteUrl, int status, string outcome, string requestId, string clientRequestId)
        {
            var ctx = DirectoryOperationContext.Current;
            auditSink.Write(new GraphOperationLog(
                method.Method,
                PathOf(absoluteUrl),
                status,
                outcome,
                requestId,
                clientRequestId,
                ctx.Requester,
                ctx.CorrelationId,
                ctx.ClientIp));
        }

        private static string PathOf(string absoluteUrl)
            => Uri.TryCreate(absoluteUrl, UriKind.Absolute, out var uri)
                ? uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped)
                : absoluteUrl;

        private string ResolveUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new WrongParameterException("Graph request URL cannot be empty.");
            }

            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return url; // absolute (e.g. an @odata.nextLink)
            }

            var baseUrl = (config.GraphBaseUrl ?? EntraConfig.DefaultGraphBaseUrl).TrimEnd('/');
            return $"{baseUrl}/{url.TrimStart('/')}";
        }

        /// <summary>
        /// Computes the wait before the next attempt: honour <c>Retry-After</c>
        /// when Graph supplies it, otherwise exponential backoff capped at
        /// <see cref="GraphClientOptions.MaxDelay"/>.
        /// </summary>
        private TimeSpan BackoffDelay(int attempt, RetryConditionHeaderValue retryAfter)
        {
            if (retryAfter != null)
            {
                if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
                {
                    return Cap(delta);
                }

                if (retryAfter.Date is { } date)
                {
                    var until = date - DateTimeOffset.UtcNow;
                    if (until > TimeSpan.Zero)
                    {
                        return Cap(until);
                    }
                }
            }

            // 2^attempt * base, capped.
            var exponential = TimeSpan.FromMilliseconds(options.BaseDelay.TotalMilliseconds * Math.Pow(2, attempt));
            return Cap(exponential);
        }

        private TimeSpan Cap(TimeSpan delay) => delay > options.MaxDelay ? options.MaxDelay : delay;

        private static JsonElement? ParseBody(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(content);
                return doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string FirstHeader(HttpResponseMessage response, string name)
            => response.Headers.TryGetValues(name, out var values)
                ? string.Join(",", values)
                : null;

        private static string Truncate(string value, int max)
            => string.IsNullOrEmpty(value) || value.Length <= max ? value : value.Substring(0, max) + "…";
    }
}
