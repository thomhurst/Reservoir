# Assert-PrGreen.ps1
# Fail-closed merge gate for the issue-pr-loop skill.
#
# Exits 0 ONLY when PR #<N> is provably safe to merge:
#   - state OPEN and not a draft
#   - mergeable == MERGEABLE
#   - mergeStateStatus == CLEAN
#   - every status check is terminal AND passing (CheckRun: status=COMPLETED and
#     conclusion in SUCCESS/SKIPPED/NEUTRAL; StatusContext: state=SUCCESS)
#   - the `CI Passed` aggregator has a run that succeeded (skipped is not enough)
#   - no current-head top-level review has actionable finding headings
#   - no unresolved review threads
#
# -ForAutoMerge is the gate for arming native auto-merge (Enable-AutoMerge.ps1). The
# `main` ruleset makes GitHub wait for `CI Passed` itself, so CI may still be queued or
# running, and mergeStateStatus is not checked, but:
#   - the PR is open, not a draft, and MERGEABLE
#   - no check has failed, been cancelled, or reported an unknown state
#   - the `claude-review` check succeeded on the current head, so its findings exist
#   - the review-body and review-thread checks below pass
#
# Any other state — a failed check, an empty rollup, an unresolved thread, or an API
# hiccup — exits 1 so the caller MUST NOT merge or arm. The default on uncertainty is DENY.
#
# Enforce the queue's mechanical merge requirements independently of GitHub
# branch protection. The skill also checks judgment-based review signals.
#
# Usage: pwsh scripts/Assert-PrGreen.ps1 -Pr 1234 [-Repo owner/name] [-ForAutoMerge]
# Exit:  0 = safe to merge (or arm); 1 = DO NOT MERGE (reason printed to stderr).

[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$Pr,
    [string]$Repo,
    [switch]$ForAutoMerge
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'AssertPrGreenChecks.ps1')
. (Join-Path $PSScriptRoot 'AssertPrGreenReviewHeuristics.ps1')
. (Join-Path $PSScriptRoot 'AssertPrGreenReviewThreads.ps1')

function Deny([string]$reason) {
    [Console]::Error.WriteLine("DO NOT MERGE #${Pr} -- $reason")
    exit 1
}

$repoArgs = @()
if ($Repo) { $repoArgs = @('--repo', $Repo) }

# Re-fetch fresh — survey output goes stale within seconds.
$raw = gh pr view $Pr @repoArgs --json number,state,isDraft,mergeable,mergeStateStatus,statusCheckRollup,latestReviews,commits 2>$null
if ($LASTEXITCODE -ne 0) { Deny "gh pr view failed (exit $LASTEXITCODE)" }
try {
    $view = $raw | ConvertFrom-Json
}
catch {
    Deny "could not parse PR state: $($_.Exception.Message)"
}

if ($view.state -ne 'OPEN') { Deny "state=$($view.state) (need OPEN)" }
if ($view.isDraft) { Deny "PR is a draft; mark it ready only after the draft review lifecycle approves its head" }
if ($view.mergeable -ne 'MERGEABLE') { Deny "mergeable=$($view.mergeable) (need MERGEABLE)" }
if (-not $ForAutoMerge -and $view.mergeStateStatus -ne 'CLEAN') { Deny "mergeStateStatus=$($view.mergeStateStatus) (need CLEAN)" }

$checks = @($view.statusCheckRollup | Where-Object { $null -ne $_ })
# `CI Passed` is ci-cd.yml's aggregator, which needs every CI job and is the `main` ruleset's
# required check. A draft run skips it under a different name, so it cannot satisfy this.
$problems = @(Get-CheckProblem -Checks $checks -RequiredChecks @('CI Passed') -ReviewCheck 'claude-review' -ForAutoMerge:$ForAutoMerge)
if ($problems.Count -gt 0) {
    Deny ("checks not green: " + ($problems -join '; '))
}

$headCommit = @($view.commits | Where-Object { $null -ne $_ }) | Select-Object -Last 1
$headCommittedAt = if ($headCommit) { ConvertTo-UtcDateTimeOffset $headCommit.committedDate } else { $null }

# Top-level review comments are not review threads, so GitHub does not expose a
# resolved flag for them. Fail closed on common review-finding shapes instead
# of treating a COMMENTED review as automatically safe. A stale Claude review
# from before the current head commit is ignored only when the current-head
# claude-review check completed green.
$latestReviews = @($view.latestReviews | Where-Object { $null -ne $_ })
$requestedChanges = @($latestReviews | Where-Object { $_.state -eq 'CHANGES_REQUESTED' })
if ($requestedChanges.Count -gt 0) {
    $authors = ($requestedChanges | ForEach-Object { $_.author.login }) -join ', '
    Deny "latest review requests changes from: $authors"
}

$actionableReviews = @()
foreach ($review in $latestReviews) {
    if ($review.state -ne 'COMMENTED') { continue }
    if (Test-StaleBotReviewCanBeIgnored -Review $review -HeadCommitCommittedAt $headCommittedAt -Checks $checks) {
        continue
    }

    $body = [string]$review.body
    $reason = Get-ActionableReviewBodyReason -Body $body
    if ($reason) {
        $actionableReviews += "$($review.author.login): $reason"
    }
}
if ($actionableReviews.Count -gt 0) {
    Deny ("latest top-level review comment has actionable finding(s): " + ($actionableReviews -join '; '))
}

# Unresolved review threads block merge even when a review's state is COMMENTED.
if ($Repo) {
    $owner, $name = $Repo -split '/', 2
}
else {
    $nwo = gh repo view --json nameWithOwner --jq '.nameWithOwner' 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $nwo) { Deny "could not resolve repo for review-thread check" }
    $owner, $name = $nwo.Trim() -split '/', 2
}

$query = 'query($owner:String!,$repo:String!,$pr:Int!,$cursor:String){repository(owner:$owner,name:$repo){pullRequest(number:$pr){reviewThreads(first:100,after:$cursor){pageInfo{hasNextPage endCursor} nodes{isResolved}}}}}'
try {
    $reviewThreads = @(Get-AllReviewThreads -FetchPage {
        param($cursor)

        $ghArgs = @(
            'api', 'graphql',
            '-f', "query=$query",
            '-F', "owner=$owner",
            '-F', "repo=$name",
            '-F', "pr=$Pr"
        )
        if ($null -ne $cursor) {
            $ghArgs += @('-f', "cursor=$cursor")
        }

        $threadsRaw = & gh @ghArgs 2>$null
        if ($LASTEXITCODE -ne 0) {
            throw "gh api graphql failed (exit $LASTEXITCODE)."
        }

        $page = ($threadsRaw | ConvertFrom-Json).data.repository.pullRequest.reviewThreads
        if ($null -eq $page) {
            throw 'GraphQL response did not contain review threads.'
        }

        return $page
    })
}
catch {
    Deny "could not fetch or parse review threads: $($_.Exception.Message)"
}
$unresolved = @($reviewThreads | Where-Object { -not $_.isResolved }).Count
if ($unresolved -gt 0) { Deny "$unresolved unresolved review thread(s)" }

if ($ForAutoMerge) {
    Write-Host "OK #${Pr} -- ready, MERGEABLE, no failed checks, claude-review succeeded on this head, no unresolved threads. Safe to arm auto-merge."
}
else {
    Write-Host "OK #${Pr} -- MERGEABLE, CLEAN, $($checks.Count) check(s) green, no unresolved threads. Safe to merge."
}
exit 0
