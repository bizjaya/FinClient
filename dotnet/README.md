# FinClient for .NET

FinClient is the .NET SDK for the [FinCopy public API](https://fincopy.io/docs), with a default base URL of
`https://api.fincopy.pro`. Version 0.2.0 uses numeric ACNO paths under `/v1/fxs`.

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
// await fin.Copy.Orders.MakeAsync(52014125, new CmdApiFxsOrdrMake { SYM = "EURUSD", LOT = 0.01 });
```

## Member session

Pass a caller-owned `HttpClient` configured to send the member session cookie. You may omit the API key in this mode:

```csharp
using var fin = new FinClient(new FinClientOptions(), authenticatedHttpClient);
var account = await fin.Copy.Accounts.GetAsync(52014125);
```

The caller configures and disposes that `HttpClient`. Session trade requests are checked against the member's trade-PIN
gate by the API. API keys remain for trusted server-side clients; never ship them in browser or mobile code.

## Resources

- `Copy.Accounts`: paged accounts, creation, settings, pairs, symbols and quotes, orders, history, DCA, performance,
  activity and terminal operations.
- `Copy.Orders`: trade preflight, trade, hedge, modify, cancel and DCA response.
- `Copy.Fx`: cross-account lists, strategies and cascade preview/apply, signals, alerts, brokers and the live stream.
- `Copy.Changes` and `Copy.Webhooks`: change feed and webhook configuration/test delivery.

Paged methods return `FinPage<T>` with `Items` and paging metadata. Generated DTO properties retain PascalCase names.
Trade writes return when queued for the terminal; observe completion through the stream, change feed or webhook.

API calls throw `FinApiException` with HTTP status, API code, message, validation errors and `RetryAfter` when present. The
client retries rate limits and transient network/gateway errors while reusing each write's `Idempotency-Key`.

See the [QuickStart](./examples/QuickStart), [API docs](https://fincopy.io/docs), and [repository](https://github.com/bizjaya/FinClient).
