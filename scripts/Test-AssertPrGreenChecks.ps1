$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'AssertPrGreenChecks.ps1')

function New-CheckRun([string]$Name, [string]$Conclusion, [string]$Status = 'COMPLETED') {
    [pscustomobject]@{ name = $Name; status = $Status; conclusion = $Conclusion }
}

function New-StatusContext([string]$Context, [string]$State) {
    [pscustomobject]@{ context = $Context; state = $State }
}

$review = New-CheckRun 'claude-review' 'SUCCESS'
$ciPassed = New-CheckRun 'CI Passed' 'SUCCESS'
$draftAggregator = New-CheckRun "github.event_name == 'pull_request' && 'CI Passed' || 'CI Passed (draft)'" 'SKIPPED'

$cases = @(
    # Merge now: every check terminal and passing, and the required aggregator succeeded.
    @{
        Name = 'merge passes when the aggregator succeeded'
        Checks = @($review, $ciPassed, (New-CheckRun 'Publish' 'SKIPPED'))
        ForAutoMerge = $false
        Problems = @()
    },
    @{
        Name = 'merge fails when only the draft run skipped the aggregator'
        Checks = @($review, $draftAggregator, (New-CheckRun 'Pack' 'SKIPPED'))
        ForAutoMerge = $false
        Problems = @('CI Passed: required check has not succeeded')
    },
    @{
        Name = 'merge passes when a ready run succeeded beside a stale skipped draft run'
        Checks = @($review, $draftAggregator, (New-CheckRun 'Pack' 'SKIPPED'), $ciPassed)
        ForAutoMerge = $false
        Problems = @()
    },
    @{
        Name = 'merge fails on a skipped check that carries the required name'
        Checks = @($review, (New-CheckRun 'CI Passed' 'SKIPPED'))
        ForAutoMerge = $false
        Problems = @('CI Passed: required check has not succeeded')
    },
    @{
        Name = 'merge fails on a pending check'
        Checks = @($review, $ciPassed, (New-CheckRun 'Test (ubuntu-latest)' $null 'IN_PROGRESS'))
        ForAutoMerge = $false
        Problems = @('Test (ubuntu-latest): status=IN_PROGRESS')
    },
    @{
        Name = 'merge ignores a status context with the required name'
        Checks = @($review, (New-StatusContext 'CI Passed' 'SUCCESS'))
        ForAutoMerge = $false
        Problems = @('CI Passed: required check has not succeeded')
    },
    @{
        Name = 'merge fails on an empty rollup'
        Checks = @()
        ForAutoMerge = $false
        Problems = @('no checks registered on this head')
    },
    # Arm auto-merge: GitHub waits for CI, but nothing may have failed and the review must exist.
    @{
        Name = 'arm passes right after ready, before CI registers'
        Checks = @($review, $draftAggregator, (New-CheckRun 'Pack' 'SKIPPED'))
        ForAutoMerge = $true
        Problems = @()
    },
    @{
        Name = 'arm passes while CI is running'
        Checks = @($review, (New-CheckRun 'Test (ubuntu-latest)' $null 'IN_PROGRESS'), (New-CheckRun 'CI Passed' $null 'QUEUED'), (New-StatusContext 'external' 'PENDING'))
        ForAutoMerge = $true
        Problems = @()
    },
    @{
        Name = 'arm fails on a failed check'
        Checks = @($review, (New-CheckRun 'Test (windows-latest)' 'FAILURE'))
        ForAutoMerge = $true
        Problems = @('Test (windows-latest): conclusion=FAILURE')
    },
    @{
        Name = 'arm fails on a cancelled check'
        Checks = @($review, (New-CheckRun 'CI Passed' 'CANCELLED'))
        ForAutoMerge = $true
        Problems = @('CI Passed: conclusion=CANCELLED')
    },
    @{
        Name = 'arm fails while the review is running'
        Checks = @((New-CheckRun 'claude-review' $null 'IN_PROGRESS'))
        ForAutoMerge = $true
        Problems = @('claude-review: status=IN_PROGRESS', 'claude-review: review check has not succeeded on this head')
    },
    @{
        Name = 'arm fails when the review was skipped'
        Checks = @((New-CheckRun 'claude-review' 'SKIPPED'))
        ForAutoMerge = $true
        Problems = @('claude-review: review check has not succeeded on this head')
    },
    @{
        Name = 'arm fails without a review check'
        Checks = @($ciPassed)
        ForAutoMerge = $true
        Problems = @('claude-review: review check has not succeeded on this head')
    },
    @{
        Name = 'arm fails on an unknown status'
        Checks = @($review, (New-CheckRun 'odd' $null 'MYSTERY'))
        ForAutoMerge = $true
        Problems = @('odd: status=MYSTERY')
    }
)

foreach ($case in $cases) {
    $problems = @(Get-CheckProblem -Checks $case.Checks -RequiredChecks @('CI Passed') -ReviewCheck 'claude-review' -ForAutoMerge:$case.ForAutoMerge)
    if (($problems -join ' | ') -ne ($case.Problems -join ' | ')) {
        throw "Case '$($case.Name)' expected [$($case.Problems -join ' | ')], got [$($problems -join ' | ')]"
    }
}

Write-Host "OK check gate tests passed ($($cases.Count) cases)."
