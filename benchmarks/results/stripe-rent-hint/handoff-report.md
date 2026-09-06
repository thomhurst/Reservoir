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
IterationCount=12  LaunchCount=2  WarmupCount=5

```
| Method  | Job            | Runtime   | Arguments                                                                                                      | Capacity | ReturnLocation | PairCount | Mean     | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|-------- |--------------- |---------- |--------------------------------------------------------------------------------------------------------------- |--------- |--------------- |---------- |---------:|---------:|---------:|------:|--------:|----------:|------------:|
| Handoff | After-net10.0  | .NET 10.0 | Default                                                                                                        | 4096     | Distant        | 4         | 30.42 ns | 1.301 ns | 1.691 ns |  0.46 |    0.03 |         - |          NA |
| Handoff | After-net8.0   | .NET 8.0  | Default                                                                                                        | 4096     | Distant        | 4         | 33.21 ns | 1.341 ns | 1.744 ns |  0.50 |    0.03 |         - |          NA |
| Handoff | Before-net10.0 | .NET 10.0 | /p:BaselineDll=C:\git\Reservoir\artifacts\stripe-hint-baseline\src\Reservoir\bin\Release/net10.0/Reservoir.dll | 4096     | Distant        | 4         | 70.27 ns | 0.498 ns | 0.648 ns |  1.05 |    0.01 |         - |          NA |
| Handoff | Before-net8.0  | .NET 8.0  | /p:BaselineDll=C:\git\Reservoir\artifacts\stripe-hint-baseline\src\Reservoir\bin\Release/net8.0/Reservoir.dll  | 4096     | Distant        | 4         | 66.73 ns | 0.487 ns | 0.633 ns |  1.00 |    0.01 |         - |          NA |
