---
title: Benchmarks
description: BenchmarkDotNet results for Reservoir's warm paths and specialized pools.
---

# Benchmarks

<!-- BENCHMARK_RESULTS_START -->
The 24 validated warm results below allocated **0 B per operation**.
This covers the Reservoir core, collection, list, warm capacity and burst results, manual/scoped rentals, and the TLS StringBuilder reference. Empty-rent results and other libraries are excluded from this claim.

Results below select .NET 10.0 and used BenchmarkDotNet 0.15.8 `.NET 10.0`, .NET 10.0.13, Linux Ubuntu 24.04.5 LTS, INTEL XEON PLATINUM 8573C. Other runtimes remain in the raw reports. Nanosecond timings vary by machine; compare methods within a table.

:::info Automated results
Generated 2026-10-25 05:12 UTC from commit `f8bdbeef3270`. See the [GitHub Actions run](https://github.com/thomhurst/Reservoir/actions/runs/40156745589) for logs and downloadable artifacts.
:::

## Core pool

The payload owns a 256-byte buffer. Lower ratio is better; `new` is the baseline.

| Method | Mean | Ratio | Allocated |
| --- | ---: | ---: | ---: |
| `new` | 20.25 ns | 1.00 | 304 B |
| Reservoir | 18.65 ns | 0.92 | 0 B |
| `Microsoft.Extensions.ObjectPool` | 18.77 ns | 0.93 | 0 B |
| `ConcurrentBag<T>` pool | 56.62 ns | 2.80 | 0 B |

## Capacity scaling

Small pools use cache-line-separated slots. Large pools use dense striped storage so empty misses and burst transfers do not scan every retained slot. `Drain and refill` rents and returns the full retained capacity once.

| Retained capacity | Warm rent/return | Empty rent | Drain and refill |
| ---: | ---: | ---: | ---: |
| 32 | 17.00 ns | 15.30 ns | 1.34 μs |
| 256 | 17.87 ns | 10.28 ns | 7.20 μs |
| 4,096 | 17.55 ns | 10.70 ns | 112.97 μs |
| 65,536 | 17.47 ns | 11.18 ns | 1,796.69 μs |

## Warm allocation guarantee

| Pool | Mean | Allocated |
| --- | ---: | ---: |
| `ObjectPool` | 17.54 ns | 0 B |
| `ListPool` | 16.75 ns | 0 B |
| `DictionaryPool` | 16.98 ns | 0 B |
| `HashSetPool` | 17.01 ns | 0 B |
| `QueuePool` | 16.99 ns | 0 B |
| `StackPool` | 17.03 ns | 0 B |
| `StringBuilderPool` | 17.02 ns | 0 B |

## Specialized workloads

| Workload | Baseline | Reservoir | Baseline allocated | Reservoir allocated |
| --- | ---: | ---: | ---: | ---: |
| `StringBuilder`, append 128 chars | 41.24 ns | 21.75 ns | 400 B | 0 B |
| `List<int>`, 8 items | 23.38 ns | 32.25 ns | 88 B | 0 B |
| `List<int>`, 128 items | 243.53 ns | 299.43 ns | 568 B | 0 B |
| `List<int>`, 2,048 items | 2,206.15 ns | 2,304.96 ns | 8,248 B | 0 B |

The single-thread TLS `StringBuilder` cache measured 10.76 ns and 0 B; it gives up cross-thread reuse and bounded shared capacity. `ObjectPool.RentScoped(out T)` measured 7.45 ns and 0 B, `RentScoped()` measured 5.94 ns and 0 B, and manual rent/return measured 16.65 ns and 0 B. Allocations are per operation in the selected runtime and job.
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
Raw Markdown, CSV, and HTML exports—including capacity scaling and 1–32 worker contention results—are available from the [GitHub Actions run](https://github.com/thomhurst/Reservoir/actions/runs/40156745589).
<!-- BENCHMARK_RESULTS_LINK_END -->
