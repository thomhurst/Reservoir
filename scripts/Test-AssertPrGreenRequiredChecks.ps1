$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'AssertPrGreenRequiredChecks.ps1')

function New-CheckRun([string]$Name, [string]$Conclusion, [string]$Status = 'COMPLETED') {
    [pscustomobject]@{ name = $Name; status = $Status; conclusion = $Conclusion }
}

$cases = @(
    @{
        Name = 'passes when the required check succeeded'
        Checks = @((New-CheckRun 'Pack' 'SUCCESS'), (New-CheckRun 'Publish' 'SKIPPED'))
        Missing = @()
    },
    @{
        Name = 'fails when only the draft run skipped the required check'
        Checks = @((New-CheckRun 'Pack' 'SKIPPED'), (New-CheckRun 'claude-review' 'SUCCESS'))
        Missing = @('Pack')
    },
    @{
        Name = 'passes when a ready run succeeded beside a stale skipped draft run'
        Checks = @((New-CheckRun 'Pack' 'SKIPPED'), (New-CheckRun 'Pack' 'SUCCESS'))
        Missing = @()
    },
    @{
        Name = 'fails when the required check is absent'
        Checks = @((New-CheckRun 'claude-review' 'SUCCESS'))
        Missing = @('Pack')
    },
    @{
        Name = 'fails when the required check is still running'
        Checks = @((New-CheckRun 'Pack' $null 'IN_PROGRESS'))
        Missing = @('Pack')
    },
    @{
        Name = 'ignores a status context with the same name'
        Checks = @([pscustomobject]@{ context = 'Pack'; state = 'SUCCESS' })
        Missing = @('Pack')
    }
)

foreach ($case in $cases) {
    $missing = @(Get-MissingRequiredCheck -Checks $case.Checks -Names @('Pack'))
    if (($missing -join ',') -ne ($case.Missing -join ',')) {
        throw "Case '$($case.Name)' expected missing [$($case.Missing -join ',')], got [$($missing -join ',')]"
    }
}

Write-Host "OK required check tests passed ($($cases.Count) cases)."
