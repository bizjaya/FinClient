# FinClient

FinClient provides .NET and TypeScript clients for the Fin APIs: FinCopy (`https://api.fincopy.io`), FinGraph, FinNews and
FinSocial. The FinCopy sections come first; [FinGraph, FinNews and FinSocial](#fingraph-finnews-and-finsocial) follow them. Since
version 0.2.0 FinCopy uses the `/v1/fxs` trading routes and identifies accounts by their numeric ACNO, which changed the 0.1 API;
see [CHANGELOG.md](./CHANGELOG.md) before upgrading.

## .NET

```csharp
using Fin;
using Fin.Copy;

using var fin = new FinClient(new FinClientOptions(Environment.GetEnvironmentVariable("FINCOPY_API_KEY")!));
var page = await fin.Copy.Accounts.ListAsync();
foreach (var account in page.Items)
    Console.WriteLine($"{account.Fleet?.Account?.ACNO}: {account.Fleet?.Account?.Login}");

// ACNO is in the URL; request bodies do not contain an account id.
// await fin.Copy.Orders.MakeAsync(52014125, new CmdApiFxsOrdrMake
// {
//     SYM = "EURUSD", ACT = FxsOrderAction.NEW, OTY = FxsOrderType.OP_BUY, LOT = 0.01,
// });
```

## TypeScript

```ts
import { FinClient } from "@bizjaya/finclient";

const fin = new FinClient({ apiKey: process.env.FINCOPY_API_KEY! });
const page = await fin.copy.accounts.list();
for (const account of page.items) console.log(account.Fleet?.Account?.ACNO);

// await fin.copy.orders.make(52014125, { SYM: "EURUSD", ACT: "NEW", OTY: "OP_BUY", LOT: 0.01 });
```

## Python

```python
import os
from finclient import FinClient, ServiceOptions

fin = FinClient(
    api_key=os.environ["FINCOPY_API_KEY"],
    news=ServiceOptions(api_key=os.environ["FINNEWS_API_KEY"]),
)
for account in fin.copy.accounts.list_items():
    print(account["Fleet"]["Account"]["ACNO"])
print(fin.news.v1.news.all(pageSize=10).items)
```

Install with `pip install finclient` (Python 3.10 or newer, no dependencies). The Python package also reaches FinGraph,
FinNews and FinSocial; see [python/README.md](./python/README.md).

## API key or member session

An API key (`X-Api-Key`) is for trusted server-side code. Never put one in browser JavaScript, a mobile app or a public
repository.

The member session mode is what FinCopy's own member site uses: no API key, and the request carries the signed-in member's
`fincopy_member` session cookie instead.

- .NET: pass a caller-owned `HttpClient` whose handler sends the cookie, and `new FinClientOptions()` without a key. Set
  `BaseUrl` when the API is not at the default address. The caller configures and disposes that `HttpClient`.
- TypeScript: omit `apiKey` and pass `fetch`. In a browser page on a FinCopy site, the browser's own `fetch` sends the
  cookie (session clients default to `credentials: "include"`); on a server, pass a `fetch` that adds the cookie.

```csharp
using var fin = new FinClient(new FinClientOptions(), authenticatedHttpClient);
```

```ts
const fin = new FinClient({ fetch: (input, init) => fetch(input, init) });
```

The API accepts browser requests only from FinCopy's own sites, so another website cannot use a member's session.
Session trade writes pass the member's trade-PIN gate on the server.

## Resources

- `copy.accounts`: accounts, settings, pairs, symbols, quotes, orders, history, DCA, performance, activity, strategy
  preview and apply, broker password changes and terminal operations (start, stop, restart, disconnect).
- `copy.orders`: preflight, trade, hedge, modify, cancel and DCA answers.
- `copy.fx`: lists across every account you can reach (pairs, orders, history, activity, DCA, terminal operations,
  performance window and bars), strategies (list, read, create, change, delete), signals, alerts, brokers and the event
  stream.
- `copy.changes` and `copy.webhooks`: change feed, webhook configuration and test delivery.

DTO fields keep the API's PascalCase wire names. List methods return a page (`Items` / `items`) with its paging block.

Pair lists and pair views include read-only `AIBotMode`: `OFF` (Off), `EXITS` (Exits only), or `FULL` (Full auto).
The public API cannot change this mode.

Trade calls return when the work is queued for the terminal. Follow the stream, change feed or webhook to see the result.

`copy.accounts.remove(acno, { TrPin }, options)` disconnects the hosted terminal, forgets its broker password and frees its seat.
Customer account settings and trade history remain so the login can be restored later; a free demo's account and terminal records
are deleted. Open orders stay at the broker, and virtual TP, SL and TS stop working while no terminal runs. Include the member's
trading PIN for a session account when its PIN is enabled; API-key requests do not use a session PIN. The `Idempotency-Key`
lets a retry receive the same operation.

The event stream does not reconnect. It ends when the server closes it (about every 30 minutes) or when the connection
breaks (a network error); open it again in a loop and wait a few seconds between attempts.

To verify a webhook in TypeScript, import `FinWebhook` from `@bizjaya/finclient/webhook` (Node.js only). In .NET, use
`Fin.FinWebhook`.

## FinGraph, FinNews and FinSocial

Beside FinCopy, the same client reaches FinGraph (market data, tickers, signals and statistics), FinNews (news, sentiment,
trending, sources and videos) and FinSocial (prediction markets, social posts, authors and videos). Install the package
(`dotnet add package FinClient` or `npm install @bizjaya/finclient`). Each service has its own API key, sent in the
`FinUsrKey` header (FinCopy's stays `X-Api-Key`), and its own address (`https://x.fingraph.io`, `https://x.finnews.io`,
`https://x.finsocial.io`). Give the client a block for each service you use; the top-level key is optional when a block is
given, and a service whose key is missing throws a clear error when you first call it.

```csharp
using var fin = new FinClient(new FinClientOptions
{
    Graph = new FinServiceOptions(Environment.GetEnvironmentVariable("FINGRAPH_API_KEY")!),
    News = new FinServiceOptions(Environment.GetEnvironmentVariable("FINNEWS_API_KEY")!),
    Social = new FinServiceOptions(Environment.GetEnvironmentVariable("FINSOCIAL_API_KEY")!),
});
```

```ts
const fin = new FinClient({
  graph: { apiKey: process.env.FINGRAPH_API_KEY! },
  news: { apiKey: process.env.FINNEWS_API_KEY! },
  social: { apiKey: process.env.FINSOCIAL_API_KEY! },
});
```

### Two surfaces

Every operation in each service's OpenAPI document is a method, and the two surfaces are kept apart because they are billed
differently:

| | `/api` operations | `/v1` operations |
| --- | --- | --- |
| Billing | Charged in credits per call, on any plan. | Included in paid plans, gated by package tier. |
| TypeScript | `fin.graph.tickers.list()` | `fin.graph.v1.tickers.list()` |
| .NET | `fin.Graph.Tickers.ListAsync()` | `fin.Graph.V1.Tickers.ListAsync()` |

The group is the operation's tag (`tickers`, `news`, `markets`, ...) and the method is the action part of its operation id
(`Api_NewsController_RatingMake` is `fin.news.news.ratingMake()` and `fin.News.News.RatingMakeAsync()`). Path parameters are
required positional arguments, then come the body, the query and the per-request options (an abort signal or cancellation
token, an idempotency key for writes). A method returns the documented 200 reply, typed from the API's document. Refusals
(`401` `invalid_api_key`, `402` `insufficient_credits`, `403` `package_not_allowed`, `429` rate limited) throw `FinApiError` /
`FinApiException` with the API's error code, message and the limit fields (`lane`, `limit`, `catag`, `tier`); a `429` is retried
after `Retry-After`. The full naming and argument rules are in the [TypeScript](./typescript/README.md) and
[.NET](./dotnet/README.md) READMEs.

### Filtering and paging

Most FinNews and FinSocial lists take one query parameter, `qry`, a JSON object with `pageNo`, `pageSize`, `sortBy`, `srchBy` and
the endpoint's own filter fields. The method takes it as a typed object and sends it as JSON. Lists that page also have
iterators over pages and over items that advance `pageNo` (or `offset`, for FinGraph's `limit` and `offset` lists) and stop
on an empty page or the last page.

```csharp
var page = await fin.News.News.ListAsync(new QueApiNewsList
{
    PageNo = 1, PageSize = 20, SortBy = [["published", "desc"]], Symbols = "AAPL,MSFT",
});
await foreach (var article in fin.News.News.ListItemsAsync(new QueApiNewsList { Symbols = "AAPL" }))
    Console.WriteLine(article.Title);
```

```ts
const page = await fin.news.news.list({ pageNo: 1, pageSize: 20, sortBy: [["published", "desc"]], Symbols: "AAPL,MSFT" });
for await (const article of fin.news.news.listItems({ Symbols: "AAPL" })) console.log(article.Title);
```

Other operations take their named query fields directly: `fin.graph.tickers.list({ dax: "BIN", quote: "USDT", limit: 20 })`.
A runnable example for each service is in `typescript/examples` and `dotnet/examples`, reading its key from an environment
variable (`FINGRAPH_API_KEY`, `FINNEWS_API_KEY`, `FINSOCIAL_API_KEY`).

For FinCopy's routes and authentication details, see the [FinCopy API documentation](https://fincopy.io/docs). Source:
[GitHub](https://github.com/bizjaya/FinClient).
