$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = 'dotnet/src/FinClient/Generated/FinCopyDtos.g.cs'
Push-Location $root
try {
    dotnet tool restore --tool-manifest (Join-Path $root 'dotnet-tools.json')
    if ($LASTEXITCODE -ne 0) { throw 'Could not restore the pinned NSwag tool.' }
    dotnet tool run nswag openapi2csclient `
        /Input:spec/fincopy-v1.json `
        /Output:$output `
        /Namespace:Fin.Copy `
        /GenerateClientClasses:false `
        /GenerateClientInterfaces:false `
        /GenerateDtoTypes:true `
        /GenerateJsonMethods:false `
        /GenerateDataAnnotations:false `
        /JsonLibrary:SystemTextJson `
        /GenerateNullableReferenceTypes:true `
        /GenerateOptionalPropertiesAsNullable:true `
        /NewLineBehavior:LF
    if ($LASTEXITCODE -ne 0) { throw 'NSwag DTO generation failed.' }

    # Enum values the server adds later must not break older clients: every generated enum converter becomes the
    # tolerant one (an unknown value reads as Unknown/UNK, or null on a nullable property).
    $path = Join-Path $root $output
    $text = [IO.File]::ReadAllText($path)
    $text = $text.Replace('System.Text.Json.Serialization.JsonStringEnumConverter<', 'global::Fin.FinTolerantEnumConverter<')
    if ($text.Contains('JsonStringEnumConverter')) { throw 'A generated enum converter was not replaced.' }
    [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
}
finally {
    Pop-Location
}
