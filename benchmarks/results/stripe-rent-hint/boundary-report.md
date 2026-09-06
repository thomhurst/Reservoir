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
| Method  | Job            | Runtime   | Arguments                                                                                                      | Capacity | ReturnLocation | PairCount | Mean     | Error     | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|-------- |--------------- |---------- |--------------------------------------------------------------------------------------------------------------- |--------- |--------------- |---------- |---------:|----------:|---------:|------:|--------:|----------:|------------:|
| **Handoff** | **After-net10.0**  | **.NET 10.0** | **Default**                                                                                                        | **65**       | **Home**           | **4**         | **28.65 ns** |  **9.533 ns** | **0.523 ns** |  **0.85** |    **0.02** |         **-** |          **NA** |
| Handoff | After-net8.0   | .NET 8.0  | Default                                                                                                        | 65       | Home           | 4         | 31.57 ns | 12.793 ns | 0.701 ns |  0.94 |    0.03 |         - |          NA |
| Handoff | Before-net10.0 | .NET 10.0 | /p:BaselineDll=C:\git\Reservoir\artifacts\stripe-hint-baseline\src\Reservoir\bin\Release/net10.0/Reservoir.dll | 65       | Home           | 4         | 32.13 ns | 36.056 ns | 1.976 ns |  0.95 |    0.06 |         - |          NA |
| Handoff | Before-net8.0  | .NET 8.0  | /p:BaselineDll=C:\git\Reservoir\artifacts\stripe-hint-baseline\src\Reservoir\bin\Release/net8.0/Reservoir.dll  | 65       | Home           | 4         | 33.70 ns | 16.615 ns | 0.911 ns |  1.00 |    0.03 |         - |          NA |
|         |                |           |                                                                                                                |          |                |           |          |           |          |       |         |           |             |
| **Handoff** | **After-net10.0**  | **.NET 10.0** | **Default**                                                                                                        | **65**       | **Distant**        | **4**         | **32.30 ns** | **16.518 ns** | **0.905 ns** |  **0.56** |    **0.01** |         **-** |          **NA** |
| Handoff | After-net8.0   | .NET 8.0  | Default                                                                                                        | 65       | Distant        | 4         | 33.34 ns | 31.156 ns | 1.708 ns |  0.58 |    0.03 |         - |          NA |
| Handoff | Before-net10.0 | .NET 10.0 | /p:BaselineDll=C:\git\Reservoir\artifacts\stripe-hint-baseline\src\Reservoir\bin\Release/net10.0/Reservoir.dll | 65       | Distant        | 4         | 58.85 ns |  4.760 ns | 0.261 ns |  1.02 |    0.01 |         - |          NA |
| Handoff | Before-net8.0  | .NET 8.0  | /p:BaselineDll=C:\git\Reservoir\artifacts\stripe-hint-baseline\src\Reservoir\bin\Release/net8.0/Reservoir.dll  | 65       | Distant        | 4         | 57.63 ns |  9.253 ns | 0.507 ns |  1.00 |    0.01 |         - |          NA |
|         |                |           |                                                                                                                |          |                |           |          |           |          |       |         |           |             |
| **Handoff** | **After-net10.0**  | **.NET 10.0** | **Default**                                                                                                        | **4096**     | **Home**           | **4**         | **28.76 ns** |  **9.709 ns** | **0.532 ns** |  **0.78** |    **0.05** |         **-** |          **NA** |
| Handoff | After-net8.0   | .NET 8.0  | Default                                                                                                        | 4096     | Home           | 4         | 32.54 ns | 40.869 ns | 2.240 ns |  0.88 |    0.07 |         - |          NA |
| Handoff | Before-net10.0 | .NET 10.0 | /p:BaselineDll=C:\git\Reservoir\artifacts\stripe-hint-baseline\src\Reservoir\bin\Release/net10.0/Reservoir.dll | 4096     | Home           | 4         | 33.33 ns | 81.717 ns | 4.479 ns |  0.90 |    0.12 |         - |          NA |
| Handoff | Before-net8.0  | .NET 8.0  | /p:BaselineDll=C:\git\Reservoir\artifacts\stripe-hint-baseline\src\Reservoir\bin\Release/net8.0/Reservoir.dll  | 4096     | Home           | 4         | 37.16 ns | 43.977 ns | 2.411 ns |  1.00 |    0.08 |         - |          NA |
