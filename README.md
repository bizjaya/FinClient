# FinClient

FinClient provides .NET and TypeScript clients for the FinCopy public API at `https://api.fincopy.pro`. Version 0.2.0 uses
the unified `/v1/fxs` trading routes and identifies accounts by their numeric ACNO.

## .NET

```csharp
using Fin;
using Fin.Copy;

using var fin = new FinClient(new FinClientOptions(Environment.GetEnvironmentVariable("FINCOPY_API_KEY")!));
var page = await fin.Copy.Accounts.ListAsync();
foreach (var account in page.Items)
    Console.WriteLine($"{account.Fleet?.Account?.ACNO}: {account.Fleet?.Account?.Login}");

// ACNO is in the URL; request bodies do not contain an account id.
// await fin.Copy.Orders.MakeAsync(52014125, new CmdApiFxsOrdrMake { SYM = "EURUSD", LOT = 0.01 });
```

For a signed-in member session, pass a caller-owned `HttpClient` whose handler sends the member cookie. In that mode the
API key can be omitted:

```csharp
using var fin = new FinClient(new FinClientOptions(), authenticatedHttpClient);
var account = await fin.Copy.Accounts.GetAsync(52014125);
```

The caller configures and disposes its `HttpClient`. Server-side API-key clients use `X-Api-Key`; keep that key out of
browser and mobile code. The default base URL is `https://api.fincopy.pro`.

## TypeScript

```ts
import { FinClient } from "@bizjaya/finclient";

const fin = new FinClient({ apiKey: process.env.FINCOPY_API_KEY! });
const page = await fin.copy.accounts.list();
for (const account of page.items) console.log(account.Fleet?.Account?.ACNO);

// await fin.copy.orders.make(52014125, { SYM: "EURUSD", LOT: 0.01 });
```

For a signed-in member session, omit `apiKey` and provide a `fetch` implementation that carries the session cookie. The
default credentials mode for a session client is `include`:

```ts
const fin = new FinClient({ fetch: sessionFetch, credentials: "include" });
const account = await fin.copy.accounts.get(52014125);
```

The SDK is intended for trusted server-side use with API keys. Never put an API key in browser JavaScript or a mobile app.
DTO fields retain the API's PascalCase wire names. List methods return `{ items, paging, ... }`.

## Resources

- `copy.accounts`: account creation, settings, pairs, symbols, quotes, orders, history, DCA, performance, activity,
  broker password changes and terminal operations.
- `copy.orders`: preflight, trade, hedge, modify, cancel and DCA responses.
- `copy.fx`: cross-account lists, strategies, cascade preview/apply, signals, alerts, brokers and server-sent events.
- `copy.changes` and `copy.webhooks`: change feed, webhook configuration and test delivery.

Successful trade calls mean the work was queued for the terminal. Follow the stream, change feed or webhook to observe its
result. Session-based trade requests pass the member's trade-PIN gate on the server.

For complete routes and authentication details, see [FinCopy API documentation](https://fincopy.io/docs). Source:
[GitHub](https://github.com/bizjaya/FinClient).
