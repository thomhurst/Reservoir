```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8973/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i7-12700K 3.60GHz, 1 CPU, 20 logical and 12 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1
WarmupCount=3

```
| Method                        | WorkerCount | Mean      | Error      | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------------ |------------ |----------:|-----------:|----------:|------:|--------:|----------:|------------:|
| **Reservoir**                     | **1**           | **11.255 ns** |  **0.0725 ns** | **0.0040 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| MicrosoftExtensionsObjectPool | 1           | 15.245 ns |  0.6980 ns | 0.0383 ns |  1.35 |    0.00 |         - |          NA |
| ConcurrentBag                 | 1           | 41.016 ns | 11.6822 ns | 0.6403 ns |  3.64 |    0.05 |         - |          NA |
|                               |             |           |            |           |       |         |           |             |
| **Reservoir**                     | **4**           |  **2.981 ns** |  **0.2319 ns** | **0.0127 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| MicrosoftExtensionsObjectPool | 4           | 72.941 ns | 11.5277 ns | 0.6319 ns | 24.47 |    0.20 |         - |          NA |
| ConcurrentBag                 | 4           | 66.191 ns |  7.9628 ns | 0.4365 ns | 22.20 |    0.15 |         - |          NA |
|                               |             |           |            |           |       |         |           |             |
| **Reservoir**                     | **8**           |  **1.754 ns** |  **0.2753 ns** | **0.0151 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| MicrosoftExtensionsObjectPool | 8           | 87.313 ns |  5.4946 ns | 0.3012 ns | 49.79 |    0.40 |         - |          NA |
| ConcurrentBag                 | 8           | 65.719 ns | 17.8747 ns | 0.9798 ns | 37.48 |    0.56 |         - |          NA |
|                               |             |           |            |           |       |         |           |             |
| **Reservoir**                     | **16**          |  **1.612 ns** |  **0.0356 ns** | **0.0020 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| MicrosoftExtensionsObjectPool | 16          | 83.165 ns |  3.9310 ns | 0.2155 ns | 51.58 |    0.13 |         - |          NA |
| ConcurrentBag                 | 16          | 52.436 ns |  3.6540 ns | 0.2003 ns | 32.52 |    0.11 |         - |          NA |
|                               |             |           |            |           |       |         |           |             |
| **Reservoir**                     | **32**          |  **4.820 ns** |  **0.7361 ns** | **0.0403 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| MicrosoftExtensionsObjectPool | 32          | 83.826 ns |  8.9469 ns | 0.4904 ns | 17.39 |    0.15 |         - |          NA |
| ConcurrentBag                 | 32          | 48.081 ns |  3.4233 ns | 0.1876 ns |  9.97 |    0.08 |         - |          NA |
