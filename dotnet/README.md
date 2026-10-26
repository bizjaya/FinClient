# FinClient for .NET

FinClient is the .NET SDK for the [FinCopy public API](https://fincopy.io/docs), with a default base URL of
`https://api.fincopy.io`. Since version 0.2.0 it uses numeric ACNO paths under `/v1/fxs`, which changed the 0.1 API; see
the changelog before upgrading. It targets .NET Standard 2.0 and .NET 8.

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

See the [QuickStart](./examples/QuickStart), [API docs](https://fincopy.io/docs), and [repository](https://github.com/bizjaya/FinClient).
