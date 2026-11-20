# Contributing

Issues and pull requests are welcome. Please keep changes focused, include tests for behavior changes, and run the
checks for the affected language before opening a pull request.

- .NET: `dotnet build dotnet/FinClient.slnx -c Release` and `dotnet test dotnet/FinClient.slnx -c Release`
- TypeScript: `npm run typecheck`, `npm test`, and `npm run build` from `typescript/`
- Python: from `python/`, `pip install -e .[dev]`, then `ruff check .`, `mypy` and `pytest`; regenerate the types with
  `python scripts/generate_python.py` (CI fails if the generated files differ from the specs)
- Generated API types and resources must be regenerated from the snapshots in `spec/` (FinCopy, FinGraph, FinNews and
  FinSocial) with `powershell -File scripts/generate-all.ps1`; do not hand-edit generated files. See `spec/README.md`.

Do not commit API keys, tokens, connection strings, webhook secrets, customer data, or internal URLs. Keep credentials in
local environment variables or a secret manager.
