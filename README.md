# FinClient

FinClient provides .NET and TypeScript clients for the FinCopy public API at `https://api.fincopy.pro`. Version 0.2.0 uses
the `/v1/fxs` trading routes and identifies accounts by their numeric ACNO. It changes the 0.1 API; see
[CHANGELOG.md](./CHANGELOG.md) before upgrading.

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

For routes and authentication details, see the [FinCopy API documentation](https://fincopy.io/docs). Source:
[GitHub](https://github.com/bizjaya/FinClient).
