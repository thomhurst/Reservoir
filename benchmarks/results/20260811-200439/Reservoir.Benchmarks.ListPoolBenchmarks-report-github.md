```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8973/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i7-12700K 3.60GHz, 1 CPU, 20 logical and 12 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1
WarmupCount=3

```
| Method    | Count | Mean        | Error        | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|---------- |------ |------------:|-------------:|----------:|------:|--------:|----------:|------------:|
| **NewList**   | **8**     |    **15.27 ns** |     **6.933 ns** |  **0.380 ns** |  **1.00** |    **0.03** |      **88 B** |        **1.00** |
| Reservoir | 8     |    32.12 ns |    17.375 ns |  0.952 ns |  2.10 |    0.07 |         - |        0.00 |
|           |       |             |              |           |       |         |           |             |
| **NewList**   | **128**   |   **139.23 ns** |    **19.155 ns** |  **1.050 ns** |  **1.00** |    **0.01** |     **568 B** |        **1.00** |
| Reservoir | 128   |   126.66 ns |    60.627 ns |  3.323 ns |  0.91 |    0.02 |         - |        0.00 |
|           |       |             |              |           |       |         |           |             |
| **NewList**   | **2048**  | **1,738.18 ns** | **1,075.533 ns** | **58.954 ns** |  **1.00** |    **0.04** |    **8248 B** |        **1.00** |
| Reservoir | 2048  | 1,502.78 ns |    26.992 ns |  1.480 ns |  0.87 |    0.03 |         - |        0.00 |
