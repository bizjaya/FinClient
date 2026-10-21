using System.Net;
using System.Security.Cryptography;
using System.Text;
using Fin;
using Fin.Copy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fin.Tests;

public sealed class TransportTests
{
    [Fact]
    public async Task UsesApiKeyAndPreservesPascalCaseData()
    {
        var handler = new QueueHandler(_ => Json(HttpStatusCode.OK,
            "{\"data\":[{\"ACNO\":1234,\"USNO\":77,\"ValDt\":\"2026-10-20T00:00:00Z\",\"ACCTEQT\":10.5}],\"code\":200,\"isOk\":true}"));
        using var fin = NewClient(new FinClientOptions("unit-test-key"), handler);

        var rows = await fin.Copy.Accounts.PerformanceAsync(1234);

        Assert.Equal(1234, rows[0].ACNO);
        Assert.Equal("unit-test-key", handler.Requests[0].Headers.GetValues("X-Api-Key").Single());
        Assert.Equal("https://api.fincopy.pro/v1/fxs/accounts/1234/performance", handler.Requests[0].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task PerformanceDatesAreSentInUtc()
    {
        var handler = new QueueHandler(_ => Json(HttpStatusCode.OK, "{\"data\":[],\"code\":200,\"isOk\":true}"));
        using var fin = NewClient(new FinClientOptions("test"), handler);

        await fin.Copy.Accounts.PerformanceAsync(1234, new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.FromHours(8)));

        Assert.Contains("fromDt=2026-10-20T00%3A00%3A00.0000000%2B00%3A00", handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task PagesIncludeItemsAndPagingMetadata()
    {
        var handler = new QueueHandler(_ => Json(HttpStatusCode.OK,
            "{\"data\":[{}],\"paging\":{\"pageNo\":2,\"pageSize\":3,\"pageCount\":4,\"resCount\":11},\"code\":200,\"isOk\":true,\"errors\":[]}"));
        using var fin = NewClient(new FinClientOptions("test"), handler);

        var page = await fin.Copy.Accounts.OrdersAsync(1234, new FinPageQuery { PageNo = 2, PageSize = 3, Search = "EUR USD" });

        Assert.Single(page.Items);
        Assert.Equal(2, page.Paging!.PageNo);
        Assert.Equal(11, page.Paging.ResCount);
        Assert.Contains("PageNo=2", handler.Requests[0].RequestUri!.Query);
        Assert.Contains("search=EUR%20USD", handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task RetriesReuseWriteIdempotencyKeyAndHonorRetryAfter()
    {
        var calls = 0;
        var handler = new QueueHandler(_ =>
        {
            calls++;
            if (calls == 1)
            {
                var retry = Json(HttpStatusCode.TooManyRequests, "{\"code\":429,\"isOk\":false,\"message\":\"wait\"}");
                retry.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(5));
                return retry;
            }
            return Json(HttpStatusCode.OK, "{\"data\":{},\"code\":200,\"isOk\":true}");
        });
        using var fin = NewClient(new FinClientOptions("test") { MaxRetries = 1 }, handler, out var delays);

        _ = await fin.Copy.Orders.MakeAsync(1234, new CmdApiFxsOrdrMake { SYM = "EURUSD", LOT = 0.05 }, "same-key");

        Assert.Equal(2, calls);
        Assert.Equal(new[] { TimeSpan.FromSeconds(5) }, delays);
        var keys = handler.Requests.Select(x => x.Headers.GetValues("Idempotency-Key").Single()).Distinct().ToArray();
        Assert.Equal(new[] { "same-key" }, keys);
        Assert.All(handler.Bodies, body => Assert.Contains("\"SYM\":\"EURUSD\"", body));
        Assert.All(handler.Bodies, body => Assert.Contains("\"LOT\":0.05", body));
    }

    [Fact]
    public async Task RateLimitWithoutRetryAfterWaitsAtLeastOneSecondAndDoubles()
    {
        var calls = 0;
        var handler = new QueueHandler(_ => ++calls < 3
            ? Json(HttpStatusCode.TooManyRequests, "{\"code\":429,\"isOk\":false,\"message\":\"slow down\"}")
            : Json(HttpStatusCode.OK, "{\"data\":true,\"code\":200,\"isOk\":true}"));
        using var fin = NewClient(new FinClientOptions("test") { MaxRetries = 2 }, handler, out var delays);

        Assert.True(await fin.Copy.Webhooks.DeleteAsync());

        Assert.Equal(2, delays.Count);
        Assert.InRange(delays[0], TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.25));
        Assert.InRange(delays[1], TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2.5));
    }

    [Fact]
    public async Task RetryAfterLongerThanTheCapFailsAtOnceAndReportsIt()
    {
        var handler = new QueueHandler(_ =>
        {
            var reply = Json(HttpStatusCode.TooManyRequests, "{\"code\":429,\"isOk\":false,\"message\":\"wait a while\"}");
            reply.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
            return reply;
        });
        using var fin = NewClient(new FinClientOptions("test") { MaxRetries = 3 }, handler, out var delays);

        var exception = await Assert.ThrowsAsync<FinApiException>(() => fin.Copy.Accounts.ListAsync());

        Assert.Single(handler.Requests);
        Assert.Empty(delays);
        Assert.Equal(429, exception.StatusCode);
        Assert.Equal("rate_limited", exception.ErrorCode);
        Assert.Equal(TimeSpan.FromSeconds(120), exception.RetryAfter);
        Assert.Equal("wait a while", exception.Message);
    }

    [Fact]
    public async Task RetriesTransientServiceFailureWithBackoff()
    {
        var calls = 0;
        var handler = new QueueHandler(_ => ++calls == 1
            ? Json(HttpStatusCode.ServiceUnavailable, "{\"code\":503,\"isOk\":false,\"message\":\"try later\"}")
            : Json(HttpStatusCode.OK, "{\"data\":true,\"code\":200,\"isOk\":true}"));
        using var fin = NewClient(new FinClientOptions("test") { MaxRetries = 1 }, handler, out var delays);

        var deleted = await fin.Copy.Webhooks.DeleteAsync();

        Assert.True(deleted);
        Assert.Equal(2, calls);
        Assert.InRange(delays.Single(), TimeSpan.FromMilliseconds(187.5), TimeSpan.FromMilliseconds(312.5));
    }

    [Fact]
    public async Task NonJsonGatewayPageBecomesTypedException()
    {
        var handler = new QueueHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html><body>502 Bad Gateway</body></html>", Encoding.UTF8, "text/html")
        });
        using var fin = NewClient(new FinClientOptions("test") { MaxRetries = 0 }, handler);

        var exception = await Assert.ThrowsAsync<FinApiException>(() => fin.Copy.Accounts.ListAsync());

        Assert.Equal(502, exception.StatusCode);
        Assert.Contains("HTTP 502", exception.Message);
    }

    [Fact]
    public async Task TimeoutAfterRetriesBecomesTypedException()
    {
        var handler = new QueueHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Json(HttpStatusCode.OK, "{}");
        });
        using var fin = NewClient(new FinClientOptions("test") { MaxRetries = 1, Timeout = TimeSpan.FromMilliseconds(50) }, handler, out var delays);

        var exception = await Assert.ThrowsAsync<FinApiException>(() => fin.Copy.Accounts.ListAsync());

        Assert.Equal(0, exception.StatusCode);
        Assert.Equal("timeout", exception.ErrorCode);
        Assert.IsAssignableFrom<OperationCanceledException>(exception.InnerException);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Single(delays);
    }

    [Fact]
    public async Task CallerCancellationIsNotWrapped()
    {
        using var cancel = new CancellationTokenSource();
        var handler = new QueueHandler(async (_, token) =>
        {
            cancel.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            return Json(HttpStatusCode.OK, "{}");
        });
        using var fin = NewClient(new FinClientOptions("test"), handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fin.Copy.Accounts.ListAsync(cancellationToken: cancel.Token));
    }

    [Fact]
    public async Task NetworkFailureAfterRetriesBecomesTypedException()
    {
        var handler = new QueueHandler((_, _) => throw new HttpRequestException("connection refused"));
        using var fin = NewClient(new FinClientOptions("test") { MaxRetries = 2 }, handler, out var delays);

        var exception = await Assert.ThrowsAsync<FinApiException>(() => fin.Copy.Accounts.ListAsync());

        Assert.Equal(0, exception.StatusCode);
        Assert.Equal("network", exception.ErrorCode);
        Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(2, delays.Count);
    }

    [Fact]
    public async Task MissingDataIsAnEmptyResponseError()
    {
        var handler = new QueueHandler(_ => Json(HttpStatusCode.OK, "{\"code\":200,\"isOk\":true}"));
        using var fin = NewClient(new FinClientOptions("test"), handler);

        var exception = await Assert.ThrowsAsync<FinApiException>(() => fin.Copy.Webhooks.DeleteAsync());

        Assert.Equal("empty_response", exception.ErrorCode);
    }

    [Fact]
    public async Task GeneratesIdempotencyKeyForEveryWrite()
    {
        var handler = new QueueHandler(_ => Json(HttpStatusCode.OK, "{\"data\":true,\"code\":200,\"isOk\":true}"));
        using var fin = NewClient(new FinClientOptions("test"), handler);

        await fin.Copy.Webhooks.DeleteAsync();

        Assert.True(Guid.TryParseExact(handler.Requests[0].Headers.GetValues("Idempotency-Key").Single(), "N", out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\nkey")]
    public async Task RejectsAnInvalidIdempotencyKeyBeforeSending(string key)
    {
        var handler = new QueueHandler(_ => Json(HttpStatusCode.OK, "{\"data\":true,\"code\":200,\"isOk\":true}"));
        using var fin = NewClient(new FinClientOptions("test"), handler);

        await Assert.ThrowsAsync<ArgumentException>(() => fin.Copy.Webhooks.DeleteAsync(key));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RequestBodiesOmitUnsetFields()
    {
        var handler = new QueueHandler(_ => Json(HttpStatusCode.OK, "{\"data\":{},\"code\":200,\"isOk\":true}"));
        using var fin = NewClient(new FinClientOptions("test"), handler);

        await fin.Copy.Accounts.PatchPairAsync(1234, 7, new CmdApiFxsPairPatch { Enabled = true });

        Assert.Equal("{\"Enabled\":true}", handler.Bodies.Single());
    }

    [Fact]
    public async Task UnknownEnumValuesDoNotFailTheCall()
    {
        var handler = new QueueHandler(_ => Json(HttpStatusCode.OK,
            "{\"data\":{\"OID\":9,\"ACT\":\"SOME_NEW_ACTION\",\"ORD\":\"OPN\",\"CRSN\":\"SOME_NEW_REASON\",\"DIR\":\"buy\"},\"code\":200,\"isOk\":true}"));
        using var fin = NewClient(new FinClientOptions("test"), handler);

        var order = await fin.Copy.Accounts.OrderAsync(1234, 9);

        Assert.Equal(9, order.OID);
        Assert.Equal(FxsOrderStatus.OPN, order.ORD);
        Assert.Equal(FxsCloseReason.UNK, order.CRSN);
        Assert.Null(order.ACT);   // no Unknown member: a nullable property reads null, never an unrelated action such as NEW
        Assert.Equal(FxsSide.BUY, order.DIR);
    }

    [Fact]
    public async Task OptionsAreCopiedWhenTheClientIsCreated()
    {
        var handler = new QueueHandler(_ => Json(HttpStatusCode.OK, "{\"data\":[],\"code\":200,\"isOk\":true}"));
        var options = new FinClientOptions("first-key");
        using var fin = NewClient(options, handler);
        options.ApiKey = "changed-key";

        await fin.Copy.Accounts.ListAsync();

        Assert.Equal("first-key", handler.Requests[0].Headers.GetValues("X-Api-Key").Single());
    }

    [Fact]
    public async Task MapsApiErrorsToTypedException()
    {
        var handler = new QueueHandler(_ => Json(HttpStatusCode.BadRequest,
            "{\"code\":400,\"isOk\":false,\"message\":\"Invalid lot\",\"errors\":[\"LOT must be positive\",{\"field\":\"LOT\"}]}"));
        using var fin = NewClient(new FinClientOptions("test"), handler);

        var exception = await Assert.ThrowsAsync<FinApiException>(() => fin.Copy.Accounts.GetAsync(1234));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal(400, exception.Code);
        Assert.Equal("Invalid lot", exception.Message);
        Assert.Contains("LOT must be positive", exception.Errors);
        Assert.Contains("{\"field\":\"LOT\"}", exception.Errors);
        Assert.Null(exception.ErrorCode);
    }

    [Fact]
    public async Task ReadsEveryChangePageUsingTheReturnedCursor()
    {
        var handler = new QueueHandler(request => request.RequestUri!.Query.Contains("cursor=next", StringComparison.Ordinal)
            ? Json(HttpStatusCode.OK, "{\"data\":{\"Changes\":[{}],\"Cursor\":\"last\",\"More\":false},\"code\":200,\"isOk\":true}")
            : Json(HttpStatusCode.OK, "{\"data\":{\"Changes\":[{}],\"Cursor\":\"next\",\"More\":true},\"code\":200,\"isOk\":true}"));
        using var fin = NewClient(new FinClientOptions("test"), handler);

        var changes = new List<DtoApiChange>();
        await foreach (var change in fin.Copy.Changes.ReadAllAsync("start")) changes.Add(change);

        Assert.Equal(2, changes.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("cursor=next", handler.Requests[1].RequestUri!.Query);
    }

    [Fact]
    public async Task ReadsPagesSoTheCursorCanBeSaved()
    {
        var handler = new QueueHandler(request => request.RequestUri!.Query.Contains("cursor=next", StringComparison.Ordinal)
            ? Json(HttpStatusCode.OK, "{\"data\":{\"Changes\":[{},{}],\"Cursor\":\"last\",\"More\":false},\"code\":200,\"isOk\":true}")
            : Json(HttpStatusCode.OK, "{\"data\":{\"Changes\":[{}],\"Cursor\":\"next\",\"More\":true},\"code\":200,\"isOk\":true}"));
        using var fin = NewClient(new FinClientOptions("test"), handler);

        var pages = new List<DtoApiChangeFeed>();
        await foreach (var page in fin.Copy.Changes.ReadPagesAsync("start")) pages.Add(page);

        Assert.Equal(new[] { "next", "last" }, pages.Select(x => x.Cursor).ToArray());
        Assert.Equal(new[] { 1, 2 }, pages.Select(x => x.Changes!.Count).ToArray());
    }

    [Fact]
    public async Task StopsWhenTheFeedRepeatsACursor()
    {
        var handler = new QueueHandler(_ =>
            Json(HttpStatusCode.OK, "{\"data\":{\"Changes\":[],\"Cursor\":\"same\",\"More\":true},\"code\":200,\"isOk\":true}"));
        using var fin = NewClient(new FinClientOptions("test"), handler);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in fin.Copy.Changes.ReadPagesAsync("same")) { }
        });
    }

    [Fact]
    public void VerifiesTimestampedWebhookHmacAndRejectsBadInput()
    {
        const string secret = "example-secret";
        const string body = "{\"event\":\"ping\"}";
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var timestamp = now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hex = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(timestamp + "." + body))).ToLowerInvariant();
        var signature = "sha256=" + hex;
        var tampered = "sha256=" + (hex[0] == '0' ? '1' : '0') + hex.Substring(1);

        Assert.True(FinWebhook.Verify(secret, timestamp, body, signature, now));
        Assert.True(FinWebhook.Verify(secret, timestamp, body, "sha256=" + hex.ToUpperInvariant(), now));
        Assert.False(FinWebhook.Verify(secret, timestamp, body + " ", signature, now));
        Assert.False(FinWebhook.Verify(secret, timestamp, body, tampered, now));
        Assert.False(FinWebhook.Verify(secret, timestamp, body, "sha256=abc", now));
        Assert.False(FinWebhook.Verify(secret, timestamp, body, signature, now.AddMinutes(6)));
        Assert.False(FinWebhook.Verify(secret, timestamp, body, signature, now.AddMinutes(-6)));
        Assert.False(FinWebhook.Verify(secret, "not-a-number", body, signature, now));
    }

    [Fact]
    public void RegistersClientThroughHttpClientFactory()
    {
        var services = new ServiceCollection();
        services.AddFinClient(options => options.ApiKey = "test");
        using var provider = services.BuildServiceProvider();
        using var fin = provider.GetRequiredService<FinClient>();
        Assert.NotNull(fin.Copy.Accounts);
    }

    [Fact]
    public async Task CallerHttpClientCanUseTheMemberSessionWithoutAnApiKey()
    {
        var handler = new QueueHandler(_ => Json(HttpStatusCode.OK, "{\"data\":{\"State\":{\"ACNO\":1234}},\"code\":200,\"isOk\":true}"));
        using var http = new HttpClient(handler);
        using var fin = new FinClient(new FinClientOptions(), http);

        var account = await fin.Copy.Accounts.GetAsync(1234);

        Assert.Equal(1234, account.State!.ACNO);
        Assert.Equal("https://api.fincopy.pro/v1/fxs/accounts/1234", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.False(handler.Requests[0].Headers.Contains("X-Api-Key"));
    }

    private static FinClient NewClient(FinClientOptions options, HttpMessageHandler handler) => NewClient(options, handler, out _);

    private static FinClient NewClient(FinClientOptions options, HttpMessageHandler handler, out List<TimeSpan> delays)
    {
        var recorded = new List<TimeSpan>();
        var fin = new FinClient(options, handler);
        fin.Transport.Delay = (delay, _) =>
        {
            recorded.Add(delay);
            return Task.CompletedTask;
        };
        delays = recorded;
        return fin;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public QueueHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = (request, _) => Task.FromResult(respond(request));
        public QueueHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) => _respond = respond;

        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string?> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(Clone(request));
            Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            return await _respond(request, cancellationToken);
        }

        private static HttpRequestMessage Clone(HttpRequestMessage source)
        {
            var copy = new HttpRequestMessage(source.Method, source.RequestUri);
            foreach (var header in source.Headers) copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            return copy;
        }
    }
}
