```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9168/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i7-12700K 3.60GHz, 1 CPU, 20 logical and 12 physical cores
.NET SDK 10.0.400
  [Host]    : .NET 8.0.30 (8.0.30, 8.0.3026.36720), X64 RyuJIT x86-64-v3
  baseline  : .NET 8.0.30 (8.0.30, 8.0.3026.36720), X64 RyuJIT x86-64-v3
  candidate : .NET 8.0.30 (8.0.30, 8.0.3026.36720), X64 RyuJIT x86-64-v3

EvaluateOverhead=False  OutlierMode=DontRemove  IterationCount=10  
UnrollFactor=16  WarmupCount=1  

```
| Method                        | Job       | Arguments                                                                                                            | InvocationCount | Mean     | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------------ |---------- |--------------------------------------------------------------------------------------------------------------------- |---------------- |---------:|---------:|---------:|------:|--------:|----------:|------------:|
| ManualThreadLocal             | baseline  | /p:BaselineDll=C:\git\Reservoir-worktrees\issue-98-weak-clear-record\artifacts\issue98-baseline\net8.0\Reservoir.dll | 33554432        | 17.70 ns | 0.397 ns | 0.263 ns |  1.00 |    0.02 |         - |          NA |
| ManualThreadLocal             | candidate | Default                                                                                                              | 33554432        | 17.85 ns | 0.472 ns | 0.312 ns |  1.01 |    0.02 |         - |          NA |
|                               |           |                                                                                                                      |                 |          |          |          |       |         |           |             |
| CancellationTokenSourceScoped | baseline  | /p:BaselineDll=C:\git\Reservoir-worktrees\issue-98-weak-clear-record\artifacts\issue98-baseline\net8.0\Reservoir.dll | 33554432        | 18.20 ns | 0.389 ns | 0.258 ns |  1.00 |    0.02 |         - |          NA |
| CancellationTokenSourceScoped | candidate | Default                                                                                                              | 33554432        | 18.46 ns | 0.350 ns | 0.231 ns |  1.01 |    0.02 |         - |          NA |
|                               |           |                                                                                                                      |                 |          |          |          |       |         |           |             |
| Scoped                        | baseline  | /p:BaselineDll=C:\git\Reservoir-worktrees\issue-98-weak-clear-record\artifacts\issue98-baseline\net8.0\Reservoir.dll | 67108864        | 13.61 ns | 0.320 ns | 0.211 ns |  1.00 |    0.02 |         - |          NA |
| Scoped                        | candidate | Default                                                                                                              | 67108864        | 13.64 ns | 0.228 ns | 0.151 ns |  1.00 |    0.02 |         - |          NA |
|                               |           |                                                                                                                      |                 |          |          |          |       |         |           |             |
| ScopedOut                     | baseline  | /p:BaselineDll=C:\git\Reservoir-worktrees\issue-98-weak-clear-record\artifacts\issue98-baseline\net8.0\Reservoir.dll | 67108864        | 13.38 ns | 0.185 ns | 0.122 ns |  1.00 |    0.01 |         - |          NA |
| ScopedOut                     | candidate | Default                                                                                                              | 67108864        | 13.41 ns | 0.220 ns | 0.146 ns |  1.00 |    0.01 |         - |          NA |
