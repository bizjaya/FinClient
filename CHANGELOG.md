# Changelog

## 0.2.0 (unreleased)

### BREAKING

- Accounts are addressed by their numeric ACNO, not by a text account id. Every account method takes `long acno` (.NET)
  or `acno: number` (TypeScript), and the routes move to `/v1/fxs/accounts/{acno}`.
- Account lists, pairs, orders, history, activity and operations return a page: `FinPage<T>.Items` (.NET) and
  `page.items` (TypeScript), with the paging block beside them.
- Per-account order reads move from `Orders` to `Accounts`: `Orders.ListAsync` / `orders.list` is now
  `Accounts.OrdersAsync` / `accounts.orders`; `Orders.GetAsync` / `orders.get` is `Accounts.OrderAsync` / `accounts.order`;
  `Orders.HistoryAsync` and `Orders.HistoryItemAsync` are `Accounts.HistoryAsync` and `Accounts.HistoryItemAsync`.
- Account settings: `GetTradingAsync` / `getTrading` is `GetSettingsAsync` / `getSettings`, and `PatchTradingAsync` /
  `patchTrading` is `PatchSettingsAsync` / `patchSettings`, taking `CmdApiFxsAcctPatch`.
- TypeScript pairs: `accounts.listPairs` is `accounts.pairs` and `accounts.getPair` is `accounts.pair`.
- Request types follow the public API: `CmdApiFxsOrdrMake`, `CmdApiFxsHedgeMake`, `CmdApiFxsPreInfo`, `CmdApiFxsPairPatch`
  and `CmdApiFxsAcctPatch` replace the 0.1 commands. None of them carries an account id; the ACNO is in the path.
- `FinPageQuery` keeps `PageNo`, `PageSize`, `Search`, `SortBy`, `SrchBy` and `Include`. `PageOff`, `IsAsc`, `SortCol`,
  `SortDir`, `SrchCol` and `SrchVal` are removed: the API works them out and never read them from a request.
- `Fx.StrategiesAsync` / `fx.strategies` take an optional `acno` before the cancellation token or request options.
- TypeScript: `FinWebhook` moves to its own entry, `@bizjaya/finclient/webhook`, because it needs Node.js's crypto module.
  The main entry no longer imports any Node.js built-in, so it can run in a browser.

### Migrating from 0.1

1. Read each account's `ACNO` (from `Accounts.ListAsync` / `accounts.list`) and pass it wherever you passed the account
   id.
2. Iterate `page.Items` / `page.items` instead of the list itself.
3. Rename the moved methods and request types listed above; remove any account id from request bodies.
4. In TypeScript, change `import { FinWebhook } from "@bizjaya/finclient"` to
   `import { FinWebhook } from "@bizjaya/finclient/webhook"`.

### Added

- Lists across every account a key or session can reach: pairs, orders, history, activity, DCA proposals, terminal
  operations, and the performance window and bars, each with an optional `acno` filter.
- Strategies: list (optionally for one account's owner), read, create, change and delete; preview and apply a strategy to
  an account.
- Account lifecycle and results: demo accounts, external reference, symbols, quotes, DCA queue, performance, activity,
  broker password change, start, stop, restart and remove.
- Signals, alerts, broker search and the live event stream (`Fx.ReadStreamAsync` / `fx.stream()`).
- Member session authentication through a caller-owned `HttpClient` (.NET) or a custom `fetch` (TypeScript); the API key
  is optional in that mode.

### Changed

- Both SDKs default to `https://api.fincopy.pro`.
- The event stream does not reconnect. A connection that cannot open or breaks reports a `network` error; cancelling ends
  it on every .NET target, including .NET Standard 2.0.
- TypeScript makes idempotency keys with Web Crypto when it is available, and otherwise without it (Node.js 18).
- .NET and TypeScript DTOs are regenerated from the current public OpenAPI document.

## 0.1.1 - 2026-10-21

- The npm package is published as `@bizjaya/finclient`: npm refused the unscoped name `finclient` as too close to an
  existing package. No code change; the .NET package is identical to 0.1.0.

## 0.1.0 - 2026-10-21

- Initial public FinCopy SDK for .NET and TypeScript.
