# Returns each name in -Names that has no check run completed with SUCCESS.
# A skipped run does not count: draft PR events skip the build and test jobs, so right
# after a PR is marked ready its rollup can hold only skipped CI checks, which would
# otherwise read as passing.
function Get-MissingRequiredCheck {
    [CmdletBinding()]
    param(
        [AllowNull()]$Checks,
        [Parameter(Mandatory)][string[]]$Names
    )

    foreach ($name in $Names) {
        $succeeded = @($Checks | Where-Object {
                $null -ne $_ -and
                $_.PSObject.Properties['name'] -and $_.name -eq $name -and
                $_.status -eq 'COMPLETED' -and $_.conclusion -eq 'SUCCESS'
            })
        if ($succeeded.Count -eq 0) { $name }
    }
}
