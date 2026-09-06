# Remember successful remote rent stripes

When callers rent on one set of threads and completion threads return to another, every rent can repeat the same scan from its home stripe. The large-pool store now tries the last successful remote stripe after a home miss, before its ordinary scan. The hint is a thread-static integer shared by stores of the same item type; it is bounds-checked against each store and retains no objects or store references. Hint lookup and the fallback scan are kept out of the inlined home-stripe path. Ownership still transfers through the existing CAS and stamped-node operations.

## Controlled before/after comparison

Baseline: `ad01a2c181b656636f0f3973ff52903421600635` (`main`). A temporary BenchmarkDotNet project linked the PR's `ObjectPoolRemoteHandoffBenchmarks.cs` and `BenchmarkWorkerGroup.cs`. Before jobs referenced saved baseline DLLs using a conditional `BaselineDll` MSBuild property; after jobs referenced the working library project. All four jobs ran in the same BenchmarkDotNet invocation.

Four producer/consumer pairs, capacity 4096, 20 stripes, ordinal distance 16. Each pair has a one-item channel; the pool starts with 32 objects. Each invocation contains 65,536 aggregate handoffs. Reported time includes channel operations, spinning, pool calls, and amortized worker coordination; it is throughput-normalized elapsed time, not per-request latency.

| Runtime | Before Mean ± StdDev | After Mean ± StdDev | After / before | Worker allocation |
|---|---:|---:|---:|---:|
| .NET 10.0.11 | 70.27 ± 0.648 ns | 30.42 ± 1.691 ns | 0.433 | 0 B |
| .NET 8.0.30 | 66.73 ± 0.633 ns | 33.21 ± 1.744 ns | 0.498 | 0 B |

Two launches per job, five warmup iterations and 12 measurement iterations per launch; all 24 measurements retained. The 99.9% CI half-widths are 0.498/1.301 ns for .NET 10 and 0.487/1.341 ns for .NET 8. Every worker allocation summary reported `final=0, max=0`; MemoryDiagnoser reported no allocations or collections. This independently confirms the earlier source-snapshot experiment's direction with the actual baseline and candidate library assemblies.

[Raw BenchmarkDotNet report](handoff-report.md). Its Ratio column uses the .NET 8 baseline across all jobs; the table above divides means within each runtime.

Environment: Windows 11 build 26200.9168, Intel Core i7-12700K (12 physical / 20 logical processors), x64 RyuJIT, concurrent workstation GC, BenchmarkDotNet 0.15.8, SDK 11.0.100-preview.7.26381.103. CPU affinity mask `65535`; `DOTNET_PROCESSOR_COUNT=20` fixes the stripe count independently of affinity. Every launch logged its actual stripe count and return distance. Benchmarks ran without overlapping builds, tests, or other benchmark jobs.

## Scope and validation

Only `StripedObjectStore<T>` changes production behavior, for pools retaining more than 64 objects. Home-stripe reuse remains first; an empty, contested, or out-of-range hint falls back to the bounded scan. Changing which stripe is searched first changes reuse order, not the retention limit or ownership protocol. Small-pool storage and thread-local object retention are unchanged.

Longer .NET 8 control measurements (two launches, 24 measurements, capacity 4096) show the tradeoff:

| Workload | Before Mean ± StdDev | After Mean ± StdDev | After / before | Allocated |
|---|---:|---:|---:|---:|
| Same-thread rent/return | 14.871 ± 0.277 ns | 14.979 ± 0.863 ns | 1.007 | 0 B |
| Rent/return burst of 32 | 25.401 ± 0.455 ns | 25.962 ± 0.704 ns | 1.022 | 0 B |

Burst measurements are normalized per rent/return pair. The roughly 2.2% burst cost should be weighed against the remote-handoff gain, not hidden behind it. [Control report](net8-controls-report.md). [Short-run controls](short-controls-report.md) include .NET 10 (same-thread 13.21 → 12.85 ns, burst 29.00 → 23.05 ns), but three samples and visible run-to-run variation are insufficient for a precise .NET 10 control-speedup claim. Assembly inspection confirms `TryPopSlow` remains out of line.

At the 65-item boundary (eight stripes), short-run distant handoffs also improved: .NET 10 was 58.85 ± 0.261 → 32.30 ± 0.905 ns (0.549), and .NET 8 was 57.63 ± 0.507 → 33.34 ± 1.708 ns (0.579). Home-stripe handoff controls at capacities 65 and 4096 showed no regression in that run. All worker counters remained `final=0, max=0`. These are three-sample checks, with wider confidence intervals than the main comparison. [Boundary and home-stripe report](boundary-report.md).

Regression tests cover hints shared across differently sized stores (including one and non-power-of-two stripe counts), empty hints requiring wrapped scans, home preference, node-backed remote reuse, and exactly-once destruction under concurrent `Clear`/`Dispose`. Existing lifecycle-race and zero-allocation tests now also exercise capacities 65 and 4096.

- Release solution build: zero warnings/errors.
- Library tests: 344 passed across .NET 8 and .NET 10.
- netstandard2.0 library consumer tests: 175 passed on .NET 8.
- Node 24 `npm ci` and website production build passed.
- Benchmark Dry validation passed against both baseline and candidate on both runtimes.

The speedup is specific to recurring remote-stripe handoffs. The hint adds a possible failed probe on a miss and increases generated code size. This is not a claim of universal improvement, and ARM64 performance has not been measured. CAS and memory ordering remain unchanged.

## Reproduction

The new benchmark is compatible with the baseline library. The repository's **Benchmark comparison** workflow overlays the candidate benchmark sources onto the baseline checkout, then measures both on one runner:

```powershell
gh workflow run benchmark-compare.yml `
  -f baseline_ref=ad01a2c181b656636f0f3973ff52903421600635 `
  -f candidate_ref=perf/stripe-rent-hint `
  -f filter='*ObjectPoolRemoteHandoffBenchmarks*' `
  -f runtimes='net8.0 net10.0' `
  -f job=medium
```

Runner topology changes stripe counts and absolute timings; compare the two builds within that run. For the specific local topology above, run from the benchmark project directory with `DOTNET_PROCESSOR_COUNT=20` and an appropriate 16-CPU affinity mask:

```powershell
$previousProcessorCount = $env:DOTNET_PROCESSOR_COUNT
try {
    $env:DOTNET_PROCESSOR_COUNT = '20'
    dotnet run -c Release -f net10.0 -- `
      --filter '*ObjectPoolRemoteHandoffBenchmarks*Capacity: 4096*Distant*PairCount: 4*' `
      --runtimes net8.0 net10.0 --affinity 65535 `
      --iterationCount 12 --warmupCount 5 --launchCount 2 --outliers DontRemove `
      --noOverwrite
}
finally {
    $env:DOTNET_PROCESSOR_COUNT = $previousProcessorCount
}
```

Repeat with the same benchmark source overlaid onto a baseline checkout. Inspect worker-allocation lines in the log as well as the main-thread MemoryDiagnoser columns. Do not use `--apples` for these batched benchmarks; the repository workflow documents the `OperationsPerInvoke` issue in BenchmarkDotNet 0.15.8.
