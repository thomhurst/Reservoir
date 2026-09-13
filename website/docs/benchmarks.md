---
title: Benchmarks
description: BenchmarkDotNet results for Reservoir's warm paths and specialized pools.
---

# Benchmarks

<!-- BENCHMARK_RESULTS_START -->
The 24 validated warm results below allocated **0 B per operation**.
This covers the Reservoir core, collection, list, warm capacity and burst results, manual/scoped rentals, and the TLS StringBuilder reference. Empty-rent results and other libraries are excluded from this claim.

Results below select .NET 10.0 and used BenchmarkDotNet 0.15.8 `.NET 10.0`, .NET 10.0.12, Linux Ubuntu 24.04.5 LTS, AMD EPYC 9V74. Other runtimes remain in the raw reports. Nanosecond timings vary by machine; compare methods within a table.

:::info Automated results
Generated 2026-09-13 05:03 UTC from commit `cb638b4b56c7`. See the [GitHub Actions run](https://github.com/thomhurst/Reservoir/actions/runs/34734502659) for logs and downloadable artifacts.
:::

## Core pool

The payload owns a 256-byte buffer. Lower ratio is better; `new` is the baseline.

| Method | Mean | Ratio | Allocated |
| --- | ---: | ---: | ---: |
| `new` | 25.58 ns | 1.00 | 304 B |
| Reservoir | 17.45 ns | 0.68 | 0 B |
| `Microsoft.Extensions.ObjectPool` | 15.11 ns | 0.59 | 0 B |
| `ConcurrentBag<T>` pool | 39.13 ns | 1.53 | 0 B |

## Capacity scaling

Small pools use cache-line-separated slots. Large pools use dense striped storage so empty misses and burst transfers do not scan every retained slot. `Drain and refill` rents and returns the full retained capacity once.

| Retained capacity | Warm rent/return | Empty rent | Drain and refill |
| ---: | ---: | ---: | ---: |
| 32 | 16.39 ns | 27.68 ns | 1.50 μs |
| 256 | 22.81 ns | 16.41 ns | 8.84 μs |
| 4,096 | 16.86 ns | 16.57 ns | 143.22 μs |
| 65,536 | 17.29 ns | 16.18 ns | 2,335.55 μs |

## Warm allocation guarantee

| Pool | Mean | Allocated |
| --- | ---: | ---: |
| `ObjectPool` | 15.95 ns | 0 B |
| `ListPool` | 13.66 ns | 0 B |
| `DictionaryPool` | 14.11 ns | 0 B |
| `HashSetPool` | 14.11 ns | 0 B |
| `QueuePool` | 14.18 ns | 0 B |
| `StackPool` | 13.83 ns | 0 B |
| `StringBuilderPool` | 15.62 ns | 0 B |

## Specialized workloads

| Workload | Baseline | Reservoir | Baseline allocated | Reservoir allocated |
| --- | ---: | ---: | ---: | ---: |
| `StringBuilder`, append 128 chars | 47.14 ns | 24.69 ns | 400 B | 0 B |
| `List<int>`, 8 items | 26.27 ns | 30.25 ns | 88 B | 0 B |
| `List<int>`, 128 items | 252.73 ns | 236.82 ns | 568 B | 0 B |
| `List<int>`, 2,048 items | 3,991.21 ns | 3,233.07 ns | 8,248 B | 0 B |

The single-thread TLS `StringBuilder` cache measured 14.99 ns and 0 B; it gives up cross-thread reuse and bounded shared capacity. `ObjectPool.RentScoped(out T)` measured 10.50 ns and 0 B, `RentScoped()` measured 11.80 ns and 0 B, and manual rent/return measured 16.23 ns and 0 B. Allocations are per operation in the selected runtime and job.
<!-- BENCHMARK_RESULTS_END -->

## Choosing a rental API

For synchronous hot paths on .NET 10, prefer `RentScoped(out T)`. On .NET 8, manual `Rent()` and
`Return()` remain faster. See the measured allocation results above. Always use manual rental when ownership
crosses an `await`, and measure representative workloads on target hardware.

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
Raw Markdown, CSV, and HTML exports—including capacity scaling and 1–32 worker contention results—are available from the [GitHub Actions run](https://github.com/thomhurst/Reservoir/actions/runs/34734502659).
<!-- BENCHMARK_RESULTS_LINK_END -->
