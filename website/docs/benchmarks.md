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

The single-thread TLS `StringBuilder` cache measured 10.35 ns and 0 B; it gives up cross-thread reuse and bounded shared capacity. Scoped leases measured 16.06 ns versus 9.41 ns for manual rent/return, with 0 B allocated on both paths.
<!-- BENCHMARK_RESULTS_END -->

## Reproduce

```shell
dotnet run -c Release -f net10.0 --project benchmarks/Reservoir.Benchmarks -- --filter "*" --job Short --runtimes net8.0 net10.0 --apples
```

<!-- BENCHMARK_RESULTS_LINK_START -->
Raw Markdown, CSV, and HTML exports—including 1–32 worker contention results—are available from the [GitHub Actions run](https://github.com/thomhurst/Reservoir/actions/runs/31543884370).
<!-- BENCHMARK_RESULTS_LINK_END -->
