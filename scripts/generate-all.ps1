$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'generate-dotnet.ps1')
if ($LASTEXITCODE -ne 0) { throw 'The .NET DTO generation failed.' }
& (Join-Path $PSScriptRoot 'generate-typescript.ps1')
if ($LASTEXITCODE -ne 0) { throw 'The TypeScript DTO generation failed.' }
& python (Join-Path $PSScriptRoot 'generate_python.py')
if ($LASTEXITCODE -ne 0) { throw 'The Python type generation failed.' }
