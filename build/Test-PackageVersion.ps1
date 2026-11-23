# Checks the release-tag grammar and SemVer precedence used by Get-PackageVersion.ps1. Run: pwsh build/Test-PackageVersion.ps1
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'Get-PackageVersion.ps1'
. (Join-Path $PSScriptRoot 'SemanticVersion.ps1')

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

# Each pair is strictly ascending.
$ascending = @(
    @('1.10.1', '1.11.0'), @('1.9.0', '1.10.0'), @('1.10.1', '2.0.0'), @('0.9.9', '1.0.0'),
    @('2.0.0-rc.9', '2.0.0-rc.10'), @('2.0.0-rc.1', '2.0.0'), @('1.0.0-alpha', '1.0.0-alpha.1'),
    @('1.0.0-alpha.1', '1.0.0-alpha.beta'), @('1.0.0-alpha.beta', '1.0.0-beta'),
    @('1.0.0-beta', '1.0.0-beta.2'), @('1.0.0-beta.2', '1.0.0-beta.11'), @('1.0.0-beta.11', '1.0.0-rc.1'),
    @('1.0.0-1', '1.0.0-alpha'), @('1.0.0-Beta', '1.0.0-alpha'), @('1.0.0-beta01', '1.0.0-beta1')
)
foreach ($pair in $ascending) {
    $low = ConvertTo-SemanticVersion $pair[0]
    $high = ConvertTo-SemanticVersion $pair[1]
    if ((Compare-SemanticVersion $low $high) -ne -1 -or (Compare-SemanticVersion $high $low) -ne 1) {
        Write-Host "FAIL precedence: expected $($pair[0]) < $($pair[1])"
        $failures++
    }
}

foreach ($version in '1.10.1', '2.0.0-rc.1', '1.0.0-alpha.1') {
    $parsed = ConvertTo-SemanticVersion $version
    if ((Compare-SemanticVersion $parsed (ConvertTo-SemanticVersion "v$version")) -ne 0) {
        Write-Host "FAIL precedence: expected $version to equal v$version"
        $failures++
    }
}

# -HigherThan prints a draft tag's version only when the tag is valid and higher than the suggestion.
$keep = [ordered] @{
    'v1.11.0' = '1.11.0'; 'v2.0.0' = '2.0.0'; '1.10.2' = '1.10.2'; 'v1.10.1' = ''; 'v1.10.0' = '';
    'v1.10.1-beta.1' = ''; 'v1.10.2-beta.1' = '1.10.2-beta.1'; 'v1.011.0' = ''; 'release-2' = ''; '' = ''
}
foreach ($tag in $keep.Keys) {
    $actual = "$(& $script -ReleaseTag $tag -HigherThan '1.10.1')"
    if ($actual -cne [string] $keep[$tag]) {
        Write-Host "FAIL -HigherThan 1.10.1 '$tag': got '$actual', expected '$($keep[$tag])'"
        $failures++
    }
}

if ($failures -gt 0) {
    throw "$failures release-tag grammar check(s) failed."
}

Write-Host "Release-tag grammar: $($accepted.Count) accepted and $($rejected.Count) rejected; $($ascending.Count) precedence pairs and $($keep.Count) override cases as expected."
