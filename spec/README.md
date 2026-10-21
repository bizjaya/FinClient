# FinCopy OpenAPI snapshot

`fincopy-v1.json` is FinCopy's public OpenAPI document, `https://api.fincopy.pro/openapi/v1.json`, as of the SDK's last
regeneration. Both SDKs' data types are generated from it:

```powershell
./scripts/generate-all.ps1
```

Do not edit the generated type files by hand. A scheduled CI job compares this snapshot with the live document every day;
when it reports a difference, refresh the snapshot, regenerate, and release a new version.
