# Shared-store scoped rental investigation (#103)

`RentScopedShared()` adds copied-lease ownership protection while keeping idle pooled objects in the existing bounded shared store. It is an opt-in retention tradeoff: the measured warm shared-store lease paths cost more than the existing thread-local scoped paths. Existing defaults and hot method bodies are unchanged. The separate lease types avoid adding a mode branch to `PooledLease.Dispose()`.

## Reproduction and provenance

- [Successful Ubuntu run 34148738010](https://github.com/thomhurst/Reservoir/actions/runs/34148738010), [full logs and artifacts](https://github.com/thomhurst/Reservoir/actions/runs/34148738010/artifacts/10029325401).
- Measured implementation and harness commit: `f7bdd8f27976f08de059b5ee0db947b438a9e47f`; parent/base: `b0b8d5b`. This is an additive API investigation with manual, existing scoped, and proposed scoped methods measured sequentially in the same process harness/job on one runner, rather than a claim that existing methods became faster.
- Reviewed implementation commit before this report: `81c3a8fe590d8190baca078923082cc156852fb1`. Its production diff from the measured commit contains only two XML summary clarifications and trimming metadata on the new lease's `TPolicy`; executable method bodies and the benchmark harness are identical.
- `ubuntu-latest`: Ubuntu 24.04.4 LTS, AMD EPYC 7763 2.45 GHz, 4 logical / 2 physical cores; SDK 10.0.400; .NET 8.0.30 and .NET 10.0.11, X64 RyuJIT.
- BenchmarkDotNet 0.15.8 `MediumRun`: 2 launches, 10 warmup iterations, 15 measurement iterations per launch; `MemoryDiagnoser`; capacities 1, 32, 256; 42 measured cases. The workflow's separate Dry pass only validates setup.

Dispatch the checked-in harness at a ref containing the measured commit:

```powershell
gh workflow run benchmarks.yml --ref <ref-containing-f7bdd8f> -f 'filter=*SharedScopedRentalBenchmarks*' -f job=medium
```

The measurement command executed by the workflow is:

```powershell
dotnet run --project benchmarks/Reservoir.Benchmarks/Reservoir.Benchmarks.csproj --configuration Release --framework net10.0 --no-build -- --filter '*SharedScopedRentalBenchmarks*' --job medium --runtimes net8.0 net10.0 --artifacts artifacts/benchmarks
```

## Results

Ratios below divide each mean by **Manual in the same runtime and capacity**. The unmodified raw CSV/Markdown exports use BenchmarkDotNet's cross-runtime baseline grouping; their Ratio column must not be read as a same-runtime comparison. Error is BenchmarkDotNet's 99.9% confidence-interval half-width. Values retain the exported precision.

| Runtime | Capacity | Method | Mean | Error | StdDev | Ratio | Allocated |
| --- | ---: | --- | ---: | ---: | ---: | ---: | ---: |
| .NET 10.0 | 1 | Manual | 15.67 ns | 1.302 ns | 1.868 ns | 1.000 | 0 B |
| .NET 10.0 | 1 | Scoped | 13.87 ns | 0.103 ns | 0.145 ns | 0.885 | 0 B |
| .NET 10.0 | 1 | SharedScoped | 21.85 ns | 1.782 ns | 2.612 ns | 1.394 | 0 B |
| .NET 10.0 | 1 | SharedScopedValue | 22.52 ns | 1.159 ns | 1.735 ns | 1.437 | 0 B |
| .NET 10.0 | 1 | RuntimeManual | 18.51 ns | 0.030 ns | 0.043 ns | 1.181 | 0 B |
| .NET 10.0 | 1 | RuntimeScoped | 19.48 ns | 0.233 ns | 0.334 ns | 1.243 | 0 B |
| .NET 10.0 | 1 | RuntimeSharedScoped | 24.27 ns | 0.059 ns | 0.083 ns | 1.549 | 0 B |
| .NET 8.0 | 1 | Manual | 29.97 ns | 0.173 ns | 0.242 ns | 1.000 | 0 B |
| .NET 8.0 | 1 | Scoped | 25.58 ns | 0.024 ns | 0.033 ns | 0.854 | 0 B |
| .NET 8.0 | 1 | SharedScoped | 37.55 ns | 0.130 ns | 0.190 ns | 1.253 | 0 B |
| .NET 8.0 | 1 | SharedScopedValue | 36.56 ns | 0.062 ns | 0.087 ns | 1.220 | 0 B |
| .NET 8.0 | 1 | RuntimeManual | 40.62 ns | 0.114 ns | 0.163 ns | 1.355 | 0 B |
| .NET 8.0 | 1 | RuntimeScoped | 37.21 ns | 0.931 ns | 1.305 ns | 1.242 | 0 B |
| .NET 8.0 | 1 | RuntimeSharedScoped | 46.13 ns | 0.442 ns | 0.619 ns | 1.539 | 0 B |
| .NET 10.0 | 32 | Manual | 13.83 ns | 0.020 ns | 0.029 ns | 1.000 | 0 B |
| .NET 10.0 | 32 | Scoped | 13.72 ns | 0.252 ns | 0.346 ns | 0.992 | 0 B |
| .NET 10.0 | 32 | SharedScoped | 21.67 ns | 1.541 ns | 2.307 ns | 1.567 | 0 B |
| .NET 10.0 | 32 | SharedScopedValue | 19.54 ns | 0.052 ns | 0.073 ns | 1.413 | 0 B |
| .NET 10.0 | 32 | RuntimeManual | 19.77 ns | 0.676 ns | 0.948 ns | 1.430 | 0 B |
| .NET 10.0 | 32 | RuntimeScoped | 18.68 ns | 0.210 ns | 0.301 ns | 1.351 | 0 B |
| .NET 10.0 | 32 | RuntimeSharedScoped | 24.61 ns | 0.160 ns | 0.235 ns | 1.779 | 0 B |
| .NET 8.0 | 32 | Manual | 29.32 ns | 0.016 ns | 0.022 ns | 1.000 | 0 B |
| .NET 8.0 | 32 | Scoped | 26.08 ns | 0.260 ns | 0.381 ns | 0.889 | 0 B |
| .NET 8.0 | 32 | SharedScoped | 37.49 ns | 0.077 ns | 0.116 ns | 1.279 | 0 B |
| .NET 8.0 | 32 | SharedScopedValue | 38.18 ns | 0.276 ns | 0.368 ns | 1.302 | 0 B |
| .NET 8.0 | 32 | RuntimeManual | 39.98 ns | 0.271 ns | 0.389 ns | 1.364 | 0 B |
| .NET 8.0 | 32 | RuntimeScoped | 35.95 ns | 0.051 ns | 0.072 ns | 1.226 | 0 B |
| .NET 8.0 | 32 | RuntimeSharedScoped | 45.57 ns | 0.350 ns | 0.491 ns | 1.554 | 0 B |
| .NET 10.0 | 256 | Manual | 16.98 ns | 0.925 ns | 1.326 ns | 1.000 | 0 B |
| .NET 10.0 | 256 | Scoped | 14.68 ns | 0.049 ns | 0.072 ns | 0.865 | 0 B |
| .NET 10.0 | 256 | SharedScoped | 19.97 ns | 0.010 ns | 0.013 ns | 1.176 | 0 B |
| .NET 10.0 | 256 | SharedScopedValue | 19.81 ns | 0.020 ns | 0.028 ns | 1.167 | 0 B |
| .NET 10.0 | 256 | RuntimeManual | 20.88 ns | 0.164 ns | 0.235 ns | 1.230 | 0 B |
| .NET 10.0 | 256 | RuntimeScoped | 18.59 ns | 0.045 ns | 0.064 ns | 1.095 | 0 B |
| .NET 10.0 | 256 | RuntimeSharedScoped | 23.87 ns | 0.014 ns | 0.019 ns | 1.406 | 0 B |
| .NET 8.0 | 256 | Manual | 32.62 ns | 0.049 ns | 0.071 ns | 1.000 | 0 B |
| .NET 8.0 | 256 | Scoped | 28.00 ns | 1.192 ns | 1.671 ns | 0.858 | 0 B |
| .NET 8.0 | 256 | SharedScoped | 39.44 ns | 0.112 ns | 0.157 ns | 1.209 | 0 B |
| .NET 8.0 | 256 | SharedScopedValue | 37.61 ns | 0.046 ns | 0.066 ns | 1.153 | 0 B |
| .NET 8.0 | 256 | RuntimeManual | 47.73 ns | 1.227 ns | 1.680 ns | 1.463 | 0 B |
| .NET 8.0 | 256 | RuntimeScoped | 37.22 ns | 0.746 ns | 1.021 ns | 1.141 | 0 B |
| .NET 8.0 | 256 | RuntimeSharedScoped | 51.02 ns | 1.205 ns | 1.690 ns | 1.564 | 0 B |

## Interpretation and noise

- All 42 rows report **0 B/op**. All 84 launch GC records show zero Gen0/Gen1/Gen2 collections. A few launches contain 32 bytes total across millions of operations; these round to 0 B/op and occur on existing as well as new paths. This is measured warm-path evidence, not a promise that pool creation, first-use lease bookkeeping, or factory misses allocate nothing.
- The generic shared-store `out` path measures 19.97–21.85 ns on .NET 10 and 37.49–39.44 ns on .NET 8. Relative to the current scoped path, it costs approximately 5.29–7.98 ns on .NET 10 and 11.41–11.97 ns on .NET 8. Runtime-policy shared scopes cost 4.79–5.93 ns and 8.92–13.80 ns more than their current scoped counterparts, respectively.
- Relative standard deviations range from approximately 0.07% to 11.95%. BenchmarkDotNet flags four bimodal distributions: .NET 10 Manual at capacities 1 and 256, .NET 10 SharedScoped at capacity 1, and .NET 8 RuntimeScoped at capacity 1. Launch/code-layout and hosted-runner variation limit precise rankings of nearby cases. No improvement is claimed; repeat a controlled comparison before making a small latency claim.
- The consistent additional cost buys automatic return and stale-copy protection without the default scoped mode's extra thread-local object retention. Tests hold 320 nested rentals across eight workers, then verify idle retention does not exceed capacities 1, 32, 65, or 256, including pools with the manual TLS option enabled. Outstanding rentals and lease bookkeeping remain outside this idle-object bound. Mixing this mode with other rental modes does not remove their thread-local retention.
- These are warmed, single-thread latency/allocation measurements. They do not establish contended throughput, production tail latency, or retained byte counts for an application. Prefer the existing scoped API when its additional retention is acceptable; choose the new mode when a strict idle-object bound is more valuable than the measured latency cost.
