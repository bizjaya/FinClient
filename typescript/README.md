# @bizjaya/finclient

The official TypeScript SDK for the Fin APIs: FinCopy (`https://api.fincopy.io`), FinGraph, FinNews and FinSocial. The
FinCopy sections come first; [FinGraph, FinNews and FinSocial](#fingraph-finnews-and-finsocial) follow them. Since version
0.2.0 FinCopy uses numeric ACNO account paths under `/v1/fxs`, which changed the 0.1 API; see the changelog before upgrading.

```sh
npm install @bizjaya/finclient
```

```ts
import { FinClient } from "@bizjaya/finclient";

const fin = new FinClient({ apiKey: process.env.FINCOPY_API_KEY! });
const accounts = await fin.copy.accounts.list();
for (const account of accounts.items) console.log(account.Fleet?.Account?.ACNO, account.Fleet?.Account?.Login);

// ACNO is in the route; the JSON body contains only trade fields.
// await fin.copy.orders.make(52014125, { SYM: "EURUSD", ACT: "NEW", OTY: "OP_BUY", LOT: 0.01 });
```

The client defaults to `https://api.fincopy.io`. Account-scoped methods take the numeric ACNO, and paged list methods
return `{ items, paging, ... }`. DTO fields keep the API's PascalCase names.

- `copy.accounts`: settings and lifecycle, pairs, symbols, quotes, orders, history, DCA, performance, activity, strategy
  preview and apply, and terminal operations.
- `copy.orders`: preflight, trade, hedge, modify, cancel and DCA answers.
- `copy.fx`: lists across every account you can reach (pairs, orders, history, activity, DCA, terminal operations,
  performance window and bars), strategies, signals, alerts, brokers and the event stream.
- `copy.changes` and `copy.webhooks`: the change feed and webhook setup.

## API key or member session

API-key mode is for trusted server-side code. Do not put an API key in browser JavaScript, a mobile app, a public
repository, or a client-side environment variable.

Member session mode sends the signed-in member's `fincopy_member` cookie instead of a key: omit `apiKey` and pass `fetch`.
A session client defaults to `credentials: "include"`, so in a browser page on a FinCopy site the browser's `fetch` sends
the cookie. On a server, pass a `fetch` that adds the cookie header.

```ts
const fin = new FinClient({ fetch: (input, init) => fetch(input, init) });
const account = await fin.copy.accounts.get(52014125);
```

The API accepts browser requests only from FinCopy's own sites. Session trade writes pass the member's trade-PIN gate on
the server. The main entry uses no Node.js built-ins, so it runs in browsers, Node.js 20+, Deno and Bun.

`copy.accounts.remove(acno, { TrPin }, options)` disconnects the hosted terminal, forgets its broker password and frees its seat.
Customer account settings and trade history remain so the login can be restored later; a free demo's account and terminal records
are deleted. Open orders stay at the broker, and virtual TP, SL and TS stop working while no terminal runs. Include the member's
trading PIN for a session account when its PIN is enabled; API-key requests do not use a session PIN. Pass an `idempotencyKey`
in `options` so retries receive the same operation.

## Results, errors and the stream

Trade calls report that an operation was queued; follow the stream, change feed or webhook for its terminal result. Errors
are raised as `FinApiError` with HTTP status, API code, validation errors, any `Retry-After` value, and an `errorCode` of
`timeout`, `network`, `rate_limited`, `invalid_response` or `empty_response` when the SDK set one. Retries reuse the same
`Idempotency-Key`.

`copy.fx.stream()` does not reconnect. It ends when the server closes it (about every 30 minutes); a broken connection
raises a `network` error. Open it again in a loop, waiting a few seconds between attempts.

## Webhooks

Webhook verification is server-side and uses Node.js's crypto module, so it has its own entry:

```ts
import { FinWebhook } from "@bizjaya/finclient/webhook";

const valid = FinWebhook.verify(secret, timestampHeader, rawBody, signatureHeader);
```

## FinGraph, FinNews and FinSocial

FinGraph (market data, tickers, signals and statistics), FinNews (news, sentiment, trending, sources and videos) and FinSocial
(prediction markets, social posts, authors and videos) sit beside FinCopy in the same client. Each has its own API key, sent
in the `FinUsrKey` header (FinCopy's stays `X-Api-Key`), and its own address:

```ts
import { FinClient } from "@bizjaya/finclient";

const fin = new FinClient({
  graph: { apiKey: process.env.FINGRAPH_API_KEY! },   // https://x.fingraph.io
  news: { apiKey: process.env.FINNEWS_API_KEY! },     // https://x.finnews.io
  social: { apiKey: process.env.FINSOCIAL_API_KEY! }, // https://x.finsocial.io
  // apiKey: process.env.FINCOPY_API_KEY!,            // FinCopy, as before
});
```

Give a block only for the services you use; set `baseUrl` in a block to use another address. The top-level `apiKey` is
optional when a block is given. A service whose key is missing throws a clear error when you first call it, before anything is
sent. Timeouts, retries and `fetch` are shared by every service. Keep keys in trusted server-side code.

### Two surfaces: `/api` and `/v1`

Every operation in each service's OpenAPI document is a method. The two surfaces are billed differently, so they are kept apart:

| | `/api` operations | `/v1` operations |
| --- | --- | --- |
| Billing | Charged in credits per call, on any plan. | Included in paid plans, gated by package tier. |
| Where | `fin.graph.tickers.list()` | `fin.graph.v1.tickers.list()` |

| Service | `/api` groups | `/v1` groups |
| --- | --- | --- |
| `fin.graph` | apcDat, assets, daxDat, fxsTikr, klsePrc, ohlcv, sigCry, sigDax, sigEqt, sigFxs, signals, stats, tickers, tvdDat | assets, ohlcv, quotes, signals, stats, tickers |
| `fin.news` | categories, entities, news, sentiment, sources, trending, videos | entities, news, sentiment, sources, trending |
| `fin.social` | markets, social, videos | markets, social, videos |

The group is the operation's tag and the method is the action part of its operation id (`Api_NewsController_RatingMake` is
`fin.news.news.ratingMake()`; `Pub_TickersController_Get` is `fin.graph.v1.tickers.get()`). If two operations of one group on one
surface would get the same name, the later one in path order is named with its HTTP method in front, then its last path segment;
no operation needs that today. Each method's documentation comment gives what it does, its route, and its cost in credits
(`/api`) or the package tiers it is open to (`/v1`).

Arguments run in the order of the request: path parameters (required, positional), the JSON body, the query, then the
per-request options (`signal`, `idempotencyKey`):

```ts
const detail = await fin.graph.tickers.view("BTCUSDT");                                    // GET /api/tickers/BTCUSDT
const some = await fin.graph.v1.tickers.list({ dax: "BIN", quote: "USDT", limit: 20 });     // GET /v1/tickers?dax=BIN&...
await fin.news.news.ratingMake("<checksum>", { ReqSite: "mysite" }, { idempotencyKey });    // POST with a JSON body
```

A method returns the documented 200 reply as it comes (its `data`, `paging` and the rest), typed from the API's document. Query
fields keep their documented names. One route family is typed loosely: the document names an error body as the reply of
`/v1/ohlcv/...`, so those methods return `unknown`.

### The `qry` object and paging

Most FinNews and FinSocial lists take one query parameter, `qry`, whose value is a JSON object: `pageNo`, `pageSize`, `sortBy`
(pairs of field and `asc` or `desc`), `srchBy`, and the endpoint's own filter fields. The method takes that object, typed from
the document, and sends it as JSON:

```ts
const page = await fin.news.news.list({
  pageNo: 1,
  pageSize: 20,
  sortBy: [["published", "desc"]],
  Symbols: "AAPL,MSFT", // a filter field of this endpoint
});
console.log(page.paging?.resCount, page.data?.[0]?.Title);
```

Every list that pages also has `...Pages()` and `...Items()` async iterators (`listPages`, `listItems`, `postListItems`, ...). They
advance `pageNo` (for `qry` lists) or `offset` (FinGraph's `limit` and `offset` lists), and stop on an empty page, which they do
not yield, or when the paging information says it was the last page. They never change the query you pass.

```ts
for await (const article of fin.news.news.listItems({ pageSize: 100, Symbols: "AAPL" })) console.log(article.Title);
for await (const page of fin.graph.v1.tickers.listPages({ dax: "BIN", limit: 200 })) console.log(page.count);
```

### Refusals

They throw `FinApiError` with `statusCode`, the API's machine-readable `error` code and its `message`, plus `lane`, `limit`,
`catag` and `tier` when the refusal is about a limit: `401` `invalid_api_key`, `402` `insufficient_credits` (or a credit budget or
call limit), `403` `package_not_allowed` (the key's package may not call the endpoint), `429` for a rate limit. A `429` is
retried after the server's `Retry-After` (up to 60 seconds); when it is longer, the error carries `retryAfterMs`.

See the [FinCopy API docs](https://fincopy.io/docs), the examples ([FinCopy](./examples/QuickStart.ts),
[FinGraph](./examples/GraphQuickStart.ts), [FinNews](./examples/NewsQuickStart.ts), [FinSocial](./examples/SocialQuickStart.ts)),
and the [FinClient source](https://github.com/bizjaya/FinClient).

## Build from source

```sh
npm ci
npm run generate:types
npm run generate:surface
npm run typecheck
npm test
npm run build
```
