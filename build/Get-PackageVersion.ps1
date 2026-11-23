[CmdletBinding()]
param(
    [string] $BaseVersion = "0.1.0",
    [string[]] $ReleaseBranches = @("main", "master")
)

$ErrorActionPreference = "Stop"

function Invoke-Git {
    param(
        [Parameter(Mandatory)]
        [string[]] $GitArguments,
        [switch] $AllowFailure
    )

    $output = & git @GitArguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        if ($AllowFailure) {
            return $null
        }

        throw "git $($GitArguments -join ' ') failed with exit code $LASTEXITCODE`: $($output -join [Environment]::NewLine)"
    }

    return ($output -join "`n").Trim()
}

function ConvertTo-SemanticVersion {
    param([string] $Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    $match = [regex]::Match($Value, '^[vV]?(?<major>\d+)\.(?<minor>\d+)(?:\.(?<patch>\d+))?(?<prerelease>-[0-9A-Za-z.-]+)?')
    if (-not $match.Success) {
        return $null
    }

    return [pscustomobject] @{
        Major = [int] $match.Groups['major'].Value
        Minor = [int] $match.Groups['minor'].Value
        Patch = $(if ($match.Groups['patch'].Success) { [int] $match.Groups['patch'].Value } else { 0 })
        IsPrerelease = $match.Groups['prerelease'].Success
    }
}

function Get-BranchName {
    foreach ($candidate in @($env:PULL_REQUEST_BRANCH, $env:GITHUB_HEAD_REF, $env:GITHUB_REF_NAME)) {
        if (-not [string]::IsNullOrWhiteSpace($candidate)) {
            return ($candidate.Trim() -replace '^refs/(heads|tags)/', '')
        }
    }

    $branch = Invoke-Git -GitArguments @('branch', '--show-current') -AllowFailure
    if ([string]::IsNullOrWhiteSpace($branch)) {
        $branch = Invoke-Git -GitArguments @('rev-parse', '--abbrev-ref', 'HEAD') -AllowFailure
    }

    if ([string]::IsNullOrWhiteSpace($branch) -or $branch -eq 'HEAD') {
        return 'detached'
    }

    return ($branch.Trim() -replace '^refs/(heads|tags)/', '')
}

$repositoryRoot = Invoke-Git -GitArguments @('rev-parse', '--show-toplevel')
Push-Location $repositoryRoot
try {
    $branchName = Get-BranchName
    $shortCommitHash = Invoke-Git -GitArguments @('rev-parse', '--short=8', 'HEAD')
    $latestVersionTag = Invoke-Git -GitArguments @(
        'describe', '--tags', '--abbrev=0', '--match', 'v[0-9]*', '--match', '[0-9]*'
    ) -AllowFailure

    $baseSemanticVersion = ConvertTo-SemanticVersion $BaseVersion
    if ($null -eq $baseSemanticVersion) {
        throw "Base version '$BaseVersion' is not a semantic version."
    }

    $commitRange = if ($latestVersionTag) { "$latestVersionTag..HEAD" } else { 'HEAD' }
    $commitHeight = [int] (Invoke-Git -GitArguments @('rev-list', '--count', $commitRange))

    # The suggested version is the next patch after the latest release tag, however many commits
    # follow it; commit messages do not choose it. A minor or major release is chosen by editing
    # the tag of the draft release before publishing it (see .github/workflows/release-draft.yml).
    # A prerelease tag such as v2.0.0-beta.1 suggests the release it previews, 2.0.0.
    $latestVersion = ConvertTo-SemanticVersion $latestVersionTag
    if ($null -eq $latestVersion) {
        $major, $minor, $patch = $baseSemanticVersion.Major, $baseSemanticVersion.Minor, $baseSemanticVersion.Patch
    }
    elseif ($latestVersion.IsPrerelease) {
        $major, $minor, $patch = $latestVersion.Major, $latestVersion.Minor, $latestVersion.Patch
    }
    else {
        $major, $minor, $patch = $latestVersion.Major, $latestVersion.Minor, ($latestVersion.Patch + 1)
    }

    $packageVersion = "$major.$minor.$patch"
    if ($branchName -notin $ReleaseBranches) {
        $sanitizedBranch = ($branchName.ToLowerInvariant() -replace '[^0-9a-z-]+', '-').Trim('-')
        if ([string]::IsNullOrWhiteSpace($sanitizedBranch)) {
            $sanitizedBranch = 'branch'
        }

        $packageVersion = "$packageVersion-ci.$sanitizedBranch.$commitHeight.$shortCommitHash"
    }

    Write-Output $packageVersion
}
finally {
    Pop-Location
}
