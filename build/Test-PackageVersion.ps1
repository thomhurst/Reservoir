# Checks the release-tag grammar in Get-PackageVersion.ps1. Run: pwsh build/Test-PackageVersion.ps1
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'Get-PackageVersion.ps1'

$accepted = [ordered] @{
    'v1.2.3' = '1.2.3'
    '1.2.3' = '1.2.3'
    'v0.0.0' = '0.0.0'
    'v1.2.3-beta.1' = '1.2.3-beta.1'
    'v1.2.3-beta01' = '1.2.3-beta01'
    'v1.2.3-0' = '1.2.3-0'
    'v1.2.3-rc.1.2' = '1.2.3-rc.1.2'
    'v1.2.3-x-y.0a' = '1.2.3-x-y.0a'
    'v65534.65534.65534' = '65534.65534.65534'
}
$rejected = @(
    'v1.02.3', 'v01.2.3', 'v1.2.03', 'v1.2.3-beta..1', 'v1.2.3-beta.01', 'v1.2.3-01', 'v1.2.3-',
    'v1.2', 'v1', 'v65535.0.0', 'v0.65535.0', 'v0.0.99999999999', 'release-1', 'vv1.2.3', 'V1.2.3',
    'v1.2.3+build', 'v1.2.3-beta+build', 'v1.2.3;x', ' v1.2.3', 'v1.2.3 ', ''
)

$failures = 0
foreach ($tag in $accepted.Keys) {
    try {
        $actual = & $script -ReleaseTag $tag
        if ($actual -ne $accepted[$tag]) {
            Write-Host "FAIL accept '$tag': got '$actual', expected '$($accepted[$tag])'"
            $failures++
        }
    }
    catch {
        Write-Host "FAIL accept '$tag': $_"
        $failures++
    }
}

foreach ($tag in $rejected) {
    try {
        $actual = & $script -ReleaseTag $tag
        Write-Host "FAIL reject '$tag': accepted as '$actual'"
        $failures++
    }
    catch {
    }
}

if ($failures -gt 0) {
    throw "$failures release-tag grammar check(s) failed."
}

Write-Host "Release-tag grammar: $($accepted.Count) accepted and $($rejected.Count) rejected as expected."
