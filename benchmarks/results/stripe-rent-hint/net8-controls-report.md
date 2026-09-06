```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9168/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i7-12700K 3.60GHz, 1 CPU, 20 logical and 12 physical cores
.NET SDK 11.0.100-preview.7.26381.103
  [Host]        : .NET 10.0.11 (10.0.11, 10.0.1126.37416), X64 RyuJIT x86-64-v3
  After-net8.0  : .NET 8.0.30 (8.0.30, 8.0.3026.36720), X64 RyuJIT x86-64-v3
  Before-net8.0 : .NET 8.0.30 (8.0.30, 8.0.3026.36720), X64 RyuJIT x86-64-v3

OutlierMode=DontRemove  Affinity=00001111111111111111  EnvironmentVariables=DOTNET_PROCESSOR_COUNT=20
Runtime=.NET 8.0  IterationCount=12  LaunchCount=2
WarmupCount=5

```
| Method     | Job           | Arguments                                                                                                     | Mean     | Error    | StdDev   | Ratio | RatioSD | Code Size | Allocated | Alloc Ratio |
|----------- |-------------- |-------------------------------------------------------------------------------------------------------------- |---------:|---------:|---------:|------:|--------:|----------:|----------:|------------:|
| SameThread | After-net8.0  | Default                                                                                                       | 14.98 ns | 0.664 ns | 0.863 ns |  1.01 |    0.06 |   3,695 B |         - |          NA |
| SameThread | Before-net8.0 | /p:BaselineDll=C:\git\Reservoir\artifacts\stripe-hint-baseline\src\Reservoir\bin\Release/net8.0/Reservoir.dll | 14.87 ns | 0.213 ns | 0.277 ns |  1.00 |    0.03 |   3,232 B |         - |          NA |
|            |               |                                                                                                               |          |          |          |       |         |           |           |             |
| DenseBurst | After-net8.0  | Default                                                                                                       | 25.96 ns | 0.542 ns | 0.704 ns |  1.02 |    0.03 |   3,844 B |         - |          NA |
| DenseBurst | Before-net8.0 | /p:BaselineDll=C:\git\Reservoir\artifacts\stripe-hint-baseline\src\Reservoir\bin\Release/net8.0/Reservoir.dll | 25.40 ns | 0.350 ns | 0.455 ns |  1.00 |    0.02 |   3,175 B |         - |          NA |
