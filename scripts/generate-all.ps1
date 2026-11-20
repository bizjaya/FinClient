$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'generate-dotnet.ps1')
if ($LASTEXITCODE -ne 0) { throw 'The .NET DTO generation failed.' }
& (Join-Path $PSScriptRoot 'generate-typescript.ps1')
if ($LASTEXITCODE -ne 0) { throw 'The TypeScript DTO generation failed.' }
node (Join-Path $PSScriptRoot 'generate-surface.mjs')
if ($LASTEXITCODE -ne 0) { throw 'The FinGraph, FinNews and FinSocial resource generation failed.' }
