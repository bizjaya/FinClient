$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
# One DTO file per service, each in its own namespace.
$services = @(
    @{ Spec = 'spec/fincopy-v1.json';   Output = 'dotnet/src/FinClient/Generated/FinCopyDtos.g.cs';   Namespace = 'Fin.Copy' },
    @{ Spec = 'spec/fingraph-v1.json';  Output = 'dotnet/src/FinClient/Generated/FinGraphDtos.g.cs';  Namespace = 'Fin.Graph' },
    @{ Spec = 'spec/finnews-v1.json';   Output = 'dotnet/src/FinClient/Generated/FinNewsDtos.g.cs';   Namespace = 'Fin.News' },
    @{ Spec = 'spec/finsocial-v1.json'; Output = 'dotnet/src/FinClient/Generated/FinSocialDtos.g.cs'; Namespace = 'Fin.Social' }
)
Push-Location $root
try {
    dotnet tool restore --tool-manifest (Join-Path $root 'dotnet-tools.json')
    if ($LASTEXITCODE -ne 0) { throw 'Could not restore the pinned NSwag tool.' }
    foreach ($service in $services) {
        dotnet tool run nswag openapi2csclient `
            /Input:$($service.Spec) `
            /Output:$($service.Output) `
            /Namespace:$($service.Namespace) `
            /GenerateClientClasses:false `
            /GenerateClientInterfaces:false `
            /GenerateDtoTypes:true `
            /GenerateJsonMethods:false `
            /GenerateDataAnnotations:false `
            /JsonLibrary:SystemTextJson `
            /GenerateNullableReferenceTypes:true `
            /GenerateOptionalPropertiesAsNullable:true `
            /NewLineBehavior:LF
        if ($LASTEXITCODE -ne 0) { throw "NSwag DTO generation failed for $($service.Spec)." }

        # Enum values the server adds later must not break older clients: every generated enum converter becomes the
        # tolerant one (an unknown value reads as Unknown/UNK, or null on a nullable property).
        $path = Join-Path $root $service.Output
        $text = [IO.File]::ReadAllText($path)
        $text = $text.Replace('System.Text.Json.Serialization.JsonStringEnumConverter<', 'global::Fin.FinTolerantEnumConverter<')
        if ($text.Contains('JsonStringEnumConverter')) { throw "A generated enum converter was not replaced in $($service.Output)." }
        [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
    }
}
finally {
    Pop-Location
}
