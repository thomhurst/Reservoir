```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8973/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i7-12700K 3.60GHz, 1 CPU, 20 logical and 12 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1
WarmupCount=3

```
| Method              | Mean     | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|-------------------- |---------:|---------:|---------:|------:|--------:|----------:|------------:|
| RentReturn          | 10.50 ns | 2.486 ns | 0.136 ns |  1.00 |    0.02 |         - |          NA |
| ScopedRentReturn    | 12.67 ns | 1.435 ns | 0.079 ns |  1.21 |    0.01 |         - |          NA |
| ScopedOutRentReturn | 12.46 ns | 1.076 ns | 0.059 ns |  1.19 |    0.01 |         - |          NA |
