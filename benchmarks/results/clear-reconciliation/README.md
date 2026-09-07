# Clear reconciliation retention fix (#98)

`TrackedInstanceThreadLocalFrontTier<T>` now keeps its last captured item in a weak reference. A racing renter still holds the item strongly while reconciling under the slot lock; an uncontended clear no longer keeps a destroyed object graph alive. The first capturing clear allocates one weak-reference handle per slot, reused by later clears. Ordinary warm rent/return does not access that handle.

## Measurement

- Baseline: `6a98774c3374b732bbcc3a71053a1f29a282af18`, Release assemblies saved before editing.
- Candidate tracked-tier source: `4c1f4148f3fbda461659b330e3cacbfdb5f64eca`. The subsequent rebase only adds the XML packaging and concurrency-test changes from #115/#116; the measured tracked-tier source is identical.
- Windows 11, Intel Core i7-12700K, SDK 10.0.400, BenchmarkDotNet 0.15.8.
- Separate .NET 8 and .NET 10 runs, each comparing the saved baseline DLL with the candidate project in the same run. Both jobs use identical invocation counts through `--apples`, ten measurement iterations, one measurement warmup after the pilot, and no outlier removal.
- `MemoryDiagnoser` enabled. Every measured job reports zero allocated bytes and zero Gen0/Gen1/Gen2 collections in the raw logs.
- Shared Dekaf `performance` reservation held continuously from baseline preparation through both runs. Ownership verified before measurement phases and after completion. Timed .NET 10 run: 2026-09-07 17:49:19–17:51:09 BST; .NET 8: 17:52:09–17:53:53 BST. No other heavy local workload ran during measurement. Process samples showed idle MSBuild/compiler/Node processes; only the two lock Redis containers were running. Approximately 16 GiB RAM remained free.

| Runtime | Method | Baseline Mean | Candidate Mean | Ratio | Candidate StdDev | Allocated |
|---|---|---:|---:|---:|---:|---:|
| .NET 8 | ManualThreadLocal | 17.70 ns | 17.85 ns | 1.01 | 0.312 ns | 0 B |
| .NET 8 | Scoped | 13.61 ns | 13.64 ns | 1.00 | 0.151 ns | 0 B |
| .NET 8 | ScopedOut | 13.38 ns | 13.41 ns | 1.00 | 0.146 ns | 0 B |
| .NET 8 | CancellationTokenSourceScoped | 18.20 ns | 18.46 ns | 1.01 | 0.231 ns | 0 B |
| .NET 10 | ManualThreadLocal | 7.468 ns | 7.342 ns | 0.98 | 0.0766 ns | 0 B |
| .NET 10 | Scoped | 9.135 ns | 9.127 ns | 1.00 | 0.0979 ns | 0 B |
| .NET 10 | ScopedOut | 6.574 ns | 6.575 ns | 1.00 | 0.0899 ns | 0 B |
| .NET 10 | CancellationTokenSourceScoped | 10.056 ns | 9.824 ns | 0.98 | 0.1216 ns | 0 B |

Full results, including baseline noise and 99.9% confidence intervals: [.NET 8](net8-report.md), [.NET 10](net10-report.md). The 1–2% differences are comparable to run noise; these results support no material warm-path regression, not a throughput improvement claim. A preceding independent .NET 10 manual-path comparison measured 7.376 ns baseline and 7.398 ns candidate (ratio 1.00, both 0 B).

## Reproduction

The PR includes the task-scoped comparison project and program as text. The benchmark methods perform one warm operation each: opt-in manual thread-local rent/return, scoped lease/value, scoped lease/out, and scoped CancellationTokenSource rental. Pools have capacity 32 and are warmed in GlobalSetup. Each method returns its object to prevent dead-code elimination. The generic policy creates an empty reference object and always accepts reset.

1. Build the baseline revision in Release and copy `src/Reservoir/bin/Release/net8.0/Reservoir.dll` and `net10.0/Reservoir.dll` into the candidate checkout's `artifacts/issue98-baseline/<TFM>/Reservoir.dll`.
2. Create the comparison project from the PR under `artifacts/issue98-comparison`. Its conditional reference selects the saved DLL for the baseline job and the candidate project for the other job.
3. From the candidate checkout, run:

```powershell
dotnet run -c Release -f net10.0 --project artifacts/issue98-comparison -- --filter '*' --dry --artifacts artifacts/issue98-benchmarks/dry-net10 --noOverwrite
dotnet run -c Release -f net10.0 --no-build --project artifacts/issue98-comparison -- --filter '*' --apples --artifacts artifacts/issue98-benchmarks/full-net10 --noOverwrite
dotnet run -c Release -f net8.0 --project artifacts/issue98-comparison -- --filter '*' --dry --artifacts artifacts/issue98-benchmarks/dry-net8 --noOverwrite
dotnet run -c Release -f net8.0 --no-build --project artifacts/issue98-comparison -- --filter '*' --apples --artifacts artifacts/issue98-benchmarks/full-net8 --noOverwrite
```

Reserve the shared performance lock before preparation and hold it throughout measurement. Do not run builds/tests alongside the benchmarks. Use the final two-job report, not the baseline-only pilot report emitted by `--apples`.

## Correctness

The six new collection cases failed on both modern baseline assets (12 failures), then passed with weak records. Tests use non-inlined seed/GC helpers and keep the pool alive while checking weak references. Coverage includes Clear and Dispose, scoped and opt-in manual thread-local rentals, and CancellationTokenSourcePool. Concurrent tests combine rental ownership checks, repeated clears, disposal, and forced GC for all three paths. Final Release validation passed 384 modern and 195 netstandard-consumer tests.
