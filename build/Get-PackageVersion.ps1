[CmdletBinding()]
param(
    [string] $BaseVersion = "0.1.0",
    [string[]] $ReleaseBranches = @("main", "master"),
    # A release run passes the published release's tag: it is validated and printed without its
    # `v` prefix instead of computing a version from git history.
    [string] $ReleaseTag,
    # With -ReleaseTag: print the tag's version only when it is valid and SemVer-greater than
    # this version, and print nothing otherwise (the release draft keeps such an override).
    [string] $HigherThan,
    # Print the latest stable (non-prerelease) version tag reachable from HEAD, the release the
    # suggestion counts from, or nothing when there is none. The release draft starts its notes there.
    [switch] $LatestStableTag
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

. (Join-Path $PSScriptRoot 'SemanticVersion.ps1')

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

if ($PSBoundParameters.ContainsKey('HigherThan')) {
    $floor = ConvertTo-SemanticVersion $HigherThan
    if ($null -eq $floor) {
        throw "Version '$HigherThan' is not a version."
    }

    $candidate = ConvertTo-SemanticVersion $ReleaseTag
    if ($null -ne $candidate -and (Compare-SemanticVersion $candidate $floor) -gt 0) {
        Write-Output $candidate.Text
    }

    return
}

if ($PSBoundParameters.ContainsKey('ReleaseTag')) {
    $releaseVersion = ConvertTo-SemanticVersion $ReleaseTag
    if ($null -eq $releaseVersion) {
        throw "Release tag '$ReleaseTag' is not a version. Use a tag such as v1.2.3 or v1.2.3-beta.1 (no leading zeros, no build metadata, each number at most $MaxVersionPart)."
    }

    Write-Output $releaseVersion.Text
    return
}

$repositoryRoot = Invoke-Git -GitArguments @('rev-parse', '--show-toplevel')
Push-Location $repositoryRoot
try {
    $branchName = Get-BranchName
    $shortCommitHash = Invoke-Git -GitArguments @('rev-parse', '--short=8', 'HEAD')
    $latestVersionTag = Invoke-Git -GitArguments @(
        'describe', '--tags', '--abbrev=0', '--match', 'v[0-9]*', '--match', '[0-9]*'
    ) -AllowFailure

    if ($LatestStableTag) {
        # After a prerelease such as v2.0.0-beta.1 the suggestion is 2.0.0, so its notes cover
        # everything since the last stable release, prerelease changes included.
        $stableTag = Invoke-Git -GitArguments @(
            'describe', '--tags', '--abbrev=0', '--match', 'v[0-9]*', '--match', '[0-9]*', '--exclude', '*-*'
        ) -AllowFailure
        if ($stableTag) {
            if ($null -eq (ConvertTo-SemanticVersion $stableTag)) {
                throw "Latest stable release tag '$stableTag' is not a version."
            }

            Write-Output $stableTag
        }

        return
    }

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
    if ($latestVersionTag -and $null -eq $latestVersion) {
        throw "Latest release tag '$latestVersionTag' is not a version, so the next version cannot be suggested."
    }

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
