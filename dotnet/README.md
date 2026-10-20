# FinClient for .NET

The official .NET client for the [FinCopy public API](https://fincopy.io/docs): hosted MetaTrader accounts, their settings,
trading, the change feed and webhooks. Runs on .NET 8 or later and on .NET Standard 2.0 (including .NET Framework 4.6.2+).

```sh
dotnet add package FinClient
```

## Quick start

```csharp
using Fin;
using Fin.Copy;

using var fin = new FinClient(new FinClientOptions { ApiKey = Environment.GetEnvironmentVariable("FINCOPY_API_KEY")! });

var accounts = await fin.Copy.Accounts.ListAsync();
```

Create one `FinClient` per API key and reuse it; it is safe to share between threads. With dependency injection, register
it once:

```csharp
services.AddFinClient(options => options.ApiKey = configuration["FinCopy:ApiKey"]!);
```

## Trades complete asynchronously

`fin.Copy.Orders.MakeAsync(...)` returns once the order is **queued** for your MetaTrader terminal, not when it is filled. The
terminal picks it up on its next sync (seconds). Follow the change feed, or set a webhook, to see each order filled (with its
ticket) or refused (with the reason).

## The change feed

Read it a page at a time and save the cursor after handling each page, so a restart carries on where it stopped:

```csharp
string? cursor = LoadSavedCursor();   // null the first time: the feed starts at "now"
await foreach (var page in fin.Copy.Changes.ReadPagesAsync(cursor))
{
    foreach (var change in page.Changes ?? []) Handle(change);
    SaveCursor(page.Cursor);
}
```

`ReadAllAsync` streams the rows without the cursor, for when you do not need to resume.

## Webhooks

FinCopy signs every webhook POST. Check it before trusting the body:

```csharp
var valid = FinWebhook.Verify(secret, request.Headers["X-FinCopy-Timestamp"], rawBody, request.Headers["X-FinCopy-Signature"]);
```

Pass the raw request body, exactly as received. Signatures older than five minutes are refused.

## Errors, retries and idempotency

- A failed call throws `FinApiException` with the HTTP `StatusCode`, FinCopy's `Code`, `Message` and `Errors`. `ErrorCode`
  is `timeout`, `network`, `rate_limited`, `invalid_response` or `empty_response` when the SDK can say why; `RetryAfter` is
  how long the API asked you to wait.
- Rate limits (429) and gateway errors (502, 503, 504) are retried with back-off, honouring `Retry-After` up to 60 seconds,
  `MaxRetries` times (3 by default).
- Every write carries an `Idempotency-Key`, generated for you or passed as `idempotencyKey`, and a retry reuses it, so the
  API applies the write once.
- `FinClientOptions.Timeout` (30 seconds) limits each attempt. When you pass your own `HttpClient`, its `Timeout` applies too.

## Notes

- **Server-side only.** An API key reaches real trading accounts: never put it in a browser, a mobile app, source control or
  logs.
- Data types keep FinCopy's field names exactly (`ACNO`, `SYM`, `LOT` …). An enum value this version does not know yet reads
  as the enum's `Unknown`/`UNK` member, or as null, instead of failing.
- The package is not annotated for trimming or native AOT yet.

More: the [quick-start example](https://github.com/bizjaya/FinClient/tree/main/dotnet/examples/QuickStart), the
[API reference](https://fincopy.io/docs) and the [repository](https://github.com/bizjaya/FinClient).
