# Changelog

## 0.11.0 (Unreleased)

### Added

- Python: the `finclient` package for Python 3.10 and newer, with no runtime dependencies. One `FinClient` reaches FinCopy
  (`fin.copy`), FinGraph (`fin.graph`), FinNews (`fin.news`) and FinSocial (`fin.social`); each part has its own key,
  sent as `X-Api-Key` for FinCopy and as `FinUsrKey` for the others, and its own base URL.
- Python: the FinCopy surface mirrors the TypeScript client in snake_case (accounts, orders, fx, changes, webhooks, the
  event stream and the webhook signature check).
- Python: every FinGraph, FinNews and FinSocial operation is reachable, `/api` operations as `fin.<part>.<group>.<operation>()`
  and `/v1` operations as `fin.<part>.v1.<group>.<operation>()`. A filter object is passed as keyword arguments or a dict
  and sent as the `qry` parameter; list operations have `_pages` and `_items` iterators that follow `pageNo` or `offset`.
- Python: typed request and response shapes (`TypedDict` and `Literal`) generated from the four API snapshots in `spec/`
  by `scripts/generate_python.py`; CI regenerates them and fails on a difference.
- Python: one exception hierarchy under `FinApiError` (`AuthenticationError`, `InsufficientCreditsError`,
  `PermissionDeniedError`, `NotFoundError`, `RateLimitError`, `ServerError`), carrying the API's code and the limit
  fields; retries with backoff on 429 (honouring `Retry-After`) and on 502, 503 and 504 for requests that are safe to repeat.
- Snapshots of the FinGraph, FinNews and FinSocial API descriptions in `spec/`.
- The release workflow publishes the Python package to PyPI through trusted publishing, beside NuGet and npm; CI checks
  that all three packages carry the same version.

## 0.10.0 (2026-11-19)

- Regenerate FinCopy DTOs for the T709 SL model and account limits.
- Carry native Fixed / My range / Strategy values and cached ranges in account contracts, including nullable Any steps and boolean / enum choices.
- Account patches accept ordinary per-value state edits; AIBot-only controls remain on FinCopy's member API.
- Rename AMode DCAW/DCAG to WDCA/GDCA; numeric IDs remain 1 and 2.
- Keep the .NET and TypeScript generated contracts matched to the FinCopy public OpenAPI source.

## 0.9.0 (2026-11-18)

Regenerated from FinCopy's T709 API document. FinCopy strategies now hold ranges and per-side signal sets; several facts
that FinCopy held twice keep one home. **Breaking:** the removed fields below, and strategy numbers that were plain numbers.

### Added

- `DecRng` (`Min`, `Def`, `Max`): every number a strategy holds is a range. The account and each pair keep one value, read
  against the strategy's range; a cascade sends each range's `Def`.
- `SigMins` and `SigMinsRng`, with `Signal`, `Trend` and `Confidence`: the per-side signal minimums (`BuySig`, `SellSig`,
  `BuyTrd`, `SellTrd` (null = Any), `Conf`, `Score` 0-10). A signal minimum may be any step of `Signal` (BUY `VSS`-`VSB`, a BUY
  passing at or above it; SELL mirrored, `VSB`-`VSS`, passing at or below it; `N` lets a neutral reading through), and a trend
  minimum any step of `Trend`. Strategies (`DtoFxsStg`, `CmdFxsStgMake`, `CmdFxsStgPatch`) and accounts (`DtoFxsAcct`) carry
  `SigCold`, `SigWarm` and `SigHot`; HOT has no score.
- `DtoFxsStgSymRng`: a strategy's symbol template, its numbers as ranges, per side.
- `FxsFullAutoSettings.OpenPerSide`: most AIBot trades open on one side at once.
- `DtoFxsAccountState.ISNO`: the strategy the account follows (0 = none).
- `CmdApiFxsAcctPatch.SigCold`, `SigWarm` and `SigHot`: the account's own three signal sets, each optional (absent leaves it
  as stored) and replaced whole, checked as the member's own AIBot settings are and written to the account log. A signal
  minimum may be any step of `Signal` (BUY `VSS`-`VSB`, SELL mirrored); a trend minimum any step of `Trend` (BUY `VSD`-`VSU`,
  SELL mirrored), or null for Any. `CmdApiFxsAcctPatch.TrPin`: a member session (cookie or Telegram bearer) that sends any of
  the three sets gives its Trading PIN, as the member's own AIBot settings do; an API key does not.
- `DtoFxsStgRank` and `DtoFxsStg.Rank`: a strategy's ranking from its followers' last 30 days, rebuilt by FinCopy each night
  (`Followers`, `Trades`, `Wins`, `PnlAmt`, `EqtBase` and how it was valued, `PnlPct`, `WinPct`, `MaxDDPct`, `Score` 0-100 or
  null for "New" under 10 closed trades, `WinFrom` / `WinTo`). No ratings. `PnlPct` and `MaxDDPct` are null when the followers'
  equity base is zero or less (nothing to measure against), and the score is then null too.
- `DtoFxsStg.PicFile` and `PicUrl`: the strategy's picture, a 256 px WebP FinCopy made, served at `PicUrl` (a path on the API's
  origin, `/media/stg/<file>`); null when the strategy has none.
- `CmdFxsStgAIBot.Protection` (`SL` or `DCA`, the ladder) and `CmdFxsStgPatch.Clear` (the nullable ACCSTG / SYMSTG ranges to
  empty); `CmdFxsStgMake` / `CmdFxsStgPatch` carry `TrPin` (FinCopy's member routes ask the Trading PIN; an API key does not).
- Types already in FinCopy's API and now in the SDKs: `CmdFxsStgAIBot`, `FxsAIBotStgLimits`, `FxsAIBotStgPair`,
  `FxsAIBotEntryRules`, `DtoFxsStgCascadeChange`, and the AIBot fields of the cascade preview and result.

### Changed

- `DtoFxsStg.SYMSTG` is a `DtoFxsStgSymRng`, and `DtoFxsStgAccStg`'s numbers (`DAYTP`, `DAYSL`, `POCNT`, `DCAIVMINS`,
  `POCLSMINS`, `MAXDDFRC`, `MAXDLYSECS`, `SYNCSECS`) and `DCACooloffMins` are `DecRng`, on the strategy only.
- `FxsAIBotPreferences.Protection`: `DCA` is the ladder.

### Removed

- `EnDcaGate` (`CmdApiFxsAcctPatch`, `CmdFxsStgMake`, `CmdFxsStgPatch`, `DtoFxsStg`, `DtoFxsAcct`): the DCA gate is
  `ACCSTG.DCAGATE` alone.
- `DtoFxsAcct.AIBotRiskPct` (risk a trade is `SYMSTG.BRISK` / `SRISK`) and `AIBotDailyLossPct` (the daily loss is
  `ACCSTG.DAYSL`).
- `FxsFullAutoSettings.DailyLossPct`, `RiskDial`, `RiskCeiling` and `AdviceTimes` (the risk dial is gone; one `AdviceTime`).
- `FxsAIBotPreferences.StopStyle`, `LadderEnabled` and `MaxLayers` (the hard SL is `BHRSL` / `SHRSL`, the ladder is
  Protection `DCA`, the most levels and groups are `BLMAX` / `BGMAX`).

## 0.8.0 (2026-11-14)

### Added

- `AdoptOpenTradesAsync` (.NET) and `adoptOpenTrades` (TypeScript): `POST /v1/fxs/accounts/{acno}/trade/adopt`. One press makes every market
  position opened outside FinCopy on the account FinCopy's own, keeping its ticket, lot, price and open time. FinCopy enables each trade's
  pair within the plan's pair limit, oldest first; a pair that does not fit is refused whole and listed with its tickets (`Refused`).
  The broker's TP and SL on an adopted trade stay until its side gets an add or the symbol's TP or SL settings change. `Preview: true`
  returns what would happen and changes nothing. It needs the Trade permission and an EA of 2.38 or later, and honours `Idempotency-Key`.
- Types `CmdApiFxsAdopt`, `DtoApiFxsAdopt`, `DtoApiFxsAdoptPair` and `DtoApiFxsAdoptRefused`, generated from FinCopy's T709 API document.

### Removed

- `ImportOpenLegsAsync` / `importOpenLegs` (`POST /v1/fxs/accounts/{acno}/open-legs/import`) and its types
  `CmdApiFxsOpenLeg`, `CmdApiFxsOpenLegImport` and `DtoApiFxsOpenLegImport`: the FINQ5 open-legs import is gone; use Adopt open trades.

## 0.7.0 (2026-11-13)

### Changed

- Rename mode 5 to `FIRR` (Fixed Risk/Reward); the numeric identity stays 5.
- Regenerate both clients from FinCopy’s T709 API document.
- Use the shared mode names with codes: DCA Whole Side (DCAW), DCA Per Group (DCAG), Detached Grid (GRID), Contra Offset (CONT), Fixed Risk/Reward (FIRR), Copy Trade (COPY).

## 0.6.0 (2026-11-12)

### Added
- Pair lists and pair views expose read-only `AIBotMode`: `OFF` (Off), `EXITS` (Exits only), or `FULL` (Full auto).
  Both clients are generated from the same API spec. The public API cannot change this mode.

## 0.5.0 (2026-11-11)

### Changed

- Refresh the FXS schema for FinCopy's AMode rollout: per-side modes are DCAW, DCAG, GRID, CONT, FRRR and COPY, with the matching mode and risk settings.
- Remove the retired Independent-orders field from the FinCopy strategy/account API shape.
- Mode names: DCA Whole Side (DCAW), DCA Per Group (DCAG), Detached Grid (GRID), Contra Offset (CONT), Fixed Risk:Reward (FRRR), Copy Trade (COPY). `FXMODE` is gone; `BAMODE` / `SAMODE` carry the mode.
- The trailing switch `BTRAIL` / `STRAIL` replaces the TP and TS notches (`BHRTP`, `SHRTP`, `BHRTS`, `SHRTS` are gone); the SL keeps its notch.
- Fixed Risk:Reward settings `BFRATIO` / `SFRATIO` and `BRISK` / `SRISK`; the allowed modes AIBot may switch a side to, `BALLOWED` / `SALLOWED`.

### Added

- Rebalance: `RebalanceMode` (OFF, AUTO, MANUAL) on the account as `REBALMODE`, the order action `RBL`, the close reason `RBL`, and `RebalId` on order rows that belong to one rebalance.
- The day stop: `DAYSTOP`, `DAYTP`, `DAYSL` on the account, the close reason `DAY`, and the refusal codes `DAYSTOP` and `AMODE` (a side's mode refuses the order); `NOT_CLOSED` when the broker kept a position FinCopy asked to close.
- Unreferenced Telegram copy schemas dropped from the public document.

## 0.4.0 (2026-11-10)

### Added

- Idempotency-key protection for account, pair, symbol, DCA, strategy, instrument, credential, webhook and terminal write routes. Reusing a key cannot apply the write a second time.
- Filter reachable accounts by the caller's external reference.
- Join the account waitlist and import already-open broker legs into a hosted account, preserving the supplied order IDs and tickets.
- Update the broker login, server and password through the account credentials route.
- Instrument suggestions and confirmation are now fully described in the API schema; confirmation uses `PUT` and returns the confirmed FinGraph instrument.
- .NET and TypeScript SDK methods for waitlist join and open-leg import.
## 0.3.0 (2026-11-10)

### Changed

- **Breaking:** strategies take no account. `Copy.Fx.StrategiesAsync()` (.NET) and `copy.fx.strategies()` (TypeScript) list
  your own strategies and FinCopy's presets; the `acno` argument is gone, because FinCopy no longer shares accounts between
  members (an account is reached only by its owner). The API ignores an `acno` sent by an older SDK.
- Fleet accounts no longer carry `USRMAILS` (the members an account was shared with), and the demo-availability answer no
  longer carries `DurationHours`: the demo's length is in the public settings and may be part of an hour.
- The API description and comments say a key reaches only its member's own accounts, and every trading PIN is the
  member's own.

### Added

- Edit order (`CmdFxsModify`) carries `TrailPercent`: a trade's own TS as a percent of its TP money (needs FinCopy's EA
  2.35); null keeps the TS the order has, 0 removes it. Orders, archived orders and pre-trade legs carry `TSP`.
- Orders and archived orders carry `AIBotRole`, `AIBotAuto`, `AutoDecisionId` and `PlannedHoldMins` (who manages a trade
  for AIBot); fleet accounts carry `FullAuto`, `BrokerEnvironment`, `PairLimitOverride` and `PairLimitLastKnown`; account
  state carries `PairLimit` and `PairPreviews`; strategies carry `AIBot`.
- Pair instrument suggestions and confirmation: `GET /v1/fxs/accounts/{acno}/pairs/instrument-suggestions/{symbol}` and
  `PUT /v1/fxs/accounts/{acno}/pairs/instrument-confirm`.
- Hosted terminals carry `OpenAlerts`, `PriceLagSec` and `PriceTickAt`; hosted accounts carry `MemberTerminalAlerts`; paged
  answers may carry `grps`.

## 0.2.2 (2026-10-28)

### Fixed

- Broker search (`Copy.Fx.BrokersAsync` in .NET, `copy.fx.brokers` in TypeScript) sends the search text as `query`, the
  parameter the API reads. Earlier versions sent it as `Q`, which the API does not read, so the search text had no
  effect.

## 0.2.1 (2026-10-26)

### Changed

- Both SDKs default to `https://api.fincopy.io`, FinCopy's API address. `https://api.fincopy.pro` keeps answering for a
  year, so earlier versions keep working until you upgrade.
- Order and hedge requests name their AIBot fields `AIBotAdviceId` and `AIBotAcceptedSettings`, as the API now does
  (they were `AskAiAdviceId` and `AskAiAcceptedSettings`). Code that sets the old names must use the new ones.
- TypeScript: Node.js 20 or newer is required; Node.js 18 has had no security fixes since April 2025. The package is
  built for Node.js 20. The .NET package's support (.NET Standard 2.0 and .NET 8) is unchanged.
- TypeScript test and build tools updated: vite 8, vitest 5, tsup 9, openapi-typescript 7.13 and @types/node 26.
- Account disconnect stops and removes the terminal, forgets the broker password and frees its seat. Customer account
  settings and trade history remain; a free demo's account and terminal records are deleted. Open orders stay at the
  broker, and virtual exits stop working while no terminal runs.

### Added

- Fleet accounts carry `AIBotRiskPct`, `AIBotPropFirm` and `AIBotDailyLossPct`; pairs carry `AIMeta` and `AIMisc`.
- Hosted accounts and terminals carry `TMNO`, the terminal's number; fleet accounts carry `AccountName`, the name on the
  MetaTrader account.
- Account disconnect accepts the member trading PIN in the DELETE request body when a session account requires one.

## 0.2.0 (2026-10-21)

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
