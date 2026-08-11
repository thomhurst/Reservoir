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

    $match = [regex]::Match($Value, '^[vV]?(?<major>\d+)\.(?<minor>\d+)(?:\.(?<patch>\d+))?')
    if (-not $match.Success) {
        return $null
    }

    return @(
        [int] $match.Groups['major'].Value
        [int] $match.Groups['minor'].Value
        $(if ($match.Groups['patch'].Success) { [int] $match.Groups['patch'].Value } else { 0 })
    )
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

    $version = ConvertTo-SemanticVersion $(if ($latestVersionTag) { $latestVersionTag } else { $BaseVersion })
    if ($null -eq $version) {
        throw "Base version '$BaseVersion' is not a semantic version."
    }

    $commitRange = if ($latestVersionTag) { "$latestVersionTag..HEAD" } else { 'HEAD' }
    $commitHeight = [int] (Invoke-Git -GitArguments @('rev-list', '--count', $commitRange))
    $commitMessages = Invoke-Git -GitArguments @('log', '--reverse', '--format=%B%x1e', $commitRange)
    $commits = @($commitMessages -split [char] 0x1e | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

    $selectedIncrement = $null
    $selectedCommitIndex = -1
    $incrementRanks = @{ none = 0; patch = 1; minor = 2; major = 3 }

    for ($commitIndex = 0; $commitIndex -lt $commits.Count; $commitIndex++) {
        $markers = [regex]::Matches(
            $commits[$commitIndex],
            '\+semver:\s*(?<increment>major|breaking|minor|feature|patch|fix|none|skip)',
            [Text.RegularExpressions.RegexOptions]::IgnoreCase)

        foreach ($marker in $markers) {
            $candidate = switch ($marker.Groups['increment'].Value.ToLowerInvariant()) {
                { $_ -in @('major', 'breaking') } { 'major'; break }
                { $_ -in @('minor', 'feature') } { 'minor'; break }
                { $_ -in @('none', 'skip') } { 'none'; break }
                default { 'patch' }
            }

            if ($null -eq $selectedIncrement -or $incrementRanks[$candidate] -ge $incrementRanks[$selectedIncrement]) {
                $selectedIncrement = $candidate
                $selectedCommitIndex = $commitIndex
            }
        }
    }

    if ($null -eq $selectedIncrement) {
        $selectedIncrement = 'patch'
    }

    $major, $minor, $patch = $version
    switch ($selectedIncrement) {
        'major' {
            $major++
            $minor = 0
            $patch = [Math]::Max($commits.Count - $selectedCommitIndex - 1, 0)
        }
        'minor' {
            $minor++
            $patch = [Math]::Max($commits.Count - $selectedCommitIndex - 1, 0)
        }
        'patch' { $patch += $commitHeight }
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
