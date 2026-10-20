using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fin;

internal sealed class FinHttpTransport
{
    /// <summary>A server Retry-After longer than this is not waited out: the call fails and reports it.</summary>
    internal static readonly TimeSpan MaxServerRetryAfter = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);
    private static readonly HttpMethod PatchMethod = new("PATCH");
    internal static readonly JsonSerializerOptions ReadOptions = CreateSerializerOptions(omitNulls: false);
    internal static readonly JsonSerializerOptions WriteOptions = CreateSerializerOptions(omitNulls: true);
    private static readonly object RandomGate = new();
    private static readonly Random Jitter = new();
    private readonly HttpClient _http;
    private readonly FinClientOptions _options;
    private readonly Uri _baseUri;

    public FinHttpTransport(HttpClient http, FinClientOptions options)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (options is null) throw new ArgumentNullException(nameof(options));
        _options = options.Snapshot();
        _baseUri = _options.ValidatedBaseUri();
    }

    /// <summary>Waits between attempts. Replaceable so tests run without real delays.</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = (delay, token) => Task.Delay(delay, token);

    public async Task<T> SendAsync<T>(string method, string path, object? body = null, string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        var reply = await SendTextAsync(method, path, body, idempotencyKey, cancellationToken).ConfigureAwait(false);
        if (!reply.HasData)
            throw new FinApiException(reply.Status, "The FinCopy API returned an empty response.", errorCode: "empty_response");
        FinApiEnvelope<T>? envelope;
        try { envelope = JsonSerializer.Deserialize<FinApiEnvelope<T>>(reply.Raw, ReadOptions); }
        catch (JsonException ex)
        {
            throw new FinApiException(reply.Status, "The FinCopy API returned an invalid response.", innerException: ex,
                errorCode: "invalid_response");
        }
        if (envelope is null || envelope.Data is null)
            throw new FinApiException(reply.Status, "The FinCopy API returned an empty response.", errorCode: "empty_response");
        return envelope.Data;
    }

    public async Task<FinPage<T>> SendPageAsync<T>(string method, string path, object? body = null,
        string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var reply = await SendTextAsync(method, path, body, idempotencyKey, cancellationToken).ConfigureAwait(false);
        FinApiPagedEnvelope<T>? envelope;
        try { envelope = JsonSerializer.Deserialize<FinApiPagedEnvelope<T>>(reply.Raw, ReadOptions); }
        catch (JsonException ex)
        {
            throw new FinApiException(reply.Status, "The FinCopy API returned an invalid response.", innerException: ex,
                errorCode: "invalid_response");
        }
        if (envelope is null)
            throw new FinApiException(reply.Status, "The FinCopy API returned an empty response.", errorCode: "empty_response");
        return new FinPage<T>(envelope.Data, envelope.Paging, envelope.Code, envelope.IsOk,
            envelope.Message, ErrorStrings(envelope.Errors), envelope.Chksum);
    }

    private async Task<Reply> SendTextAsync(string method, string path, object? body, string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var httpMethod = method.ToUpperInvariant() switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "PATCH" => PatchMethod,
            "DELETE" => HttpMethod.Delete,
            _ => throw new ArgumentOutOfRangeException(nameof(method), "Unsupported HTTP method.")
        };
        var isWrite = httpMethod != HttpMethod.Get;
        // One key per logical call, reused on every retry, so the API applies a write once however often it is sent.
        var requestKey = isWrite ? (idempotencyKey is null ? Guid.NewGuid().ToString("N") : ValidIdempotencyKey(idempotencyKey)) : null;
        var requestJson = body is null ? null : JsonSerializer.Serialize(body, body.GetType(), WriteOptions);
        var requestUri = new Uri(_baseUri, path.TrimStart('/'));

        for (var retry = 0; ; retry++)
        {
            HttpResponseMessage response;
            string raw;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_options.Timeout);
                using var request = new HttpRequestMessage(httpMethod, requestUri);
                request.Headers.TryAddWithoutValidation("X-Api-Key", _options.ApiKey);
                if (requestKey is not null) request.Headers.TryAddWithoutValidation("Idempotency-Key", requestKey);
                if (requestJson is not null) request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
                raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                if (retry < _options.MaxRetries)
                {
                    await Delay(Backoff(retry), cancellationToken).ConfigureAwait(false);
                    continue;
                }
                throw new FinApiException(0,
                    $"The FinCopy API did not answer within {_options.Timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)} seconds.",
                    innerException: ex, errorCode: "timeout");
            }
            catch (HttpRequestException ex)
            {
                if (retry < _options.MaxRetries)
                {
                    await Delay(Backoff(retry), cancellationToken).ConfigureAwait(false);
                    continue;
                }
                throw new FinApiException(0, "The FinCopy API could not be reached.", innerException: ex, errorCode: "network");
            }

            using (response)
            {
                var status = (int)response.StatusCode;
                if (IsRetryable(response.StatusCode))
                {
                    var serverWait = RetryAfterOf(response.Headers.RetryAfter);
                    if (retry < _options.MaxRetries && (serverWait is null || serverWait <= MaxServerRetryAfter))
                    {
                        var wait = serverWait ?? (status == 429 ? RateLimitBackoff(retry) : Backoff(retry));
                        if (wait > TimeSpan.Zero) await Delay(wait, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    throw ToException(status, raw, serverWait);
                }

                if (!TryInspect(raw, out var isOk, out var hasData))
                {
                    throw new FinApiException(status,
                        response.IsSuccessStatusCode ? "The FinCopy API returned an invalid response." : SafeFallbackMessage(response.StatusCode),
                        errorCode: response.IsSuccessStatusCode ? "invalid_response" : null);
                }
                if (!response.IsSuccessStatusCode || !isOk) throw ToException(status, raw, null);
                return new Reply(status, raw, hasData);
            }
        }
    }

    private readonly struct Reply
    {
        public Reply(int status, string raw, bool hasData) { Status = status; Raw = raw; HasData = hasData; }
        public int Status { get; }
        public string Raw { get; }
        public bool HasData { get; }
    }

    private static string ValidIdempotencyKey(string key)
    {
        if (key.Length is < 1 or > 120 || string.IsNullOrWhiteSpace(key) || key.Any(char.IsControl))
            throw new ArgumentException("An idempotency key must be 1 to 120 characters long and contain no control characters.",
                "idempotencyKey");
        return key;
    }

    private static JsonSerializerOptions CreateSerializerOptions(bool omitNulls)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = null, PropertyNameCaseInsensitive = false };
        // A request body sends only what the caller set; the API reads a missing field and a null one the same way.
        if (omitNulls) options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.Converters.Add(new FinTolerantEnumConverterFactory());
        return options;
    }

    private static bool IsRetryable(HttpStatusCode status) => status == (HttpStatusCode)429
        || status == HttpStatusCode.BadGateway || status == HttpStatusCode.ServiceUnavailable || status == HttpStatusCode.GatewayTimeout;

    private static TimeSpan? RetryAfterOf(RetryConditionHeaderValue? retryAfter)
    {
        var delay = retryAfter?.Delta;
        if (delay is null && retryAfter?.Date is { } date) delay = date - DateTimeOffset.UtcNow;
        return delay is { } value && value < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    /// <summary>Server errors: 250 ms doubling, with jitter, at most 30 s.</summary>
    private static TimeSpan Backoff(int retry) =>
        TimeSpan.FromMilliseconds(Math.Min(MaxBackoff.TotalMilliseconds, 250 * Math.Pow(2, retry) * NextJitter()));

    /// <summary>A rate limit with no Retry-After: 1 s doubling, with jitter, never under 1 s, at most 30 s.</summary>
    private static TimeSpan RateLimitBackoff(int retry)
    {
        var seconds = Math.Min(MaxBackoff.TotalSeconds, Math.Pow(2, retry)) * NextJitter();
        return TimeSpan.FromSeconds(Math.Min(MaxBackoff.TotalSeconds, Math.Max(1, seconds)));
    }

    private static double NextJitter()
    {
        lock (RandomGate) return 0.75 + Jitter.NextDouble() * 0.5;
    }

    private static bool TryInspect(string raw, out bool isOk, out bool hasData)
    {
        isOk = false;
        hasData = false;
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            isOk = root.TryGetProperty("isOk", out var ok) && ok.ValueKind == JsonValueKind.True;
            hasData = root.TryGetProperty("data", out var data) && data.ValueKind != JsonValueKind.Null;
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static FinApiException ToException(int statusCode, string raw, TimeSpan? retryAfter)
    {
        int? code = null;
        string? message = null;
        IReadOnlyList<string>? errors = null;
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.Number
                    && codeElement.TryGetInt32(out var parsedCode)) code = parsedCode;
                if (root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String)
                    message = messageElement.GetString();
                if (root.TryGetProperty("errors", out var errorsElement)) errors = ErrorStrings(errorsElement);
            }
        }
        catch (JsonException) { }
        return new FinApiException(statusCode, message ?? SafeFallbackMessage((HttpStatusCode)statusCode), code, errors,
            errorCode: statusCode == 429 ? "rate_limited" : null, retryAfter: retryAfter);
    }

    private static IReadOnlyList<string> ErrorStrings(JsonElement errors) =>
        errors.ValueKind == JsonValueKind.Array
            ? errors.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : x.GetRawText()).ToList()
            : Array.Empty<string>();

    private static string SafeFallbackMessage(HttpStatusCode status) =>
        $"The FinCopy API request failed with HTTP {(int)status} ({status.ToString().ToUpperInvariant()}).";
}
