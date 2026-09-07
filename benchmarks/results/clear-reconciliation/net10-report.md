```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9168/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i7-12700K 3.60GHz, 1 CPU, 20 logical and 12 physical cores
.NET SDK 10.0.400
  [Host]    : .NET 10.0.11 (10.0.11, 10.0.1126.37416), X64 RyuJIT x86-64-v3
  baseline  : .NET 10.0.11 (10.0.11, 10.0.1126.37416), X64 RyuJIT x86-64-v3
  candidate : .NET 10.0.11 (10.0.11, 10.0.1126.37416), X64 RyuJIT x86-64-v3

EvaluateOverhead=False  OutlierMode=DontRemove  IterationCount=10  
UnrollFactor=16  WarmupCount=1  

```
| Method                        | Job       | Arguments                                                                                                             | InvocationCount | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------------ |---------- |---------------------------------------------------------------------------------------------------------------------- |---------------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| ManualThreadLocal             | baseline  | /p:BaselineDll=C:\git\Reservoir-worktrees\issue-98-weak-clear-record\artifacts\issue98-baseline\net10.0\Reservoir.dll | 134217728       |  7.468 ns | 0.1842 ns | 0.1218 ns |  1.00 |    0.02 |         - |          NA |
| ManualThreadLocal             | candidate | Default                                                                                                               | 134217728       |  7.342 ns | 0.1158 ns | 0.0766 ns |  0.98 |    0.02 |         - |          NA |
|                               |           |                                                                                                                       |                 |           |           |           |       |         |           |             |
| ScopedOut                     | baseline  | /p:BaselineDll=C:\git\Reservoir-worktrees\issue-98-weak-clear-record\artifacts\issue98-baseline\net10.0\Reservoir.dll | 134217728       |  6.574 ns | 0.1142 ns | 0.0755 ns |  1.00 |    0.02 |         - |          NA |
| ScopedOut                     | candidate | Default                                                                                                               | 134217728       |  6.575 ns | 0.1359 ns | 0.0899 ns |  1.00 |    0.02 |         - |          NA |
|                               |           |                                                                                                                       |                 |           |           |           |       |         |           |             |
| Scoped                        | baseline  | /p:BaselineDll=C:\git\Reservoir-worktrees\issue-98-weak-clear-record\artifacts\issue98-baseline\net10.0\Reservoir.dll | 67108864        |  9.135 ns | 0.2092 ns | 0.1384 ns |  1.00 |    0.02 |         - |          NA |
| Scoped                        | candidate | Default                                                                                                               | 67108864        |  9.127 ns | 0.1480 ns | 0.0979 ns |  1.00 |    0.02 |         - |          NA |
|                               |           |                                                                                                                       |                 |           |           |           |       |         |           |             |
| CancellationTokenSourceScoped | baseline  | /p:BaselineDll=C:\git\Reservoir-worktrees\issue-98-weak-clear-record\artifacts\issue98-baseline\net10.0\Reservoir.dll | 67108864        | 10.056 ns | 0.2425 ns | 0.1604 ns |  1.00 |    0.02 |         - |          NA |
| CancellationTokenSourceScoped | candidate | Default                                                                                                               | 67108864        |  9.824 ns | 0.1838 ns | 0.1216 ns |  0.98 |    0.02 |         - |          NA |
