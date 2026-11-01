---
title: Benchmarks
description: BenchmarkDotNet results for Reservoir's warm paths and specialized pools.
---

# Benchmarks

<!-- BENCHMARK_RESULTS_START -->
The 24 validated warm results below allocated **0 B per operation**.
This covers the Reservoir core, collection, list, warm capacity and burst results, manual/scoped rentals, and the TLS StringBuilder reference. Empty-rent results and other libraries are excluded from this claim.

Results below select .NET 10.0 and used BenchmarkDotNet 0.15.8 `.NET 10.0`, .NET 10.0.14, Linux Ubuntu 24.04.5 LTS, AMD EPYC 9V74. Other runtimes remain in the raw reports. Nanosecond timings vary by machine; compare methods within a table.

:::info Automated results
Generated 2026-11-01 04:53 UTC from commit `8a2071fd6b45`. See the [GitHub Actions run](https://github.com/thomhurst/Reservoir/actions/runs/41267525070) for logs and downloadable artifacts.
:::

## Core pool

The payload owns a 256-byte buffer. Lower ratio is better; `new` is the baseline.

| Method | Mean | Ratio | Allocated |
| --- | ---: | ---: | ---: |
| `new` | 18.42 ns | 1.00 | 304 B |
| Reservoir | 10.92 ns | 0.59 | 0 B |
| `Microsoft.Extensions.ObjectPool` | 11.05 ns | 0.60 | 0 B |
| `ConcurrentBag<T>` pool | 30.08 ns | 1.63 | 0 B |

## Capacity scaling

Small pools use cache-line-separated slots. Large pools use dense striped storage so empty misses and burst transfers do not scan every retained slot. `Drain and refill` rents and returns the full retained capacity once.

| Retained capacity | Warm rent/return | Empty rent | Drain and refill |
| ---: | ---: | ---: | ---: |
| 32 | 11.37 ns | 19.26 ns | 1.09 μs |
| 256 | 12.33 ns | 11.53 ns | 6.11 μs |
| 4,096 | 12.48 ns | 11.69 ns | 93.04 μs |
| 65,536 | 12.94 ns | 12.11 ns | 1,539.22 μs |

## Warm allocation guarantee

| Pool | Mean | Allocated |
| --- | ---: | ---: |
| `ObjectPool` | 11.00 ns | 0 B |
| `ListPool` | 10.68 ns | 0 B |
| `DictionaryPool` | 10.46 ns | 0 B |
| `HashSetPool` | 10.51 ns | 0 B |
| `QueuePool` | 10.32 ns | 0 B |
| `StackPool` | 10.73 ns | 0 B |
| `StringBuilderPool` | 10.37 ns | 0 B |

## Specialized workloads

| Workload | Baseline | Reservoir | Baseline allocated | Reservoir allocated |
| --- | ---: | ---: | ---: | ---: |
| `StringBuilder`, append 128 chars | 37.13 ns | 17.58 ns | 400 B | 0 B |
| `List<int>`, 8 items | 20.12 ns | 23.33 ns | 88 B | 0 B |
| `List<int>`, 128 items | 190.27 ns | 179.77 ns | 568 B | 0 B |
| `List<int>`, 2,048 items | 2,966.34 ns | 2,692.49 ns | 8,248 B | 0 B |

The single-thread TLS `StringBuilder` cache measured 11.77 ns and 0 B; it gives up cross-thread reuse and bounded shared capacity. `ObjectPool.RentScoped(out T)` measured 12.86 ns and 0 B, `RentScoped()` measured 14.11 ns and 0 B, and manual rent/return measured 12.72 ns and 0 B. Allocations are per operation in the selected runtime and job.
<!-- BENCHMARK_RESULTS_END -->

## Choosing a rental API

For synchronous scopes, prefer `RentScoped(out T)` for automatic return; it avoids the ownership
check that each `lease.Value` read performs. Scoped and manual rentals share the same bounded store.
See the measured allocation results above. Always use manual rental when ownership crosses an
`await`, and measure representative workloads on target hardware.

## Reproduce

Run benchmarks from the repository root with GitHub CLI authenticated to an account that
can dispatch workflows. Measurements run on GitHub Actions `ubuntu-latest`; local timings
are diagnostic only and cannot establish performance acceptance.

For routine setup validation, select one warm rent/return benchmark on .NET 8 and .NET 10:

```shell
gh workflow run benchmarks.yml --ref main -f 'filter=*.ObjectPoolBenchmarks.RentReturn' -f job=short
```

The workflow first runs `--job Dry` to check compilation, setup, and execution. Dry output
is not a performance measurement and cannot establish allocation guarantees. The next
step runs `--job Short` to measure the same selection. This filter selects two cases: one
method on each runtime. Short results are useful for initial checks; repeat noisy results
or use a longer job with a narrow filter before drawing conclusions.

For the full suite, keep the `short` job:

```shell
gh workflow run benchmarks.yml --ref main -f 'filter=*' -f job=short
```

For reference only, the full-suite measurement step uses this BenchmarkDotNet invocation
on the runner after restoring and building the benchmark project in Release. This is not
a standalone setup command; use the workflow commands above to reproduce the run:

```shell
dotnet run -c Release -f net10.0 --no-build --project benchmarks/Reservoir.Benchmarks -- --filter "*" --job Short --runtimes net8.0 net10.0
```

For the bounded selection, the runner substitutes `--filter "*.ObjectPoolBenchmarks.RentReturn"`.
For its validation step, it also substitutes `--job Dry`. The repository SDK comes from
`global.json`; both .NET 8 and .NET 10 runtimes must be available.

Do not add `--apples` to these commands. With BenchmarkDotNet 0.15.8, apples mode interacts
with `OperationsPerInvoke` so batched contention cases become effectively unbounded.
The validated workflow intentionally omits it for both Dry and measurement jobs.

Find the dispatched run in the repository's Actions tab. Download its `reservoir-benchmarks`
artifact for separate Dry and measurement logs and reports. A filtered run does not
republish the benchmark tables above. The historical tables identify their own job,
runtime, hardware, and source commit; new runs need not reproduce their exact timings.

For a performance change, use the
[Benchmark comparison workflow](https://github.com/thomhurst/Reservoir/actions/workflows/benchmark-compare.yml)
with explicit baseline and candidate commit SHAs and matching runtime/job settings. It
runs both revisions sequentially on the same runner with the candidate's benchmark code.
Report Mean, Ratio, Allocated, and noise, and link the run and artifacts with both SHAs.

To compare the netstandard2.0 asset on modern runtimes, add `-f library_asset=netstandard2.0`
and select `*DestroyPolicyBenchmarks.*`, `*RuntimePolicyObjectPoolBenchmarks.*`, or
`*FactoryObjectPoolBenchmarks.*`. That option builds a bounded portable suite and forwards
the asset selection to BenchmarkDotNet's generated projects. The default `automatic` option
uses the host's matching asset and the full benchmark suite. Both selections run Dry validation
before measuring each revision; the logs print the loaded Reservoir asset.

<!-- BENCHMARK_RESULTS_LINK_START -->
Raw Markdown, CSV, and HTML exports—including capacity scaling and 1–32 worker contention results—are available from the [GitHub Actions run](https://github.com/thomhurst/Reservoir/actions/runs/41267525070).
<!-- BENCHMARK_RESULTS_LINK_END -->
