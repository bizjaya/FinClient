# Changelog

## 0.2.0 (unreleased)

- Move account operations to the public `/v1/fxs` trading surface and use numeric ACNO values in account paths.
- Add account lifecycle, settings, symbols, quotes, orders, history, trade actions, DCA, performance, activity, terminal
  operations, cross-account FXS lists, strategies, signals, alerts, brokers and server-sent events.
- Add member-session authentication through a caller-owned `HttpClient` or custom TypeScript `fetch`; API keys are optional
  only when the supplied transport authenticates the member session.
- Default both SDKs to `https://api.fincopy.pro`; update examples and public route/authentication documentation.
- Regenerate .NET and TypeScript DTOs from the local API OpenAPI document.

## 0.1.1 - 2026-10-21

- The npm package is published as `@bizjaya/finclient`: npm refused the unscoped name `finclient` as too close to an
  existing package. No code change; the .NET package is identical to 0.1.0.

## 0.1.0 - 2026-10-21

- Initial public FinCopy SDK for .NET and TypeScript.
