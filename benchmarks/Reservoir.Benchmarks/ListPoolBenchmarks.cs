using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
public class ListPoolBenchmarks
{
    private readonly ListPool<int> _pool = new(maxRetainedCapacity: 4096);

    [Params(8, 128, 2048)]
    public int Count { get; set; }

    [GlobalSetup]
    public void WarmPool()
    {
        List<int> list = _pool.Rent();
        list.EnsureCapacity(Count);
        _pool.Return(list);
    }

    [Benchmark(Baseline = true)]
    public int NewList()
    {
        var list = new List<int>(Count);
        return FillAndSum(list);
    }

    [Benchmark]
    public int Reservoir()
    {
        List<int> list = _pool.Rent();
        int result = FillAndSum(list);
        _pool.Return(list);
        return result;
    }

    private int FillAndSum(List<int> list)
    {
        int result = 0;

        for (int i = 0; i < Count; i++)
        {
            list.Add(i);
            result += list[i];
        }

        return result;
    }
}
