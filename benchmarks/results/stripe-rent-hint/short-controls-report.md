```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9168/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i7-12700K 3.60GHz, 1 CPU, 20 logical and 12 physical cores
.NET SDK 11.0.100-preview.7.26381.103
  [Host]         : .NET 10.0.11 (10.0.11, 10.0.1126.37416), X64 RyuJIT x86-64-v3
  After-net10.0  : .NET 10.0.11 (10.0.11, 10.0.1126.37416), X64 RyuJIT x86-64-v3
  After-net8.0   : .NET 8.0.30 (8.0.30, 8.0.3026.36720), X64 RyuJIT x86-64-v3
  Before-net10.0 : .NET 10.0.11 (10.0.11, 10.0.1126.37416), X64 RyuJIT x86-64-v3
  Before-net8.0  : .NET 8.0.30 (8.0.30, 8.0.3026.36720), X64 RyuJIT x86-64-v3

OutlierMode=DontRemove  Affinity=00001111111111111111  EnvironmentVariables=DOTNET_PROCESSOR_COUNT=20
IterationCount=3  LaunchCount=1  WarmupCount=3

```
| Method     | Job            | Runtime   | Arguments                                                                                                      | Mean     | Error     | StdDev   | Ratio | RatioSD | Code Size | Allocated | Alloc Ratio |
|----------- |--------------- |---------- |--------------------------------------------------------------------------------------------------------------- |---------:|----------:|---------:|------:|--------:|----------:|----------:|------------:|
| SameThread | After-net10.0  | .NET 10.0 | Default                                                                                                        | 12.85 ns |  2.371 ns | 0.130 ns |  0.81 |    0.08 |   4,141 B |         - |          NA |
| SameThread | After-net8.0   | .NET 8.0  | Default                                                                                                        | 19.23 ns | 71.221 ns | 3.904 ns |  1.22 |    0.24 |   3,682 B |         - |          NA |
| SameThread | Before-net10.0 | .NET 10.0 | /p:BaselineDll=C:\git\Reservoir\artifacts\stripe-hint-baseline\src\Reservoir\bin\Release/net10.0/Reservoir.dll | 13.21 ns |  1.599 ns | 0.088 ns |  0.84 |    0.08 |   3,495 B |         - |          NA |
| SameThread | Before-net8.0  | .NET 8.0  | /p:BaselineDll=C:\git\Reservoir\artifacts\stripe-hint-baseline\src\Reservoir\bin\Release/net8.0/Reservoir.dll  | 15.94 ns | 33.586 ns | 1.841 ns |  1.01 |    0.14 |   3,232 B |         - |          NA |
|            |                |           |                                                                                                                |          |           |          |       |         |           |           |             |
| DenseBurst | After-net10.0  | .NET 10.0 | Default                                                                                                        | 23.05 ns |  6.538 ns | 0.358 ns |  0.81 |    0.08 |   4,310 B |         - |          NA |
| DenseBurst | After-net8.0   | .NET 8.0  | Default                                                                                                        | 28.48 ns |  6.508 ns | 0.357 ns |  1.01 |    0.09 |   3,844 B |         - |          NA |
| DenseBurst | Before-net10.0 | .NET 10.0 | /p:BaselineDll=C:\git\Reservoir\artifacts\stripe-hint-baseline\src\Reservoir\bin\Release/net10.0/Reservoir.dll | 29.00 ns | 18.049 ns | 0.989 ns |  1.02 |    0.10 |   3,479 B |         - |          NA |
| DenseBurst | Before-net8.0  | .NET 8.0  | /p:BaselineDll=C:\git\Reservoir\artifacts\stripe-hint-baseline\src\Reservoir\bin\Release/net8.0/Reservoir.dll  | 28.57 ns | 59.908 ns | 3.284 ns |  1.01 |    0.14 |   3,158 B |         - |          NA |
