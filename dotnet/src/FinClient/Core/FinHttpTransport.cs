using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.CompilerServices;
using Fin.Copy;

namespace Fin;

/// <summary>How one Fin service is reached: its name in messages, its key header, its key and its address.</summary>
internal sealed class FinServiceConfig
{
    public FinServiceConfig(string label, string keyHeader, string? apiKey, Uri baseUri, bool keyRequiredAtUse, string keyHint)
    {
        Label = label;
        KeyHeader = keyHeader;
        ApiKey = apiKey?.Trim() ?? "";
        BaseUri = baseUri;
        KeyRequiredAtUse = keyRequiredAtUse;
        KeyHint = keyHint;
    }

    /// <summary>The service's name in error messages, such as "FinGraph".</summary>
    public string Label { get; }
    /// <summary>The request header that carries the API key.</summary>
    public string KeyHeader { get; }
    public string ApiKey { get; }
    public Uri BaseUri { get; }
    /// <summary>When true a missing key fails the first call; otherwise requests go out without one (a member session authenticates them).</summary>
    public bool KeyRequiredAtUse { get; }
    /// <summary>What to tell the caller to set when the key is missing.</summary>
    public string KeyHint { get; }
}

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
    private readonly FinServiceConfig _service;
    private readonly Uri _baseUri;

    /// <summary>Creates the transport for FinCopy.</summary>
    public FinHttpTransport(HttpClient http, FinClientOptions options, bool allowSession = false)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (options is null) throw new ArgumentNullException(nameof(options));
        _options = options.Snapshot();
        _baseUri = _options.ValidatedBaseUri(requireApiKey: !allowSession);
        // With a service block given FinCopy's key may be absent: using FinCopy then fails at its first call.
        _service = new FinServiceConfig("FinCopy", "X-Api-Key", _options.ApiKey, _baseUri,
            keyRequiredAtUse: !allowSession, keyHint: "FinClientOptions.ApiKey");
    }

    /// <summary>Creates the transport for FinGraph, FinNews or FinSocial: the shared timeout and retry settings, that service's key and address.</summary>
    public FinHttpTransport(HttpClient http, FinClientOptions shared, FinServiceConfig service)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = (shared ?? throw new ArgumentNullException(nameof(shared))).Snapshot();
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _baseUri = service.BaseUri;
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

    /// <summary>
    /// Reads the server-sent event stream until the server closes it or the caller cancels. It does not reconnect: a closed
    /// or broken stream ends the enumeration (broken: a <see cref="FinApiException"/> with <c>ErrorCode</c> "network").
    /// </summary>
    public async IAsyncEnumerable<FinStreamEvent> ReadServerSentEventsAsync(string path,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var response = await OpenStreamAsync(path, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw ToException((int)response.StatusCode, await response.Content.ReadAsStringAsync().ConfigureAwait(false),
                RetryAfterOf(response.Headers.RetryAfter));
        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        // netstandard2.0's ReadLineAsync takes no token: closing the stream is what ends a read that is waiting.
        using var abort = cancellationToken.Register(() => stream.Dispose());
        var parser = new FinSseParser();
        while (true)
        {
            var line = await ReadEventLineAsync(reader, cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                if (parser.Complete() is { } last) yield return last;
                yield break;
            }
            if (parser.Add(line) is { } item) yield return item;
        }
    }

    private async Task<HttpResponseMessage> OpenStreamAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri, path.TrimStart('/')));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (!string.IsNullOrWhiteSpace(_service.ApiKey))
            request.Headers.TryAddWithoutValidation(_service.KeyHeader, _service.ApiKey);
        try
        {
            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            throw new FinApiException(0, "The FinCopy event stream could not be opened.", innerException: ex, errorCode: "network");
        }
    }

    private static async Task<string?> ReadEventLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
#if NET8_0_OR_GREATER
            return await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
#else
            cancellationToken.ThrowIfCancellationRequested();
            return await reader.ReadLineAsync().ConfigureAwait(false);
#endif
        }
        catch (Exception ex) when (cancellationToken.IsCancellationRequested
            && ex is IOException or ObjectDisposedException or HttpRequestException)
        {
            throw new OperationCanceledException("The FinCopy event stream was cancelled.", ex, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        {
            throw new FinApiException(0, "The FinCopy event stream was interrupted.", innerException: ex, errorCode: "network");
        }
    }

    /// <summary>
    /// Sends one request to FinGraph, FinNews or FinSocial and returns the reply read as the operation's documented 200
    /// response. Refusals (401, 402, 403, 429 and the rest) become a <see cref="FinApiException"/>.
    /// </summary>
    public async Task<T> SendApiAsync<T>(string method, string pathAndQuery, object? body, string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var reply = await ExchangeAsync(method, pathAndQuery, body, idempotencyKey, cancellationToken).ConfigureAwait(false);
        if (!reply.Ok) throw Refusal(reply.Status, reply.Raw, reply.RetryAfter);
        if (string.IsNullOrWhiteSpace(reply.Raw))
            throw new FinApiException(reply.Status, $"The {_service.Label} API returned an empty response.", errorCode: "empty_response");
        try
        {
            using (var document = JsonDocument.Parse(reply.Raw))
            {
                // The /api routes wrap their data in an envelope that can say it failed even on HTTP 200.
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("isOk", out var ok) && ok.ValueKind == JsonValueKind.False)
                    throw Refusal(reply.Status, reply.Raw, reply.RetryAfter);
            }
            var value = JsonSerializer.Deserialize<T>(reply.Raw, ReadOptions);
            if (value is null)
                throw new FinApiException(reply.Status, $"The {_service.Label} API returned an empty response.", errorCode: "empty_response");
            return value;
        }
        catch (JsonException ex)
        {
            throw new FinApiException(reply.Status, $"The {_service.Label} API returned an invalid response.", innerException: ex,
                errorCode: "invalid_response");
        }
    }

    private async Task<Reply> SendTextAsync(string method, string path, object? body, string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var reply = await ExchangeAsync(method, path, body, idempotencyKey, cancellationToken).ConfigureAwait(false);
        var status = reply.Status;
        var raw = reply.Raw;
        if (reply.Retryable) throw ToException(status, raw, reply.RetryAfter);
        if (!TryInspect(raw, out var isOk, out var hasData))
        {
            throw new FinApiException(status,
                reply.Ok ? $"The {_service.Label} API returned an invalid response." : SafeFallbackMessage(reply.StatusCode),
                errorCode: reply.Ok ? "invalid_response" : null);
        }
        if (!reply.Ok || !isOk) throw ToException(status, raw, null);
        return new Reply(status, raw, hasData);
    }

    /// <summary>Sends one request with the retry rules shared by every service and returns the last reply.</summary>
    private async Task<Exchange> ExchangeAsync(string method, string path, object? body, string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_service.ApiKey) && _service.KeyRequiredAtUse)
            throw new InvalidOperationException($"{_service.Label} needs an API key. Set {_service.KeyHint} when you create the FinClient.");
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
            cancellationToken.ThrowIfCancellationRequested();
            HttpResponseMessage response;
            string raw;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_options.Timeout);
                using var request = new HttpRequestMessage(httpMethod, requestUri);
                if (!string.IsNullOrWhiteSpace(_service.ApiKey))
                    request.Headers.TryAddWithoutValidation(_service.KeyHeader, _service.ApiKey);
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
                    $"The {_service.Label} API did not answer within {_options.Timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)} seconds.",
                    innerException: ex, errorCode: "timeout");
            }
            catch (HttpRequestException ex)
            {
                if (retry < _options.MaxRetries)
                {
                    await Delay(Backoff(retry), cancellationToken).ConfigureAwait(false);
                    continue;
                }
                throw new FinApiException(0, $"The {_service.Label} API could not be reached.", innerException: ex, errorCode: "network");
            }

            using (response)
            {
                var status = (int)response.StatusCode;
                var serverWait = RetryAfterOf(response.Headers.RetryAfter);
                if (IsRetryable(response.StatusCode))
                {
                    if (retry < _options.MaxRetries && (serverWait is null || serverWait <= MaxServerRetryAfter))
                    {
                        var wait = serverWait ?? (status == 429 ? RateLimitBackoff(retry) : Backoff(retry));
                        if (wait > TimeSpan.Zero) await Delay(wait, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    return new Exchange(response.StatusCode, raw, serverWait, retryable: true);
                }
                return new Exchange(response.StatusCode, raw, serverWait, retryable: false);
            }
        }
    }

    private readonly struct Exchange
    {
        public Exchange(HttpStatusCode statusCode, string raw, TimeSpan? retryAfter, bool retryable)
        {
            StatusCode = statusCode;
            Raw = raw;
            RetryAfter = retryAfter;
            Retryable = retryable;
        }
        public HttpStatusCode StatusCode { get; }
        public int Status => (int)StatusCode;
        public bool Ok => (int)StatusCode is >= 200 and < 300;
        public string Raw { get; }
        public TimeSpan? RetryAfter { get; }
        /// <summary>A retryable status that ran out of retries (or whose Retry-After was too long to wait out).</summary>
        public bool Retryable { get; }
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

    private FinApiException ToException(int statusCode, string raw, TimeSpan? retryAfter)
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

    private string SafeFallbackMessage(HttpStatusCode status) =>
        $"The {_service.Label} API request failed with HTTP {(int)status} ({status.ToString().ToUpperInvariant()}).";

    /// <summary>
    /// Reads a refusal from any of the three reply shapes: FinApiError (<c>{ error, message, lane, limit, catag, tier }</c>),
    /// the /v1 error body (<c>{ error: { code, message } }</c>) and the /api envelope (<c>{ isOk, code, message, errors }</c>).
    /// </summary>
    private FinApiException Refusal(int statusCode, string raw, TimeSpan? retryAfter)
    {
        int? code = null;
        string? message = null, error = null, lane = null, catag = null, tier = null;
        JsonElement? limit = null;
        IReadOnlyList<string>? errors = null;
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.Number
                    && codeElement.TryGetInt32(out var parsedCode)) code = parsedCode;
                message = Text(root, "message");
                if (root.TryGetProperty("errors", out var errorsElement)) errors = ErrorStrings(errorsElement);
                if (root.TryGetProperty("error", out var errorElement))
                {
                    if (errorElement.ValueKind == JsonValueKind.Object)
                    {
                        error = Text(errorElement, "code") ?? Text(errorElement, "Code");
                        message ??= Text(errorElement, "message") ?? Text(errorElement, "Message");
                    }
                    else error = errorElement.ValueKind == JsonValueKind.String ? errorElement.GetString() : null;
                }
                lane = Text(root, "lane");
                catag = Text(root, "catag");
                tier = Text(root, "tier");
                if (root.TryGetProperty("limit", out var limitElement) && limitElement.ValueKind != JsonValueKind.Null)
                    limit = limitElement.Clone();
            }
        }
        catch (JsonException) { }
        return new FinApiException(statusCode, message ?? SafeFallbackMessage((HttpStatusCode)statusCode), code, errors,
            errorCode: statusCode == 429 ? "rate_limited" : null, retryAfter: retryAfter,
            error: error, lane: lane, limit: limit, catag: catag, tier: tier);
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            ? value.ValueKind switch { JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetRawText(), _ => null }
            : null;
}
