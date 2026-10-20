# FinClient

Official .NET and TypeScript clients for the [FinCopy](https://fincopy.io) public API: hosted MetaTrader 4 and 5 accounts,
their settings, trading, the change feed and signed webhooks.

| Language | Package | Install | Guide |
|---|---|---|---|
| .NET (8+, .NET Standard 2.0) | [`FinClient`](https://www.nuget.org/packages/FinClient) | `dotnet add package FinClient` | [dotnet/README.md](dotnet/README.md) |
| TypeScript / Node.js 18+ | [`@bizjaya/finclient`](https://www.npmjs.com/package/@bizjaya/finclient) | `npm install @bizjaya/finclient` | [typescript/README.md](typescript/README.md) |

API reference: [fincopy.io/docs](https://fincopy.io/docs). Create an API key in the FinCopy member area under **API**.

## .NET

```csharp
using Fin;
using Fin.Copy;

using var fin = new FinClient(new FinClientOptions { ApiKey = Environment.GetEnvironmentVariable("FINCOPY_API_KEY")! });
var accounts = await fin.Copy.Accounts.ListAsync();
```

## TypeScript

```ts
import { FinClient } from "@bizjaya/finclient";

const fin = new FinClient({ apiKey: process.env.FINCOPY_API_KEY! });
const accounts = await fin.copy.accounts.list();
```

## Good to know

- **Server-side only.** Keep API keys in a trusted backend; never ship them in a browser, a mobile app, logs or source control.
- **Trades complete asynchronously.** Placing an order returns once it is queued for the terminal; the change feed or a
  webhook reports it filled (with its ticket) or refused (with the reason).
- **Safe retries.** Every write carries an idempotency key that is reused when the client retries, and rate limits are
  honoured through the API's `Retry-After`.

## Versions

Both packages share one version number and are released together from a `vX.Y.Z` tag. See [CHANGELOG.md](CHANGELOG.md).

## Licence

[MIT](LICENSE).
