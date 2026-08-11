```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8973/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i7-12700K 3.60GHz, 1 CPU, 20 logical and 12 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1
WarmupCount=3

```
| Method            | Mean     | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------ |---------:|---------:|---------:|------:|--------:|----------:|------------:|
| ObjectPool        | 11.52 ns | 3.537 ns | 0.194 ns |  1.00 |    0.02 |         - |          NA |
| ListPool          | 13.13 ns | 0.428 ns | 0.023 ns |  1.14 |    0.02 |         - |          NA |
| DictionaryPool    | 12.21 ns | 0.637 ns | 0.035 ns |  1.06 |    0.02 |         - |          NA |
| HashSetPool       | 15.04 ns | 6.537 ns | 0.358 ns |  1.31 |    0.03 |         - |          NA |
| QueuePool         | 13.62 ns | 4.553 ns | 0.250 ns |  1.18 |    0.03 |         - |          NA |
| StackPool         | 13.86 ns | 1.273 ns | 0.070 ns |  1.20 |    0.02 |         - |          NA |
| StringBuilderPool | 14.96 ns | 2.025 ns | 0.111 ns |  1.30 |    0.02 |         - |          NA |
