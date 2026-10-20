# @bizjaya/finclient

The official TypeScript SDK for FinCopy's public `/v1` API. It supports Node.js 18 and later, with ESM and CommonJS builds.

```sh
npm install @bizjaya/finclient
```

```ts
import { FinClient } from "@bizjaya/finclient";

const fin = new FinClient({ apiKey: process.env.FINCOPY_API_KEY! });
const accounts = await fin.copy.accounts.list();
for (const account of accounts) console.log(account.Fleet?.Account?.Id, account.Fleet?.Account?.Login);

// Read what changed since the saved cursor, one page at a time; save the cursor after each page.
let cursor: string | undefined = loadSavedCursor();
for await (const page of fin.copy.changes.readPages(cursor)) {
  for (const change of page.Changes ?? []) console.log(change.Entity, change.AccountId, change.OID, change.Status);
  if (page.Cursor) saveCursor(page.Cursor);
}
```

`loadSavedCursor` and `saveCursor` stand for your own storage. The first call without a cursor starts at "now".

A failed call throws `FinApiError` with `statusCode`, FinCopy's `code`, `errors`, and, when the SDK set it, `errorCode`
(`timeout`, `network`, `rate_limited`, `invalid_response` or `empty_response`) and `retryAfterMs`. Calls are retried on
rate limits, gateway errors and network failures, honouring the API's `Retry-After`.

The client is server-side only. Do not put an API key in browser JavaScript, a mobile application, a public repository, or a
client-side environment variable. FinCopy's envelope is handled by the client; DTO fields keep the API's PascalCase wire names.
Writes automatically carry an `Idempotency-Key`, which can be supplied explicitly in the request options when a caller needs
to retry the same operation later. Orders complete asynchronously; use the change feed or webhook to learn their result.

The API key grants the permissions assigned to that key. Store it as a server secret and give it only the permissions the
application needs.

See [FinCopy API docs](https://fincopy.io/docs) and the [FinClient source](https://github.com/bizjaya/FinClient). The
[QuickStart](./examples/QuickStart.ts) example lists accounts and reads the change feed; its order call is commented out
because it places a trade.

## Build from source

```sh
npm ci
npm run generate:types
npm run typecheck
npm test
npm run build
```
