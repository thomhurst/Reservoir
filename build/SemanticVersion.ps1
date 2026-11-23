# Release-tag grammar and SemVer 2.0 precedence, shared by Get-PackageVersion.ps1 and its tests.
# Dot-source it: . ./build/SemanticVersion.ps1

# The one version grammar for release tags and package versions: an optional `v`, then a
# SemVer 2.0 version with no build metadata. Numeric parts and numeric prerelease identifiers have
# no leading zeros. Major, minor and patch are at most 65534 because they become AssemblyVersion
# and FileVersion parts.
$VersionPattern = '^v?(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)' +
    '(?:-(?<prerelease>(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*))?$'
$MaxVersionPart = 65534

function ConvertTo-SemanticVersion {
    param([string] $Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    $match = [regex]::Match($Value, $VersionPattern)
    if (-not $match.Success) {
        return $null
    }

    $parts = @()
    foreach ($name in 'major', 'minor', 'patch') {
        $digits = $match.Groups[$name].Value
        if ($digits.Length -gt 5 -or [int] $digits -gt $MaxVersionPart) {
            return $null
        }

        $parts += [int] $digits
    }

    return [pscustomobject] @{
        Major = $parts[0]
        Minor = $parts[1]
        Patch = $parts[2]
        IsPrerelease = $match.Groups['prerelease'].Success
        Prerelease = $match.Groups['prerelease'].Value
        Text = $Value.Substring($(if ($Value.StartsWith('v')) { 1 } else { 0 }))
    }
}

# SemVer 2.0 precedence: -1, 0 or 1 as $Left is lower than, equal to or higher than $Right.
# Build metadata is not part of the grammar, so it never needs ignoring here.
function Compare-SemanticVersion {
    param(
        [Parameter(Mandatory)] $Left,
        [Parameter(Mandatory)] $Right
    )

    foreach ($part in 'Major', 'Minor', 'Patch') {
        if ($Left.$part -ne $Right.$part) {
            return $(if ($Left.$part -lt $Right.$part) { -1 } else { 1 })
        }
    }

    # A version without a prerelease is higher than the same version with one.
    if (-not $Left.IsPrerelease -or -not $Right.IsPrerelease) {
        if ($Left.IsPrerelease -eq $Right.IsPrerelease) {
            return 0
        }

        return $(if ($Left.IsPrerelease) { -1 } else { 1 })
    }

    $leftIds = $Left.Prerelease.Split('.')
    $rightIds = $Right.Prerelease.Split('.')
    for ($i = 0; $i -lt [Math]::Min($leftIds.Count, $rightIds.Count); $i++) {
        $l = $leftIds[$i]
        $r = $rightIds[$i]
        $lNumeric = $l -match '^[0-9]+$'
        $rNumeric = $r -match '^[0-9]+$'
        if ($lNumeric -and $rNumeric) {
            $order = [System.Numerics.BigInteger]::Parse($l).CompareTo([System.Numerics.BigInteger]::Parse($r))
        }
        elseif ($lNumeric) {
            # Numeric identifiers are lower than alphanumeric ones.
            $order = -1
        }
        elseif ($rNumeric) {
            $order = 1
        }
        else {
            $order = [string]::CompareOrdinal($l, $r)
        }

        if ($order -ne 0) {
            return [Math]::Sign($order)
        }
    }

    # Equal so far: the longer list of identifiers is higher.
    return [Math]::Sign($leftIds.Count - $rightIds.Count)
}
