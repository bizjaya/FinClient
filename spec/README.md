# OpenAPI snapshots

Each file is a service's public OpenAPI document as of the SDK's last regeneration:

| Snapshot | Live document | Service |
| --- | --- | --- |
| `fincopy-v1.json` | `https://api.fincopy.io/openapi/v1.json` | FinCopy |
| `fingraph-v1.json` | `https://x.fingraph.io/openapi/v1.json` | FinGraph |
| `finnews-v1.json` | `https://x.finnews.io/openapi/v1.json` | FinNews |
| `finsocial-v1.json` | `https://x.finsocial.io/openapi/v1.json` | FinSocial |

Both SDKs are generated from them:

```powershell
./scripts/generate-all.ps1
```

That runs three generators, and everything they write is checked in:

- `scripts/generate-typescript.ps1` writes one schema file per service under `typescript/src/generated/`
  (openapi-typescript).
- `scripts/generate-dotnet.ps1` writes one DTO file per service under `dotnet/src/FinClient/Generated/`, each in its own
  namespace (`Fin.Copy`, `Fin.Graph`, `Fin.News`, `Fin.Social`), with the tolerant enum converter (NSwag).
- `scripts/generate-surface.mjs` writes the FinGraph, FinNews and FinSocial resource classes (`typescript/src/*.generated.ts`
  and `dotnet/src/FinClient/Generated/Fin*Resources.g.cs`): one method per operation, named from its tag and operation id.
  FinCopy's resources are written by hand.

Do not edit generated files by hand. CI regenerates each of them and fails on any difference.

A scheduled CI job compares every snapshot with its live document every day. When it reports a difference, refresh the
snapshot, regenerate, and release a new version. A live document with no schemas (a site that is still deploying) is
reported as a failure rather than snapshotted.
