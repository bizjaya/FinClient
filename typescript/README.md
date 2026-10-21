# @bizjaya/finclient

The official TypeScript SDK for FinCopy's public API, `https://api.fincopy.pro`. Version 0.2.0 uses numeric ACNO account
paths under `/v1/fxs`.

```sh
npm install @bizjaya/finclient
```

```ts
import { FinClient } from "@bizjaya/finclient";

const fin = new FinClient({ apiKey: process.env.FINCOPY_API_KEY! });
const accounts = await fin.copy.accounts.list();
for (const account of accounts.items) console.log(account.Fleet?.Account?.ACNO, account.Fleet?.Account?.Login);

// ACNO is in the route; the JSON body contains only trade fields.
// await fin.copy.orders.make(52014125, { SYM: "EURUSD", LOT: 0.01 });
```

The client defaults to `https://api.fincopy.pro`. Account-scoped methods take the numeric ACNO, and paged list methods
return `{ items, paging, ... }`. DTO fields keep the API's PascalCase names. The `copy.accounts`, `copy.orders`, and
`copy.fx` resources cover account settings and lifecycle, pairs, symbols, quotes, orders, history, trade actions, DCA,
performance, activity, terminal operations, cross-account lists, strategies, signals, alerts, brokers and the live event
stream. `copy.changes` and `copy.webhooks` expose the change feed and webhook setup.

For a signed-in member session, omit `apiKey` and pass a `fetch` implementation that sends the session cookie. The session
mode defaults to `credentials: "include"`:

```ts
const fin = new FinClient({ fetch: sessionFetch, credentials: "include" });
const account = await fin.copy.accounts.get(52014125);
```

API-key mode is for trusted server-side code. Do not put an API key in browser JavaScript, a mobile app, a public repository,
or a client-side environment variable. Session trade writes pass the member's trade-PIN gate on the server.

Trade calls report that an operation was queued; follow the stream, change feed or webhook for its terminal result. Errors
are raised as `FinApiError` with HTTP status, API code, validation errors, and any `Retry-After` value. Retries reuse the
same `Idempotency-Key`.

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
