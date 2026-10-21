# @bizjaya/finclient

The official TypeScript SDK for FinCopy's public API, `https://api.fincopy.pro`. Version 0.2.0 uses numeric ACNO account
paths under `/v1/fxs` and changes the 0.1 API; see the changelog before upgrading.

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

The client defaults to `https://api.fincopy.pro`. Account-scoped methods take the numeric ACNO, and paged list methods
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
the server. The main entry uses no Node.js built-ins, so it runs in browsers, Node.js 18+, Deno and Bun.

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

See the [FinCopy API docs](https://fincopy.io/docs), the [QuickStart](./examples/QuickStart.ts), and the
[FinClient source](https://github.com/bizjaya/FinClient).

## Build from source

```sh
npm ci
npm run generate:types
npm run typecheck
npm test
npm run build
```
