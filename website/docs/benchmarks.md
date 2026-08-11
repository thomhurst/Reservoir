---
title: Benchmarks
description: BenchmarkDotNet results for Reservoir's warm paths and specialized pools.
---

# Benchmarks

<!-- BENCHMARK_RESULTS_START -->
Every measured warm Reservoir path allocated **0 B per operation**.

Results below used BenchmarkDotNet 0.15.8 `ShortRun`, .NET 10.0.10, Windows 11, Intel Core i7-12700K. Nanosecond timings vary by machine; compare methods within a table.

## Core pool

The payload owns a 256-byte buffer. Lower ratio is better; `new` is the baseline.

| Method | Mean | Ratio | Allocated |
| --- | ---: | ---: | ---: |
| `new` | 12.67 ns | 1.00 | 304 B |
| Reservoir | 11.83 ns | 0.93 | 0 B |
| `Microsoft.Extensions.ObjectPool` | 14.56 ns | 1.15 | 0 B |
| `ConcurrentBag<T>` pool | 39.48 ns | 3.12 | 0 B |

## Warm allocation guarantee

| Pool | Mean | Allocated |
| --- | ---: | ---: |
| `ObjectPool` | 11.52 ns | 0 B |
| `ListPool` | 13.13 ns | 0 B |
| `DictionaryPool` | 12.21 ns | 0 B |
| `HashSetPool` | 15.04 ns | 0 B |
| `QueuePool` | 13.62 ns | 0 B |
| `StackPool` | 13.86 ns | 0 B |
| `StringBuilderPool` | 14.96 ns | 0 B |

## Specialized workloads

| Workload | Baseline | Reservoir | Baseline allocated | Reservoir allocated |
| --- | ---: | ---: | ---: | ---: |
| `StringBuilder`, append 128 chars | 25.66 ns | 18.12 ns | 400 B | 0 B |
| `List<int>`, 8 items | 15.27 ns | 32.12 ns | 88 B | 0 B |
| `List<int>`, 128 items | 139.23 ns | 126.66 ns | 568 B | 0 B |
| `List<int>`, 2,048 items | 1,738.18 ns | 1,502.78 ns | 8,248 B | 0 B |

The single-thread TLS `StringBuilder` cache measured 5.52 ns and 0 B; it gives up cross-thread reuse and bounded shared capacity. Scoped leases measured 12.67 ns versus 10.50 ns for manual rent/return, with 0 B allocated on both paths.
<!-- BENCHMARK_RESULTS_END -->

## Reproduce

```shell
dotnet run -c Release --project benchmarks/Reservoir.Benchmarks -- --filter "*" --job Short
```

<!-- BENCHMARK_RESULTS_LINK_START -->
Raw Markdown, CSV, and HTML exports—including 1–32 worker contention results—live in [`benchmarks/results/20260811-200439`](https://github.com/thomhurst/Reservoir/tree/main/benchmarks/results/20260811-200439).
<!-- BENCHMARK_RESULTS_LINK_END -->
