[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ResultsDirectory,

    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot),

    [string] $Commit,

    [string] $GeneratedAt,

    [string] $ResultsUrl
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

    $matches = @($Rows | Where-Object {
        if ($_.Method -ne $Method) {
            return $false
        }

        foreach ($entry in $Properties.GetEnumerator()) {
            $property = $_.PSObject.Properties[$entry.Key]
            if ($null -eq $property -or $property.Value -ne [string] $entry.Value) {
                return $false
            }
        }

        return $true
    })

    if ($matches.Count -ne 1) {
        $qualifiers = @($Properties.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ', '
        throw "Expected one '$Method' result ($qualifiers), found $($matches.Count)."
    }

    return $matches[0]
}

function Format-Duration {
    param([Parameter(Mandatory)][string] $Value)

    if ($Value -notmatch '^(?<number>[\d,]+(?:\.\d+)?)\s+(?<unit>\S+)$') {
        throw "Unexpected benchmark duration: $Value"
    }

    $number = [double]::Parse(
        $Matches.number.Replace(',', ''),
        [System.Globalization.NumberStyles]::AllowDecimalPoint,
        $culture)

    return '{0} {1}' -f $number.ToString('N2', $culture), $Matches.unit
}

function Format-Ratio {
    param([Parameter(Mandatory)][string] $Value)

    $number = [double]::Parse($Value, [System.Globalization.NumberStyles]::Float, $culture)
    return $number.ToString('F2', $culture)
}

function Format-Allocation {
    param([Parameter(Mandatory)][string] $Value)

    if ($Value -notmatch '^(?<number>[\d,]+(?:\.\d+)?)\s+(?<unit>\S+)$') {
        throw "Unexpected allocation value: $Value"
    }

    $number = [double]::Parse(
        $Matches.number.Replace(',', ''),
        [System.Globalization.NumberStyles]::AllowDecimalPoint,
        $culture)
    $format = if ($number -eq [Math]::Truncate($number)) { 'N0' } else { 'N2' }

    return '{0} {1}' -f $number.ToString($format, $culture), $Matches.unit
}

function Update-MarkedSection {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Marker,
        [Parameter(Mandatory)][string] $Content
    )

    $startMarker = "<!-- ${Marker}_START -->"
    $endMarker = "<!-- ${Marker}_END -->"
    $originalText = Get-Content -LiteralPath $Path -Raw
    $text = $originalText.Replace("`r`n", "`n")
    $pattern = '(?s)' + [regex]::Escape($startMarker) + '.*?' + [regex]::Escape($endMarker)
    $matches = [regex]::Matches($text, $pattern)

    if ($matches.Count -ne 1) {
        throw "Expected one $Marker section in $Path, found $($matches.Count)."
    }

    $replacement = $startMarker + $newLine + $Content.Trim() + $newLine + $endMarker
    $updated = [regex]::Replace(
        $text,
        $pattern,
        [System.Text.RegularExpressions.MatchEvaluator] { param($match) $replacement })

    if ($updated -ne $originalText) {
        Set-Content -LiteralPath $Path -Value $updated -Encoding utf8 -NoNewline
    }
}

$coreRows = Import-BenchmarkReport 'CorePoolComparisonBenchmarks'
$allocationRows = Import-BenchmarkReport 'CollectionPoolAllocationBenchmarks'
$listRows = Import-BenchmarkReport 'ListPoolBenchmarks'
$objectPoolRows = Import-BenchmarkReport 'ObjectPoolBenchmarks'
$stringBuilderRows = Import-BenchmarkReport 'StringBuilderPoolBenchmarks'

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

$publishedWarmRows = @(
    $coreReservoir
    $stringBuilderReservoir
    $manualRent
    $scopedRent
) + $allocationResults + @($listResults | ForEach-Object { $_.Reservoir })

$allocatingWarmRows = @($publishedWarmRows | Where-Object { $_.Allocated -ne '0 B' })
if ($allocatingWarmRows.Count -gt 0) {
    $methods = @($allocatingWarmRows | ForEach-Object { $_.Method }) -join ', '
    throw "A documented warm path allocated memory: $methods"
}

$metadataPath = Join-Path $resultsPath 'Reservoir.Benchmarks.CorePoolComparisonBenchmarks-report-github.md'
$metadata = Get-Content -LiteralPath $metadataPath -Raw

$environmentMatch = [regex]::Match(
    $metadata,
    '(?m)^BenchmarkDotNet v(?<benchmarkDotNet>[^,\r\n]+),\s*(?<os>[^\r\n(]+)')
$cpuMatch = [regex]::Match(
    $metadata,
    '(?m)^(?<cpu>.*?)(?:\s+\d+(?:\.\d+)?GHz)?,\s+\d+\s+CPU')
$runtimeMatch = [regex]::Match(
    $metadata,
    '(?m)^\s*\[Host\]\s+:\s+\.NET\s+(?<runtime>[^\s(]+)')

if (-not $environmentMatch.Success -or -not $cpuMatch.Success -or -not $runtimeMatch.Success) {
    throw "Could not read benchmark environment metadata from $metadataPath"
}

$benchmarkDotNetVersion = $environmentMatch.Groups['benchmarkDotNet'].Value.Trim()
$os = $environmentMatch.Groups['os'].Value.Trim()
$cpu = $cpuMatch.Groups['cpu'].Value.Trim() -replace '^\d+(?:st|nd|rd|th) Gen\s+', ''
$runtime = $runtimeMatch.Groups['runtime'].Value.Trim()
$job = $coreRows[0].Job
$tick = [char] 96
$environment = "BenchmarkDotNet $benchmarkDotNetVersion $tick$job$tick, .NET $runtime, $os, $cpu"

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
    $ratio = Format-Ratio $row.Ratio
    $allocated = Format-Allocation $row.Allocated

    if ($row.Method -eq 'Reservoir') {
        $readmeTable += "| **$label** | **$mean** | **$ratio** | **$allocated** |"
    }
    else {
        $readmeTable += "| $label | $mean | $ratio | $allocated |"
    }
}

$docsContent = @(
    'Every measured warm Reservoir path allocated **0 B per operation**.'
    ''
    "Results below used $environment. Nanosecond timings vary by machine; compare methods within a table."
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
        (Format-Ratio $row.Ratio),
        (Format-Allocation $row.Allocated)
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
    ('The single-thread TLS `StringBuilder` cache measured {0} and 0 B; it gives up cross-thread reuse and bounded shared capacity. Scoped leases measured {1} versus {2} for manual rent/return, with 0 B allocated on both paths.' -f @(
        (Format-Duration $stringBuilderTls.Mean),
        (Format-Duration $scopedRent.Mean),
        (Format-Duration $manualRent.Mean)
    ))
)

$relativeResultsPath = [IO.Path]::GetRelativePath($repositoryPath, $resultsPath).Replace('\', '/')
if ($ResultsUrl) {
    $resultsLink = "Raw Markdown, CSV, and HTML exports—including 1–32 worker contention results—are available from the [GitHub Actions run]($ResultsUrl)."
}
else {
    $sourceUrl = "https://github.com/thomhurst/Reservoir/tree/main/$relativeResultsPath"
    $resultsLink = 'Raw Markdown, CSV, and HTML exports—including 1–32 worker contention results—live in [`{0}`]({1}).' -f @(
        $relativeResultsPath,
        $sourceUrl
    )
}

Update-MarkedSection `
    -Path (Join-Path $repositoryPath 'README.md') `
    -Marker 'BENCHMARK_RESULTS' `
    -Content ($readmeTable -join $newLine)

$docsPath = Join-Path $repositoryPath 'website/docs/benchmarks.md'
Update-MarkedSection `
    -Path $docsPath `
    -Marker 'BENCHMARK_RESULTS' `
    -Content ($docsContent -join $newLine)
Update-MarkedSection `
    -Path $docsPath `
    -Marker 'BENCHMARK_RESULTS_LINK' `
    -Content $resultsLink

Write-Host "Updated benchmark documentation from $relativeResultsPath"
