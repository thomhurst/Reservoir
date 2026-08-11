using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

public class ScaffoldingBenchmarks
{
    [Benchmark]
    public int ReturnOne() => 1;
}
