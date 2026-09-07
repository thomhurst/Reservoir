param(
    [Parameter(Mandatory)]
    [string] $PackagePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$resolvedPackagePath = (Resolve-Path $PackagePath).Path
$archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedPackagePath)

try {
    $entries = @($archive.Entries.FullName)
    $expectedAssets = @(
        'lib/netstandard2.0/Reservoir.dll'
        'lib/netstandard2.0/Reservoir.xml'
        'lib/net8.0/Reservoir.dll'
        'lib/net8.0/Reservoir.xml'
        'lib/net10.0/Reservoir.dll'
        'lib/net10.0/Reservoir.xml'
    )

    foreach ($expectedAsset in $expectedAssets) {
        if ($entries -notcontains $expectedAsset) {
            throw "Package is missing $expectedAsset."
        }
    }

    $forbiddenPrefixes = @('contentFiles/', 'buildTransitive/')
    foreach ($entry in $entries) {
        foreach ($forbiddenPrefix in $forbiddenPrefixes) {
            if ($entry.StartsWith($forbiddenPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Package contains obsolete source-delivery asset $entry."
            }
        }
    }
}
finally {
    $archive.Dispose()
}

Write-Host "Validated runtime and XML documentation assets in $resolvedPackagePath"
