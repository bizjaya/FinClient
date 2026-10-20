$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location (Join-Path $root 'typescript')
try {
    npm run generate:types
    if ($LASTEXITCODE -ne 0) { throw 'TypeScript type generation failed.' }
}
finally {
    Pop-Location
}
