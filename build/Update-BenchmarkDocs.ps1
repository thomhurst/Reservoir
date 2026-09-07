[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ResultsDirectory,

    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot),

    [string] $Commit,

    [string] $GeneratedAt,

    [string] $ResultsUrl,

    # Publish one explicitly selected runtime; retain other runtimes in the raw reports.
    [ValidatePattern('^\.NET \d+\.\d+$')]
    [string] $Runtime = '.NET 10.0',

    # Required only when the selected runtime has multiple measurement jobs.
    [string] $Job
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$culture = [System.Globalization.CultureInfo]::InvariantCulture
$newLine = "`n"
$repositoryPath = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$resultsPath = (Resolve-Path -LiteralPath $ResultsDirectory).Path

if (-not $resultsPath.StartsWith($repositoryPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw "The results directory must be inside the repository: $resultsPath"
}

function Import-BenchmarkReport {
    param([Parameter(Mandatory)][string] $BenchmarkName)

    $path = Join-Path $resultsPath "Reservoir.Benchmarks.$BenchmarkName-report.csv"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Benchmark report not found: $path"
    }

    return @(Import-Csv -LiteralPath $path)
}

function Get-BenchmarkRow {
    param(
        [Parameter(Mandatory)][object[]] $Rows,
        [Parameter(Mandatory)][string] $Method,
        [hashtable] $Properties = @{}
    )

    $qualifiers = @{ Runtime = $Runtime; Job = $Job }
    foreach ($entry in $Properties.GetEnumerator()) {
        $qualifiers[$entry.Key] = $entry.Value
    }

    $matches = @($Rows | Where-Object {
        if ($_.Method -ne $Method) {
            return $false
        }

        foreach ($entry in $qualifiers.GetEnumerator()) {
            $property = $_.PSObject.Properties[$entry.Key]
            if ($null -eq $property -or $property.Value -ne [string] $entry.Value) {
                return $false
            }
        }

        return $true
    })

    if ($matches.Count -ne 1) {
        $description = @($qualifiers.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ', '
        throw "Expected one '$Method' result ($description), found $($matches.Count)."
    }

    return $matches[0]
}

function Read-BenchmarkValue {
    param(
        [Parameter(Mandatory)][string] $Value,
        [Parameter(Mandatory)][string] $Description
    )

    if ($Value -notmatch '^(?<number>[\d,]+(?:\.\d+)?)\s+(?<unit>\S+)$') {
        throw "Unexpected ${Description}: $Value"
    }

    $number = [double]::Parse(
        $Matches.number.Replace(',', ''),
        [System.Globalization.NumberStyles]::AllowDecimalPoint,
        $culture)

    return [pscustomobject] @{ Number = $number; Unit = $Matches.unit }
}

function Format-Duration {
    param([Parameter(Mandatory)][string] $Value)

    $parsed = Read-BenchmarkValue $Value 'benchmark duration'
    return '{0} {1}' -f $parsed.Number.ToString('N2', $culture), $parsed.Unit
}

function Get-DurationNanoseconds {
    param([Parameter(Mandatory)][string] $Value)

    $parsed = Read-BenchmarkValue $Value 'benchmark duration'
    $scale = switch -CaseSensitive ($parsed.Unit) {
        'ps' { 0.001 }
        'ns' { 1.0 }
        'us' { 1000.0 }
        'µs' { 1000.0 }
        'μs' { 1000.0 }
        'ms' { 1000000.0 }
        's' { 1000000000.0 }
        default { throw "Unexpected benchmark duration unit: $($parsed.Unit)" }
    }
    return $parsed.Number * $scale
}

function Format-CoreRatio {
    param([Parameter(Mandatory)][object] $Row)

    # BDN can normalize multi-runtime ratios against the other runtime's New job.
    # Published tables compare each selected method with New in that same job.
    $baselineMean = Get-DurationNanoseconds $coreNew.Mean
    if ($baselineMean -le 0) {
        throw 'The selected New benchmark must have a positive mean.'
    }
    $ratio = (Get-DurationNanoseconds $Row.Mean) / $baselineMean
    return $ratio.ToString('F2', $culture)
}

function Format-Allocation {
    param([Parameter(Mandatory)][string] $Value)

    $parsed = Read-BenchmarkValue $Value 'allocation value'
    $number = $parsed.Number
    $format = if ($number -eq [Math]::Truncate($number)) { 'N0' } else { 'N2' }

    return '{0} {1}' -f $number.ToString($format, $culture), $parsed.Unit
}

function Get-UpdatedMarkedSection {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $OriginalText,
        [Parameter(Mandatory)][string] $Marker,
        [Parameter(Mandatory)][string] $Content
    )

    $startMarker = "<!-- ${Marker}_START -->"
    $endMarker = "<!-- ${Marker}_END -->"
    $text = $OriginalText.Replace("`r`n", "`n")
    $pattern = '(?s)' + [regex]::Escape($startMarker) + '.*?' + [regex]::Escape($endMarker)
    $matches = [regex]::Matches($text, $pattern)

    if ($matches.Count -ne 1) {
        throw "Expected one $Marker section in $Path, found $($matches.Count)."
    }

    $replacement = $startMarker + $newLine + $Content.Trim() + $newLine + $endMarker
    return [regex]::Replace(
        $text,
        $pattern,
        [System.Text.RegularExpressions.MatchEvaluator] { param($match) $replacement })
}

$coreRows = Import-BenchmarkReport 'CorePoolComparisonBenchmarks'
$allocationRows = Import-BenchmarkReport 'CollectionPoolAllocationBenchmarks'
$listRows = Import-BenchmarkReport 'ListPoolBenchmarks'
$objectPoolRows = Import-BenchmarkReport 'ObjectPoolBenchmarks'
$capacityRows = Import-BenchmarkReport 'ObjectPoolCapacityScalingBenchmarks'
$burstRows = Import-BenchmarkReport 'ObjectPoolBurstBenchmarks'
$stringBuilderRows = Import-BenchmarkReport 'StringBuilderPoolBenchmarks'

if (-not $Job) {
    $jobs = @($coreRows | Where-Object { $_.Runtime -eq $Runtime } |
        ForEach-Object { $_.Job } | Sort-Object -Unique)
    if ($jobs.Count -ne 1 -or [string]::IsNullOrWhiteSpace($jobs[0])) {
        throw "Expected one measurement job for runtime '$Runtime', found $($jobs.Count). Specify -Job when multiple jobs exist."
    }

    $Job = $jobs[0]
}

$coreNew = Get-BenchmarkRow $coreRows 'New'
$coreReservoir = Get-BenchmarkRow $coreRows 'Reservoir'
$coreMicrosoft = Get-BenchmarkRow $coreRows 'MicrosoftExtensionsObjectPool'
$coreConcurrentBag = Get-BenchmarkRow $coreRows 'ConcurrentBag'

$allocationMethods = @(
    'ObjectPool',
    'ListPool',
    'DictionaryPool',
    'HashSetPool',
    'QueuePool',
    'StackPool',
    'StringBuilderPool'
)
$allocationResults = @($allocationMethods | ForEach-Object {
    Get-BenchmarkRow $allocationRows $_
})

$listCounts = @(8, 128, 2048)
$listResults = @($listCounts | ForEach-Object {
    [pscustomobject]@{
        Count = $_
        Baseline = Get-BenchmarkRow $listRows 'NewList' @{ Count = $_ }
        Reservoir = Get-BenchmarkRow $listRows 'Reservoir' @{ Count = $_ }
    }
})

$stringBuilderBaseline = Get-BenchmarkRow $stringBuilderRows 'NewStringBuilder'
$stringBuilderReservoir = Get-BenchmarkRow $stringBuilderRows 'Reservoir'
$stringBuilderTls = Get-BenchmarkRow $stringBuilderRows 'ThreadStaticCache'
$manualRent = Get-BenchmarkRow $objectPoolRows 'RentReturn'
$scopedRent = Get-BenchmarkRow $objectPoolRows 'ScopedRentReturn'
$scopedOutRent = Get-BenchmarkRow $objectPoolRows 'ScopedOutRentReturn'
$capacityResults = @(32, 256, 4096, 65536 | ForEach-Object {
    [pscustomobject]@{
        Capacity = $_
        RentReturn = Get-BenchmarkRow $capacityRows 'RentReturn' @{ Capacity = $_ }
        EmptyRent = Get-BenchmarkRow $capacityRows 'EmptyRent' @{ Capacity = $_ }
        DrainAndRefill = Get-BenchmarkRow $burstRows 'DrainAndRefill' @{ Capacity = $_ }
    }
})

$publishedWarmRows = @(
    $coreReservoir
    $stringBuilderReservoir
    $manualRent
    $scopedRent
    $scopedOutRent
    $stringBuilderTls
) + $allocationResults + @($listResults | ForEach-Object { $_.Reservoir }) +
    @($capacityResults | ForEach-Object { $_.RentReturn; $_.DrainAndRefill })

foreach ($row in $publishedWarmRows) {
    $allocation = $row.PSObject.Properties['Allocated']
    # Require an explicit measured zero in bytes. Missing data, placeholders, unknown units,
    # and positive values (even too small to survive numeric rounding) cannot support a claim.
    if ($null -eq $allocation -or [string] $allocation.Value -cnotmatch '^0(?:\.0+)? B$') {
        throw "A documented warm path requires a measured zero-byte allocation: $($row.Method) (runtime=$Runtime, job=$Job)."
    }
}
$warmAllocation = Format-Allocation $publishedWarmRows[0].Allocated

$metadataPath = Join-Path $resultsPath 'Reservoir.Benchmarks.CorePoolComparisonBenchmarks-report-github.md'
$metadata = Get-Content -LiteralPath $metadataPath -Raw

$environmentMatch = [regex]::Match(
    $metadata,
    '(?m)^BenchmarkDotNet v(?<benchmarkDotNet>[^,\r\n]+),\s*(?<os>[^\r\n(]+)')
$cpuMatch = [regex]::Match(
    $metadata,
    '(?m)^(?<cpu>.*?)(?:\s+\d+(?:\.\d+)?GHz)?,\s+\d+\s+CPU')
$runtimeFamily = $Runtime.Substring('.NET '.Length)
$runtimeMatches = @([regex]::Matches(
    $metadata,
    ('(?m)^\s*' + [regex]::Escape($Job) + '\s+:\s+\.NET\s+(?<runtime>[^\s(]+)')) | Where-Object {
        $version = $_.Groups['runtime'].Value
        $version -eq $runtimeFamily -or $version.StartsWith($runtimeFamily + '.', [StringComparison]::Ordinal)
    })

if (-not $environmentMatch.Success -or -not $cpuMatch.Success -or $runtimeMatches.Count -ne 1) {
    throw "Could not read benchmark environment metadata from $metadataPath"
}

$benchmarkDotNetVersion = $environmentMatch.Groups['benchmarkDotNet'].Value.Trim()
$os = $environmentMatch.Groups['os'].Value.Trim()
$cpu = $cpuMatch.Groups['cpu'].Value.Trim() -replace '^\d+(?:st|nd|rd|th) Gen\s+', ''
$runtimeVersion = $runtimeMatches[0].Groups['runtime'].Value.Trim()
$tick = [char] 96
$environment = "BenchmarkDotNet $benchmarkDotNetVersion $tick$Job$tick, .NET $runtimeVersion, $os, $cpu"

$coreTableRows = @(
    [pscustomobject]@{ Label = '`new`'; Row = $coreNew }
    [pscustomobject]@{ Label = 'Reservoir'; Row = $coreReservoir }
    [pscustomobject]@{ Label = '`Microsoft.Extensions.ObjectPool`'; Row = $coreMicrosoft }
    [pscustomobject]@{ Label = '`ConcurrentBag<T>` pool'; Row = $coreConcurrentBag }
)

$readmeTable = @(
    "${environment}:"
    ''
    '| Method | Mean | Ratio | Allocated |'
    '| --- | ---: | ---: | ---: |'
)

foreach ($item in $coreTableRows) {
    $label = $item.Label
    $row = $item.Row
    $mean = Format-Duration $row.Mean
    $ratio = Format-CoreRatio $row
    $allocated = Format-Allocation $row.Allocated

    if ($row.Method -eq 'Reservoir') {
        $readmeTable += "| **$label** | **$mean** | **$ratio** | **$allocated** |"
    }
    else {
        $readmeTable += "| $label | $mean | $ratio | $allocated |"
    }
}

$docsContent = @(
    "The $($publishedWarmRows.Count) validated warm results below allocated **$warmAllocation per operation**."
    'This covers the Reservoir core, collection, list, warm capacity and burst results, manual/scoped rentals, and the TLS StringBuilder reference. Empty-rent results and other libraries are excluded from this claim.'
    ''
    "Results below select $Runtime and used $environment. Other runtimes remain in the raw reports. Nanosecond timings vary by machine; compare methods within a table."
    ''
)

if ($Commit -and $GeneratedAt -and $ResultsUrl) {
    $shortCommit = if ($Commit.Length -gt 12) { $Commit.Substring(0, 12) } else { $Commit }
    $docsContent += @(
        ':::info Automated results'
        "Generated $GeneratedAt from commit ``$shortCommit``. See the [GitHub Actions run]($ResultsUrl) for logs and downloadable artifacts."
        ':::'
        ''
    )
}

$docsContent += @(
    '## Core pool'
    ''
    'The payload owns a 256-byte buffer. Lower ratio is better; `new` is the baseline.'
    ''
    '| Method | Mean | Ratio | Allocated |'
    '| --- | ---: | ---: | ---: |'
)

foreach ($item in $coreTableRows) {
    $label = $item.Label
    $row = $item.Row
    $docsContent += '| {0} | {1} | {2} | {3} |' -f @(
        $label,
        (Format-Duration $row.Mean),
        (Format-CoreRatio $row),
        (Format-Allocation $row.Allocated)
    )
}

$docsContent += @(
    ''
    '## Capacity scaling'
    ''
    'Small pools use cache-line-separated slots. Large pools use dense striped storage so empty misses and burst transfers do not scan every retained slot. `Drain and refill` rents and returns the full retained capacity once.'
    ''
    '| Retained capacity | Warm rent/return | Empty rent | Drain and refill |'
    '| ---: | ---: | ---: | ---: |'
)

foreach ($result in $capacityResults) {
    $docsContent += '| {0} | {1} | {2} | {3} |' -f @(
        ([int] $result.Capacity).ToString('N0', $culture),
        (Format-Duration $result.RentReturn.Mean),
        (Format-Duration $result.EmptyRent.Mean),
        (Format-Duration $result.DrainAndRefill.Mean)
    )
}

$docsContent += @(
    ''
    '## Warm allocation guarantee'
    ''
    '| Pool | Mean | Allocated |'
    '| --- | ---: | ---: |'
)

foreach ($row in $allocationResults) {
    $docsContent += '| `{0}` | {1} | {2} |' -f @(
        $row.Method,
        (Format-Duration $row.Mean),
        (Format-Allocation $row.Allocated)
    )
}

$docsContent += @(
    ''
    '## Specialized workloads'
    ''
    '| Workload | Baseline | Reservoir | Baseline allocated | Reservoir allocated |'
    '| --- | ---: | ---: | ---: | ---: |'
    ('| `StringBuilder`, append 128 chars | {0} | {1} | {2} | {3} |' -f @(
        (Format-Duration $stringBuilderBaseline.Mean),
        (Format-Duration $stringBuilderReservoir.Mean),
        (Format-Allocation $stringBuilderBaseline.Allocated),
        (Format-Allocation $stringBuilderReservoir.Allocated)
    ))
)

foreach ($result in $listResults) {
    $count = ([int] $result.Count).ToString('N0', $culture)
    $docsContent += '| `List<int>`, {0} items | {1} | {2} | {3} | {4} |' -f @(
        $count,
        (Format-Duration $result.Baseline.Mean),
        (Format-Duration $result.Reservoir.Mean),
        (Format-Allocation $result.Baseline.Allocated),
        (Format-Allocation $result.Reservoir.Allocated)
    )
}

$docsContent += @(
    ''
    ('The single-thread TLS `StringBuilder` cache measured {0} and {1}; it gives up cross-thread reuse and bounded shared capacity. `ObjectPool.RentScoped(out T)` measured {2} and {3}, `RentScoped()` measured {4} and {5}, and manual rent/return measured {6} and {7}. Allocations are per operation in the selected runtime and job.' -f @(
        (Format-Duration $stringBuilderTls.Mean),
        (Format-Allocation $stringBuilderTls.Allocated),
        (Format-Duration $scopedOutRent.Mean),
        (Format-Allocation $scopedOutRent.Allocated),
        (Format-Duration $scopedRent.Mean),
        (Format-Allocation $scopedRent.Allocated),
        (Format-Duration $manualRent.Mean),
        (Format-Allocation $manualRent.Allocated)
    ))
)

$relativeResultsPath = [IO.Path]::GetRelativePath($repositoryPath, $resultsPath).Replace('\', '/')
if ($ResultsUrl) {
    $resultsLink = "Raw Markdown, CSV, and HTML exports—including capacity scaling and 1–32 worker contention results—are available from the [GitHub Actions run]($ResultsUrl)."
}
else {
    $sourceUrl = "https://github.com/thomhurst/Reservoir/tree/main/$relativeResultsPath"
    $resultsLink = 'Raw Markdown, CSV, and HTML exports—including capacity scaling and 1–32 worker contention results—live in [`{0}`]({1}).' -f @(
        $relativeResultsPath,
        $sourceUrl
    )
}

# Validate every marker and construct both documents before writing either file.
$readmePath = Join-Path $repositoryPath 'README.md'
$originalReadme = Get-Content -LiteralPath $readmePath -Raw
$updatedReadme = Get-UpdatedMarkedSection `
    -Path $readmePath `
    -OriginalText $originalReadme `
    -Marker 'BENCHMARK_RESULTS' `
    -Content ($readmeTable -join $newLine)

$docsPath = Join-Path $repositoryPath 'website/docs/benchmarks.md'
$originalDocs = Get-Content -LiteralPath $docsPath -Raw
$updatedDocs = Get-UpdatedMarkedSection `
    -Path $docsPath `
    -OriginalText $originalDocs `
    -Marker 'BENCHMARK_RESULTS' `
    -Content ($docsContent -join $newLine)
$updatedDocs = Get-UpdatedMarkedSection `
    -Path $docsPath `
    -OriginalText $updatedDocs `
    -Marker 'BENCHMARK_RESULTS_LINK' `
    -Content $resultsLink

if ($updatedReadme -ne $originalReadme) {
    Set-Content -LiteralPath $readmePath -Value $updatedReadme -Encoding utf8 -NoNewline
}
if ($updatedDocs -ne $originalDocs) {
    Set-Content -LiteralPath $docsPath -Value $updatedDocs -Encoding utf8 -NoNewline
}

Write-Host "Updated benchmark documentation from $relativeResultsPath"
