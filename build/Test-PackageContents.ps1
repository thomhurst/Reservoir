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
    $expectedAssemblies = @(
        'lib/netstandard2.0/Reservoir.dll'
        'lib/net8.0/Reservoir.dll'
        'lib/net10.0/Reservoir.dll'
    )

    foreach ($expectedAssembly in $expectedAssemblies) {
        if ($entries -notcontains $expectedAssembly) {
            throw "Package is missing $expectedAssembly."
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

Write-Host "Validated runtime assets in $resolvedPackagePath"
