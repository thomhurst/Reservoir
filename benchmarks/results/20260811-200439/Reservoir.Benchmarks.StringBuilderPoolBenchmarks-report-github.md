```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8973/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i7-12700K 3.60GHz, 1 CPU, 20 logical and 12 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1
WarmupCount=3

```
| Method            | Mean      | Error      | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------ |----------:|-----------:|----------:|------:|--------:|----------:|------------:|
| NewStringBuilder  | 25.658 ns | 22.3417 ns | 1.2246 ns |  1.00 |    0.06 |     400 B |        1.00 |
| Reservoir         | 18.122 ns |  9.1871 ns | 0.5036 ns |  0.71 |    0.03 |         - |        0.00 |
| ThreadStaticCache |  5.517 ns |  0.4956 ns | 0.0272 ns |  0.22 |    0.01 |         - |        0.00 |
