using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Fin;
using Fin.Graph;
using Fin.News;
using Fin.Social;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fin.Tests;

/// <summary>FinGraph, FinNews and FinSocial: keys, routes, queries, bodies, refusals and paging.</summary>
public sealed class ServiceTests
{
    // ---- keys and addresses

    [Fact]
    public async Task EachServiceSendsItsOwnKeyInFinUsrKeyToItsOwnDefaultAddress()
    {
        var handler = new Recorder(_ => Json(HttpStatusCode.OK, "{\"data\":[],\"code\":200,\"isOk\":true}"));
        using var fin = NewClient(AllServices(), handler);

        await fin.Graph.Assets.ExchangesAsync();
        await fin.News.Categories.ListAsync();
        await fin.Social.Videos.VideosAsync();

        Assert.Equal(["https://x.fingraph.io", "https://x.finnews.io", "https://x.finsocial.io"],
            handler.Requests.Select(r => r.RequestUri!.GetLeftPart(UriPartial.Authority)));
        Assert.Equal(["graph-key", "news-key", "social-key"], handler.Requests.Select(r => r.Headers.GetValues("FinUsrKey").Single()));
        Assert.All(handler.Requests, r => Assert.False(r.Headers.Contains("X-Api-Key")));
    }

    [Fact]
    public async Task AServiceBlockCanSetItsOwnBaseUrlWhileFinCopyKeepsXApiKey()
    {
        var handler = new Recorder(_ => Json(HttpStatusCode.OK, "{\"data\":[],\"code\":200,\"isOk\":true}"));
        using var fin = NewClient(new FinClientOptions("copy-key")
        {
            Graph = new FinServiceOptions("g") { BaseUrl = "https://graph.example.test/" }
        }, handler);

        await fin.Graph.Assets.ExchangesAsync();
        await fin.Copy.Accounts.ListAsync();

        Assert.Equal("https://graph.example.test/api/assets/exchanges", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Equal("g", handler.Requests[0].Headers.GetValues("FinUsrKey").Single());
        Assert.Equal("https://api.fincopy.io", handler.Requests[1].RequestUri!.GetLeftPart(UriPartial.Authority));
        Assert.Equal("copy-key", handler.Requests[1].Headers.GetValues("X-Api-Key").Single());
        Assert.False(handler.Requests[1].Headers.Contains("FinUsrKey"));
    }

    [Fact]
    public async Task ABlockLetsTheClientBuildWithoutFinCopysKeyAndFinCopyFailsOnlyWhenFirstUsed()
    {
        var handler = new Recorder(_ => Json(HttpStatusCode.OK, "{\"data\":[],\"code\":200,\"isOk\":true}"));
        using var fin = NewClient(new FinClientOptions { Graph = new FinServiceOptions("g") }, handler);

        await fin.Graph.Assets.ExchangesAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fin.Copy.Accounts.ListAsync());

        Assert.Contains("FinCopy needs an API key", error.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void AClientWithNoKeyAndNoServiceBlockIsStillRefused()
    {
        Assert.Throws<ArgumentException>(() => new FinClient(new FinClientOptions()));
    }

    [Fact]
    public async Task APartWhoseKeyIsMissingThrowsAClearErrorAtFirstUseAndSendsNothing()
    {
        var handler = new Recorder(_ => Json(HttpStatusCode.OK, "{\"data\":[],\"code\":200,\"isOk\":true}"));
        using var fin = NewClient(new FinClientOptions("copy-key") { News = new FinServiceOptions("n") }, handler);

        var graph = await Assert.ThrowsAsync<InvalidOperationException>(() => fin.Graph.Assets.ExchangesAsync());
        var social = await Assert.ThrowsAsync<InvalidOperationException>(() => fin.Social.Pulse.PulseAsync());

        Assert.Contains("FinGraph needs an API key", graph.Message);
        Assert.Contains("FinClientOptions.Graph.ApiKey", graph.Message);
        Assert.Contains("FinSocial needs an API key", social.Message);
        Assert.Empty(handler.Requests);
        await fin.News.Categories.ListAsync();
    }

    [Fact]
    public async Task ABlankKeyInABlockCountsAsMissing()
    {
        var handler = new Recorder(_ => Json(HttpStatusCode.OK, "{}"));
        using var fin = NewClient(new FinClientOptions { Graph = new FinServiceOptions("  ") }, handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fin.Graph.Assets.ExchangesAsync());
    }

    [Fact]
    public void ABadServiceBaseUrlIsRefusedWhenTheClientIsCreated()
    {
        var options = new FinClientOptions { News = new FinServiceOptions("n") { BaseUrl = "not a url" } };
        Assert.Throws<ArgumentException>(() => new FinClient(options));
    }

    [Fact]
    public async Task TheSharedRetrySettingsApplyToEveryService()
    {
        var calls = 0;
        var handler = new Recorder(_ => ++calls < 3
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : Json(HttpStatusCode.OK, "{\"data\":[],\"isOk\":true}"));
        using var fin = NewClient(new FinClientOptions { News = new FinServiceOptions("n"), MaxRetries = 2 }, handler);

        await fin.News.Categories.ListAsync();

        Assert.Equal(3, calls);
    }

    [Fact]
    public void TheDependencyInjectionRegistrationAcceptsServiceBlocks()
    {
        var services = new ServiceCollection();
        services.AddFinClient(o => o.News = new FinServiceOptions("n"));
        using var provider = services.BuildServiceProvider();

        var fin = provider.GetRequiredService<FinClient>();

        Assert.NotNull(fin.News);
    }

    // ---- the two surfaces

    [Fact]
    public async Task ApiOperationsAreOnTheGroupsAndV1OperationsAreUnderV1()
    {
        var handler = new Recorder(_ => Json(HttpStatusCode.OK, "{\"isOk\":true}"));
        using var fin = NewClient(AllServices(), handler);

        await fin.Graph.Tickers.ListAsync();
        await fin.Graph.V1.Tickers.ListAsync();
        await fin.News.Sources.ListAsync();
        await fin.News.V1.Sources.ListAsync();
        await fin.Social.Pulse.PulseAsync();
        await fin.Social.V1.Pulse.PulseAsync();

        Assert.Equal(["/api/tickers", "/v1/tickers", "/api/sources", "/v1/sources", "/api/pulse", "/v1/pulse"],
            handler.Requests.Select(r => r.RequestUri!.AbsolutePath));
        Assert.All(handler.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));
    }

    [Fact]
    public async Task PathArgumentsGoInThePathEncoded()
    {
        var handler = new Recorder(_ => Json(HttpStatusCode.OK, "{\"isOk\":true}"));
        using var fin = NewClient(AllServices(), handler);

        await fin.Graph.Tickers.ViewAsync("BTC/USDT");
        await fin.Graph.V1.Assets.GetAsync("a b");
        await fin.News.News.ViewAsync("abc123");
        await fin.Social.Markets.MarketAsync("m-1", days: 5);
        await fin.Graph.Screens.EqtUsaAsync("top gain", page: 2);

        Assert.Equal(["/api/tickers/BTC%2FUSDT", "/v1/assets/a%20b", "/api/news/abc123", "/api/markets/m-1?days=5", "/api/screens/eqtusa/top%20gain?page=2"],
            handler.Requests.Select(r => r.RequestUri!.PathAndQuery));
    }

    [Fact]
    public async Task AnEmptyPathArgumentIsRefusedBeforeAnyCall()
    {
        var handler = new Recorder(_ => Json(HttpStatusCode.OK, "{}"));
        using var fin = NewClient(AllServices(), handler);

        await Assert.ThrowsAsync<ArgumentException>(() => fin.Graph.Tickers.ViewAsync(""));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task NamedQueryFieldsAreSentUnderTheirDocumentedNamesWithUnsetOnesLeftOutAndDatesInUtc()
    {
        var handler = new Recorder(_ => Json(HttpStatusCode.OK, "{\"isOk\":true}"));
        using var fin = NewClient(AllServices(), handler);

        await fin.Graph.Assets.ListAsync(exchange: "BINANCE", canTrade: false, size: 25);
        await fin.Graph.Ohlcv.ViewAsync("BTCUSDT", timeframe: "1d", from: "2026-11-01T00:00:00Z", limit: 10);
        await fin.News.News.ListAsync(since: new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.FromHours(8)));
        await fin.Graph.Markets.EqtUsaAsync(syms: "NVDA,MSFT", sort: "-chg1d");

        Assert.Equal("?exchange=BINANCE&canTrade=false&size=25", handler.Requests[0].RequestUri!.Query);
        Assert.Equal("?timeframe=1d&from=2026-11-01T00%3A00%3A00Z&limit=10", handler.Requests[1].RequestUri!.Query);
        Assert.Equal("?since=2026-11-01T00%3A00%3A00.0000000%2B00%3A00", handler.Requests[2].RequestUri!.Query);
        Assert.Equal("/api/markets/eqtusa", handler.Requests[3].RequestUri!.AbsolutePath);
        Assert.Equal("NVDA,MSFT", Query(handler.Requests[3], "syms"));
        Assert.Equal("-chg1d", Query(handler.Requests[3], "sort"));
    }

    [Fact]
    public async Task ListFiltersAreSentAsPlainQueryValuesAndNoQryParameter()
    {
        var handler = new Recorder(_ => Json(HttpStatusCode.OK, "{\"data\":[],\"paging\":{\"pageNo\":1,\"pageCount\":1},\"isOk\":true}"));
        using var fin = NewClient(AllServices(), handler);

        await fin.News.News.ListAsync(syms: "AAPL,MSFT", sort: "-published", page: 2, size: 10);
        await fin.Social.V1.Posts.PostsAsync(syms: "BTC", stance: "bull");

        Assert.Equal("/api/news", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("?syms=AAPL%2CMSFT&sort=-published&page=2&size=10", handler.Requests[0].RequestUri!.Query);
        Assert.Equal("/v1/posts", handler.Requests[1].RequestUri!.AbsolutePath);
        Assert.Null(Query(handler.Requests[1], "qry"));
        Assert.Equal("bull", Query(handler.Requests[1], "stance"));
    }

    [Fact]
    public async Task TheWholeDocumentedReplyIsReturnedNotJustItsData()
    {
        var handler = new Recorder(_ => Json(HttpStatusCode.OK,
            "{\"data\":[{\"Id\":\"c1\"}],\"paging\":{\"pageNo\":1,\"pageSize\":10,\"pageCount\":1,\"resCount\":1},\"code\":200,\"isOk\":true,\"errors\":[]}"));
        using var fin = NewClient(AllServices(), handler);

        var reply = await fin.News.News.ListAsync();

        Assert.Equal("c1", reply.Data!.Single().Id);
        Assert.Equal(1, reply.Paging!.ResCount);
        Assert.True(reply.IsOk);
    }

    [Fact]
    public async Task AV1ReplyComesBackAsItIsWithTheSymbolsALookupDidNotFind()
    {
        var handler = new Recorder(_ => Json(HttpStatusCode.OK,
            "{\"data\":[{\"Sym\":\"A\"}],\"NotFound\":[\"NOPE\"],\"paging\":{\"pageNo\":1,\"pageSize\":50,\"pageCount\":1,\"resCount\":1},\"isOk\":true}"));
        using var fin = NewClient(AllServices(), handler);

        var reply = await fin.Graph.V1.Markets.EqtUsaAsync(syms: "A,NOPE");

        Assert.Equal("A", reply.Data!.Single().Sym);
        Assert.Equal(["NOPE"], reply.NotFound);
        Assert.Equal(1, reply.Paging!.ResCount);
    }

    [Fact]
    public async Task ARouteWhoseDocumentGivesNoUsableReplyTypeReturnsTheJson()
    {
        var handler = new Recorder(_ => Json(HttpStatusCode.OK, "{\"data\":{\"candles\":[1,2]}}"));
        using var fin = NewClient(AllServices(), handler);

        var reply = await fin.Graph.V1.Ohlcv.GetAsync("BTCUSDT");

        Assert.Equal(2, reply.GetProperty("data").GetProperty("candles").GetArrayLength());
    }

    // ---- refusals

    private static async Task<FinApiException> Refused(HttpStatusCode status, string body, TimeSpan? retryAfter = null, int retries = 0)
    {
        var handler = new Recorder(_ =>
        {
            var response = Json(status, body);
            if (retryAfter is { } wait) response.Headers.RetryAfter = new RetryConditionHeaderValue(wait);
            return response;
        });
        using var fin = NewClient(new FinClientOptions { Graph = new FinServiceOptions("g"), MaxRetries = retries }, handler);
        return await Assert.ThrowsAsync<FinApiException>(() => fin.Graph.Tickers.ListAsync());
    }

    [Fact]
    public async Task A401CarriesTheApiErrorCodeAndMessage()
    {
        var error = await Refused(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_api_key\",\"message\":\"The key is unknown.\"}");

        Assert.Equal(401, error.StatusCode);
        Assert.Equal("invalid_api_key", error.Error);
        Assert.Equal("The key is unknown.", error.Message);
        Assert.Null(error.ErrorCode);
    }

    [Fact]
    public async Task A402CarriesTheLimitDetails()
    {
        var error = await Refused(HttpStatusCode.PaymentRequired,
            "{\"error\":\"insufficient_credits\",\"message\":\"Not enough credits.\",\"lane\":\"CreditBudget\",\"limit\":100}");

        Assert.Equal(402, error.StatusCode);
        Assert.Equal("insufficient_credits", error.Error);
        Assert.Equal("CreditBudget", error.Lane);
        Assert.Equal(100, error.Limit!.Value.GetInt32());
        Assert.Null(error.Catag);
        Assert.Null(error.Tier);
    }

    [Fact]
    public async Task A403CarriesTheCategoryAndTier()
    {
        var error = await Refused(HttpStatusCode.Forbidden,
            "{\"error\":\"package_not_allowed\",\"message\":\"Upgrade.\",\"catag\":\"SIGDAX\",\"tier\":\"1\",\"lane\":\"Tier\",\"limit\":\"2\"}");

        Assert.Equal(403, error.StatusCode);
        Assert.Equal("package_not_allowed", error.Error);
        Assert.Equal("SIGDAX", error.Catag);
        Assert.Equal("1", error.Tier);
        Assert.Equal("2", error.Limit!.Value.GetString());
    }

    [Fact]
    public async Task A429ThatIsTooLongToWaitOutReportsRetryAfter()
    {
        var error = await Refused((HttpStatusCode)429, "{\"error\":\"rate_limited\",\"message\":\"Slow down.\"}", TimeSpan.FromSeconds(120));

        Assert.Equal(429, error.StatusCode);
        Assert.Equal("rate_limited", error.ErrorCode);
        Assert.Equal(TimeSpan.FromSeconds(120), error.RetryAfter);
        Assert.Equal("rate_limited", error.Error);
    }

    [Fact]
    public async Task A429IsRetriedAfterTheRetryAfterTheServerGaveThenSucceeds()
    {
        var calls = 0;
        var handler = new Recorder(_ =>
        {
            if (++calls > 1) return Json(HttpStatusCode.OK, "{\"data\":[],\"isOk\":true}");
            var response = Json((HttpStatusCode)429, "{\"error\":\"rate_limited\",\"message\":\"Slow down.\"}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(3));
            return response;
        });
        using var fin = NewClient(new FinClientOptions { Graph = new FinServiceOptions("g") }, handler, out var delays);

        await fin.Graph.Tickers.ListAsync();

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(3)], delays);
    }

    [Fact]
    public async Task AV1ErrorBodyIsMapped()
    {
        var error = await Refused(HttpStatusCode.NotImplemented, "{\"error\":{\"code\":\"not_implemented\",\"message\":\"Not available yet.\"}}");

        Assert.Equal(501, error.StatusCode);
        Assert.Equal("not_implemented", error.Error);
        Assert.Equal("Not available yet.", error.Message);
    }

    [Fact]
    public async Task AnEnvelopeThatSaysItFailedOnHttp200IsAnError()
    {
        var handler = new Recorder(_ => Json(HttpStatusCode.OK, "{\"isOk\":false,\"code\":404,\"message\":\"No such ticker.\",\"errors\":[\"symbol\"]}"));
        using var fin = NewClient(AllServices(), handler);

        var error = await Assert.ThrowsAsync<FinApiException>(() => fin.Graph.Tickers.ViewAsync("NOPE"));

        Assert.Equal("No such ticker.", error.Message);
        Assert.Equal(404, error.Code);
        Assert.Equal(["symbol"], error.Errors);
    }

    [Fact]
    public async Task AnUnreadableErrorBodyGetsAPlainMessageNamingTheService()
    {
        var error = await Refused(HttpStatusCode.InternalServerError, "<html>bad gateway</html>");

        Assert.Equal(500, error.StatusCode);
        Assert.Equal("The FinGraph API request failed with HTTP 500 (INTERNALSERVERERROR).", error.Message);
    }

    [Fact]
    public async Task ANetworkFailureAndAMalformed200NameTheService()
    {
        var down = new Recorder((_, _) => throw new HttpRequestException("offline"));
        using (var fin = NewClient(new FinClientOptions { News = new FinServiceOptions("n"), MaxRetries = 0 }, down))
        {
            var error = await Assert.ThrowsAsync<FinApiException>(() => fin.News.Categories.ListAsync());
            Assert.Equal("network", error.ErrorCode);
            Assert.Equal("The FinNews API could not be reached.", error.Message);
        }

        var garbled = new Recorder(_ => Json(HttpStatusCode.OK, "not json"));
        using (var fin = NewClient(new FinClientOptions { News = new FinServiceOptions("n") }, garbled))
        {
            var error = await Assert.ThrowsAsync<FinApiException>(() => fin.News.Categories.ListAsync());
            Assert.Equal("invalid_response", error.ErrorCode);
        }
    }

    [Fact]
    public async Task TheCallersOwnCancellationIsNotWrapped()
    {
        var handler = new Recorder(_ => Json(HttpStatusCode.OK, "{\"data\":[],\"isOk\":true}"));
        using var fin = NewClient(AllServices(), handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fin.Graph.Tickers.ListAsync(cancellationToken: cts.Token));
    }

    // ---- paging

    private static string Paged(int pageNo, int pageCount, params int[] ids) =>
        "{\"data\":[" + string.Join(",", ids.Select(i => "{\"Id\":\"c" + i + "\"}")) + "],\"paging\":{\"pageNo\":" + pageNo
        + ",\"pageSize\":2,\"pageCount\":" + pageCount + ",\"resCount\":5},\"code\":200,\"isOk\":true}";

    [Fact]
    public async Task APageNumberedListIsWalkedPageByPageAndStopsAfterTheLastPage()
    {
        var replies = new Queue<string>([Paged(1, 3, 1, 2), Paged(2, 3, 3, 4), Paged(3, 3, 5)]);
        var handler = new Recorder(_ => Json(HttpStatusCode.OK, replies.Dequeue()));
        using var fin = NewClient(AllServices(), handler);

        var counts = new List<int>();
        await foreach (var page in fin.News.News.ListPagesAsync(size: 2, syms: "AAPL"))
            counts.Add(page.Data!.Count);

        Assert.Equal([2, 2, 1], counts);
        Assert.Equal(["1", "2", "3"], handler.Requests.Select(r => Query(r, "page")));
        Assert.All(handler.Requests, r => { Assert.Equal("2", Query(r, "size")); Assert.Equal("AAPL", Query(r, "syms")); });
    }

    [Fact]
    public async Task EveryItemIsWalkedStartingFromThePageTheQueryNames()
    {
        var replies = new Queue<string>([Paged(2, 3, 3, 4), Paged(3, 3, 5)]);
        var handler = new Recorder(_ => Json(HttpStatusCode.OK, replies.Dequeue()));
        using var fin = NewClient(AllServices(), handler);

        var ids = new List<string?>();
        await foreach (var item in fin.News.News.ListItemsAsync(page: 2, size: 2)) ids.Add(item.Id);

        Assert.Equal(["c3", "c4", "c5"], ids);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task AnEmptyPageEndsTheWalkWhenThePagingBlockDoesNotSayItWasTheLastAndIsNotYielded()
    {
        var replies = new Queue<string>(["{\"data\":[{}],\"paging\":{\"pageNo\":1},\"isOk\":true}", "{\"data\":[],\"paging\":{\"pageNo\":2},\"isOk\":true}"]);
        var handler = new Recorder(_ => Json(HttpStatusCode.OK, replies.Dequeue()));
        using var fin = NewClient(AllServices(), handler);

        var pages = 0;
        await foreach (var _ in fin.Social.Markets.MarketsPagesAsync()) pages++;

        Assert.Equal(1, pages);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task AnEmptyListYieldsNothingAfterOneCall()
    {
        var handler = new Recorder(_ => Json(HttpStatusCode.OK, "{\"data\":[],\"paging\":{\"pageNo\":1,\"pageCount\":0},\"isOk\":true}"));
        using var fin = NewClient(AllServices(), handler);

        var count = 0;
        await foreach (var _ in fin.News.Videos.ListItemsAsync(size: 5)) count++;

        Assert.Equal(0, count);
        Assert.Single(handler.Requests);
        Assert.Equal("5", Query(handler.Requests[0], "size"));
    }

    [Fact]
    public async Task APathAddressedListIsWalkedWithItsPathArgument()
    {
        var replies = new Queue<string>([Paged(1, 2, 1, 2), Paged(2, 2, 3)]);
        var handler = new Recorder(_ => Json(HttpStatusCode.OK, replies.Dequeue()));
        using var fin = NewClient(AllServices(), handler);

        var count = 0;
        await foreach (var _ in fin.Graph.Screens.CryptoItemsAsync("top gain", size: 2)) count++;

        Assert.Equal(3, count);
        Assert.All(handler.Requests, r => Assert.Equal("/api/screens/crypto/top%20gain", r.RequestUri!.AbsolutePath));
        Assert.Equal(["1", "2"], handler.Requests.Select(r => Query(r, "page")));
    }

    [Fact]
    public async Task ARefusalInTheMiddleOfAWalkSurfaces()
    {
        var calls = 0;
        var handler = new Recorder(_ => ++calls == 1
            ? Json(HttpStatusCode.OK, Paged(1, 3, 1, 2))
            : Json(HttpStatusCode.PaymentRequired, "{\"error\":\"insufficient_credits\",\"message\":\"No credits.\"}"));
        using var fin = NewClient(new FinClientOptions { News = new FinServiceOptions("n"), MaxRetries = 0 }, handler);

        var seen = 0;
        var error = await Assert.ThrowsAsync<FinApiException>(async () =>
        {
            await foreach (var page in fin.News.News.ListPagesAsync()) seen += page.Data!.Count;
        });

        Assert.Equal(2, seen);
        Assert.Equal(402, error.StatusCode);
        Assert.Equal("insufficient_credits", error.Error);
    }

    [Fact]
    public async Task CancellingAWalkStopsIt()
    {
        var handler = new Recorder(_ => Json(HttpStatusCode.OK, Paged(1, 9, 1, 2)));
        using var fin = NewClient(AllServices(), handler);
        using var cts = new CancellationTokenSource();

        var pages = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in fin.News.News.ListPagesAsync(cancellationToken: cts.Token))
            {
                pages++;
                cts.Cancel();
            }
        });

        Assert.Equal(1, pages);
    }

    // ---- helpers

    private static FinClientOptions AllServices() => new("copy-key")
    {
        Graph = new FinServiceOptions("graph-key"),
        News = new FinServiceOptions("news-key"),
        Social = new FinServiceOptions("social-key"),
    };

    private static string? Query(HttpRequestMessage request, string name)
    {
        foreach (var pair in request.RequestUri!.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (Uri.UnescapeDataString(parts[0]) == name) return Uri.UnescapeDataString(parts[1]);
        }
        return null;
    }

    private static FinClient NewClient(FinClientOptions options, HttpMessageHandler handler) => NewClient(options, handler, out _);

    private static FinClient NewClient(FinClientOptions options, HttpMessageHandler handler, out List<TimeSpan> delays)
    {
        var recorded = new List<TimeSpan>();
        var fin = new FinClient(options, handler);
        fin.SetDelay((delay, _) =>
        {
            recorded.Add(delay);
            return Task.CompletedTask;
        });
        delays = recorded;
        return fin;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Recorder : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public Recorder(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = (request, _) => Task.FromResult(respond(request));
        public Recorder(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) => _respond = respond;

        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string?> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var copy = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers) copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            Requests.Add(copy);
            Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            return await _respond(request, cancellationToken);
        }
    }
}
