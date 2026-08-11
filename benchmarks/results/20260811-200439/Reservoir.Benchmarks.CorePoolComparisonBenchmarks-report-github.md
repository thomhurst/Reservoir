```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8973/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i7-12700K 3.60GHz, 1 CPU, 20 logical and 12 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1
WarmupCount=3

```
| Method                        | Mean     | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------------ |---------:|---------:|---------:|------:|--------:|----------:|------------:|
| New                           | 12.67 ns | 4.206 ns | 0.231 ns |  1.00 |    0.02 |     304 B |        1.00 |
| Reservoir                     | 11.83 ns | 0.742 ns | 0.041 ns |  0.93 |    0.02 |         - |        0.00 |
| MicrosoftExtensionsObjectPool | 14.56 ns | 4.868 ns | 0.267 ns |  1.15 |    0.03 |         - |        0.00 |
| ConcurrentBag                 | 39.48 ns | 4.008 ns | 0.220 ns |  3.12 |    0.05 |         - |        0.00 |
