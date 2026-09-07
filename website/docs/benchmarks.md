---
title: Benchmarks
description: BenchmarkDotNet results for Reservoir's warm paths and specialized pools.
---

# Benchmarks

<!-- BENCHMARK_RESULTS_START -->
Every measured warm Reservoir path allocated **0 B per operation**.

Results below used BenchmarkDotNet 0.15.8 `MediumRun`, .NET 10.0.11, Windows 11, AMD EPYC 9V74. Nanosecond timings vary by machine; compare methods within a table.

:::info Automated results
Generated 2026-08-11 23:33 UTC from commit `d32ff7f69c75`. See the [GitHub Actions run](https://github.com/thomhurst/Reservoir/actions/runs/31543884370) for logs and downloadable artifacts.
:::

## Core pool

The payload owns a 256-byte buffer. Lower ratio is better; `new` is the baseline.

| Method | Mean | Ratio | Allocated |
| --- | ---: | ---: | ---: |
| `new` | 11.83 ns | 1.00 | 304 B |
| Reservoir | 10.36 ns | 0.88 | 0 B |
| `Microsoft.Extensions.ObjectPool` | 11.57 ns | 0.98 | 0 B |
| `ConcurrentBag<T>` pool | 25.19 ns | 2.13 | 0 B |

## Warm allocation guarantee

| Pool | Mean | Allocated |
| --- | ---: | ---: |
| `ObjectPool` | 10.29 ns | 0 B |
| `ListPool` | 10.97 ns | 0 B |
| `DictionaryPool` | 11.90 ns | 0 B |
| `HashSetPool` | 12.01 ns | 0 B |
| `QueuePool` | 11.32 ns | 0 B |
| `StackPool` | 11.14 ns | 0 B |
| `StringBuilderPool` | 15.91 ns | 0 B |

## Specialized workloads

| Workload | Baseline | Reservoir | Baseline allocated | Reservoir allocated |
| --- | ---: | ---: | ---: | ---: |
| `StringBuilder`, append 128 chars | 29.67 ns | 20.82 ns | 400 B | 0 B |
| `List<int>`, 8 items | 17.76 ns | 25.01 ns | 88 B | 0 B |
| `List<int>`, 128 items | 196.35 ns | 184.15 ns | 568 B | 0 B |
| `List<int>`, 2,048 items | 3,036.25 ns | 2,750.70 ns | 8,248 B | 0 B |

The single-thread TLS `StringBuilder` cache measured 10.35 ns and 0 B; it gives up cross-thread
reuse and bounded shared capacity.
<!-- BENCHMARK_RESULTS_END -->

## Choosing a rental API

For synchronous hot paths on .NET 10, prefer `RentScoped(out T)`. On .NET 8, manual `Rent()` and
`Return()` remain faster. Both warm paths allocate 0 B. Always use manual rental when ownership
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

<!-- BENCHMARK_RESULTS_LINK_START -->
Raw Markdown, CSV, and HTML exports—including 1–32 worker contention results—are available from the [GitHub Actions run](https://github.com/thomhurst/Reservoir/actions/runs/31543884370).
<!-- BENCHMARK_RESULTS_LINK_END -->
