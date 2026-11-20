# FinClient for .NET

FinClient is the .NET SDK for the Fin APIs: the [FinCopy public API](https://fincopy.io/docs) (default base URL
`https://api.fincopy.io`), FinGraph, FinNews and FinSocial. The FinCopy sections come first;
[FinGraph, FinNews and FinSocial](#fingraph-finnews-and-finsocial) follow them. Since version 0.2.0 FinCopy uses numeric ACNO
paths under `/v1/fxs`, which changed the 0.1 API; see the changelog before upgrading. It targets .NET Standard 2.0 and .NET 8.

```sh
dotnet add package FinClient
```

```csharp
using Fin;
using Fin.Copy;

using var fin = new FinClient(new FinClientOptions(Environment.GetEnvironmentVariable("FINCOPY_API_KEY")!));
var accounts = await fin.Copy.Accounts.ListAsync();
foreach (var account in accounts.Items)
    Console.WriteLine($"{account.Fleet?.Account?.ACNO} · {account.Fleet?.Account?.Login}");

// The account number is in the path, not in the trade body.
// await fin.Copy.Orders.MakeAsync(52014125, new CmdApiFxsOrdrMake
// {
//     SYM = "EURUSD", ACT = FxsOrderAction.NEW, OTY = FxsOrderType.OP_BUY, LOT = 0.01,
// });
```

## Member session

Pass a caller-owned `HttpClient` configured to send the signed-in member's `fincopy_member` session cookie, and omit the
API key. Set `BaseUrl` when the API is not at the default address:

```csharp
using var fin = new FinClient(new FinClientOptions { BaseUrl = apiRoot }, authenticatedHttpClient);
var account = await fin.Copy.Accounts.GetAsync(52014125);
```

The caller configures and disposes that `HttpClient`. Session trade requests are checked against the member's trade-PIN
gate by the API. API keys remain for trusted server-side clients; never ship them in browser or mobile code.

## Resources

- `Copy.Accounts`: paged accounts, creation, settings, pairs, symbols and quotes, orders, history, DCA, performance,
  activity, strategy preview and apply, and terminal operations (start, stop, restart, disconnect).
- `Copy.Orders`: trade preflight, trade, hedge, modify, cancel and DCA answers.
- `Copy.Fx`: lists across every account you can reach (pairs, orders, history, activity, DCA, terminal operations,
  performance window and bars), strategies (list, read, create, change, delete), signals, alerts, brokers and the event
  stream.
- `Copy.Changes` and `Copy.Webhooks`: change feed and webhook configuration/test delivery.

Paged methods return `FinPage<T>` with `Items` and paging metadata. Generated DTO properties retain PascalCase names.
Trade writes return when queued for the terminal; observe completion through the stream, change feed or webhook.
`Copy.Accounts.RemoveAsync` sends `DELETE /v1/fxs/accounts/{acno}` and disconnects the hosted terminal: it stops and
removes the terminal, forgets the broker password and frees the seat. Customer account settings and trade history remain;
a free demo's account and terminal records are deleted. Open orders stay at the broker, and virtual TP, SL and TS stop
working while no terminal runs. For a member session with a trading PIN, pass `tradingPin`:

```csharp
await fin.Copy.Accounts.RemoveAsync(acno, idempotencyKey: requestKey, tradingPin: tradingPin);
```

API-key requests do not use a session PIN. The method returns an operation id; retry with the same idempotency key to
receive the same operation.

API calls throw `FinApiException` with HTTP status, API code, message, validation errors and `RetryAfter` when present, and
an `ErrorCode` of `timeout`, `network`, `rate_limited`, `invalid_response` or `empty_response` when the SDK set one. The
client retries rate limits and transient network/gateway errors while reusing each write's `Idempotency-Key`.

`Copy.Fx.ReadStreamAsync` does not reconnect. It ends when the server closes it (about every 30 minutes); a broken
connection throws `FinApiException` with `ErrorCode` "network". Open it again in a loop, waiting a few seconds between
attempts. Cancelling the token ends it on every target framework.

## FinGraph, FinNews and FinSocial

FinGraph (market data, tickers, signals and statistics), FinNews (news, sentiment, trending, sources and videos) and FinSocial
(prediction markets, social posts, authors and videos) sit beside FinCopy in the same client. Each has its own API key, sent
in the `FinUsrKey` header (FinCopy's stays `X-Api-Key`), and its own address:

```csharp
using Fin;
using Fin.Graph;
using Fin.News;
using Fin.Social;

using var fin = new FinClient(new FinClientOptions
{
    Graph = new FinServiceOptions(Environment.GetEnvironmentVariable("FINGRAPH_API_KEY")!),   // https://x.fingraph.io
    News = new FinServiceOptions(Environment.GetEnvironmentVariable("FINNEWS_API_KEY")!),     // https://x.finnews.io
    Social = new FinServiceOptions(Environment.GetEnvironmentVariable("FINSOCIAL_API_KEY")!), // https://x.finsocial.io
    // ApiKey = Environment.GetEnvironmentVariable("FINCOPY_API_KEY")!,                       // FinCopy, as before
});
```

Give a block only for the services you use; set `BaseUrl` in a block to use another address. `ApiKey` is optional when a block
is given. A service whose key is missing throws an `InvalidOperationException` that names the setting when you first call it,
before anything is sent. `Timeout`, `MaxRetries` and the `HttpClient` are shared by every service, and the `AddFinClient`
registration takes the same blocks. Keep keys on the server.

### Two surfaces: `/api` and `/v1`

Every operation in each service's OpenAPI document is a method. The two surfaces are billed differently, so they are kept apart:

| | `/api` operations | `/v1` operations |
| --- | --- | --- |
| Billing | Charged in credits per call, on any plan. | Included in paid plans, gated by package tier. |
| Where | `fin.Graph.Tickers.ListAsync()` | `fin.Graph.V1.Tickers.ListAsync()` |

| Service | `/api` groups | `/v1` groups |
| --- | --- | --- |
| `fin.Graph` | ApcDat, Assets, DaxDat, FxsTikr, KlsePrc, Ohlcv, SigCry, SigDax, SigEqt, SigFxs, Signals, Stats, Tickers, TvdDat | Assets, Ohlcv, Quotes, Signals, Stats, Tickers |
| `fin.News` | Categories, Entities, News, Sentiment, Sources, Trending, Videos | Entities, News, Sentiment, Sources, Trending |
| `fin.Social` | Markets, Social, Videos | Markets, Social, Videos |

The group is the operation's tag and the method is the action part of its operation id plus `Async` (`Api_NewsController_RatingMake`
is `fin.News.News.RatingMakeAsync()`; `Pub_TickersController_Get` is `fin.Graph.V1.Tickers.GetAsync()`). If two operations of one
group on one surface would get the same name, the later one in path order is named with its HTTP method in front, then its last
path segment; no operation needs that today. Each method's XML documentation gives what it does, its route, and its cost in
credits (`/api`) or the package tiers it is open to (`/v1`).

Arguments run in the order of the request: path parameters (required, positional), the body, required query values, then the
rest of the query as optional named arguments, an `idempotencyKey` for writes, and the `CancellationToken`:

```csharp
var detail = await fin.Graph.Tickers.ViewAsync("BTCUSDT");                                                // GET /api/tickers/BTCUSDT
var some = await fin.Graph.V1.Tickers.ListAsync(dax: DAX.BIN, quote: "USDT", limit: 20);                  // GET /v1/tickers?dax=BIN&...
await fin.News.News.RatingMakeAsync("<checksum>", new CmdApiRatingMake { ReqSite = "mysite" }, requestKey); // POST with a JSON body
```

A method returns the documented 200 reply as it comes (its `Data`, `Paging` and the rest), as the generated type for that
service's namespace. One route family is typed loosely: the document names an error body as the reply of `/v1/ohlcv/...`, so
those methods return a `JsonElement`.

### The `qry` object and paging

Most FinNews and FinSocial lists take one query parameter, `qry`, whose value is a JSON object: `PageNo`, `PageSize`, `SortBy`
(pairs of field and `asc` or `desc`), `SrchBy`, and the endpoint's own filter fields. The method takes that object as a
generated class (`QueApiNewsList`, `QueApiSocPostList`, ...) and sends it as JSON, leaving out what you did not set:

```csharp
var page = await fin.News.News.ListAsync(new QueApiNewsList
{
    PageNo = 1,
    PageSize = 20,
    SortBy = [["published", "desc"]],
    Symbols = "AAPL,MSFT", // a filter field of this endpoint
});
Console.WriteLine($"{page.Paging?.ResCount} {page.Data?.FirstOrDefault()?.Title}");
```

Every list that pages also has `...PagesAsync()` and `...ItemsAsync()` methods that return `IAsyncEnumerable` (`ListPagesAsync`,
`ListItemsAsync`, `PostListItemsAsync`, ...). They advance `PageNo` (for `qry` lists) or `offset` (FinGraph's `limit` and `offset`
lists), and stop on an empty page, which they do not yield, or when the paging information says it was the last page. They never
change the query you pass.

```csharp
await foreach (var article in fin.News.News.ListItemsAsync(new QueApiNewsList { PageSize = 100, Symbols = "AAPL" }))
    Console.WriteLine(article.Title);
await foreach (var page in fin.Graph.V1.Tickers.ListPagesAsync(dax: DAX.BIN, limit: 200))
    Console.WriteLine(page.Count);
```

### Refusals

They throw `FinApiException` with `StatusCode`, the API's machine-readable `Error` code and its `Message`, plus `Lane`, `Limit`,
`Catag` and `Tier` when the refusal is about a limit: `401` `invalid_api_key`, `402` `insufficient_credits` (or a credit budget
or call limit), `403` `package_not_allowed` (the key's package may not call the endpoint), `429` for a rate limit. A `429` is
retried after the server's `Retry-After` (up to 60 seconds); when it is longer, the exception carries `RetryAfter`.

See the examples ([FinCopy](./examples/QuickStart), [FinGraph](./examples/GraphQuickStart), [FinNews](./examples/NewsQuickStart),
[FinSocial](./examples/SocialQuickStart)), the [FinCopy API docs](https://fincopy.io/docs), and the
[repository](https://github.com/bizjaya/FinClient).
