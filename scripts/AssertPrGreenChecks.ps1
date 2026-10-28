# Check classification for Assert-PrGreen.ps1. Returns one problem string per reason the
# rollup does not allow the requested action; an empty result allows it.
#
# Merge now (default): every check is terminal and passing (CheckRun: COMPLETED with
# SUCCESS/SKIPPED/NEUTRAL; StatusContext: SUCCESS), and every -RequiredChecks name has a
# check run that SUCCEEDED. Skipped is not enough there: draft PR events skip CI, so right
# after a PR is marked ready its rollup can hold only skipped CI checks.
#
# Arm auto-merge (-ForAutoMerge): GitHub waits for the ruleset's required checks itself, so
# queued or running checks are allowed. Nothing may have failed, and the -ReviewCheck run
# must have succeeded on this head so its findings exist before arming. Unknown states fail
# closed rather than being mistaken for pending work.
function Get-CheckProblem {
    [CmdletBinding()]
    param(
        [AllowNull()]$Checks,
        [Parameter(Mandatory)][string[]]$RequiredChecks,
        [Parameter(Mandatory)][string]$ReviewCheck,
        [switch]$ForAutoMerge
    )

    $checkList = @($Checks | Where-Object { $null -ne $_ })
    if ($checkList.Count -eq 0) {
        return 'no checks registered on this head'
    }

    $passingConclusions = @('SUCCESS', 'SKIPPED', 'NEUTRAL')
    $pendingStatuses = @('QUEUED', 'IN_PROGRESS', 'WAITING', 'REQUESTED', 'PENDING')
    $pendingStates = @('PENDING', 'EXPECTED')

    foreach ($c in $checkList) {
        if ($c.PSObject.Properties['status'] -and $null -ne $c.status) {
            if ($c.status -eq 'COMPLETED') {
                if ($passingConclusions -notcontains $c.conclusion) { "$($c.name): conclusion=$($c.conclusion)" }
            }
            elseif (-not ($ForAutoMerge -and $c.name -ne $ReviewCheck -and $pendingStatuses -contains $c.status)) {
                "$($c.name): status=$($c.status)"
            }
        }
        elseif ($c.PSObject.Properties['state'] -and $null -ne $c.state) {
            if ($c.state -ne 'SUCCESS' -and -not ($ForAutoMerge -and $pendingStates -contains $c.state)) {
                "$($c.context): state=$($c.state)"
            }
        }
        else {
            "unrecognized check entry: $($c | ConvertTo-Json -Compress)"
        }
    }

    $mustSucceed = if ($ForAutoMerge) { @($ReviewCheck) } else { $RequiredChecks }
    foreach ($name in $mustSucceed) {
        $succeeded = @($checkList | Where-Object {
                $_.PSObject.Properties['name'] -and $_.name -eq $name -and
                $_.status -eq 'COMPLETED' -and $_.conclusion -eq 'SUCCESS'
            })
        if ($succeeded.Count -eq 0) {
            if ($ForAutoMerge) { "${name}: review check has not succeeded on this head" }
            else { "${name}: required check has not succeeded" }
        }
    }
}
