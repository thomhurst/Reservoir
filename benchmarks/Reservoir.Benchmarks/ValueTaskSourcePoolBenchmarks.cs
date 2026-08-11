using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[BenchmarkCategory("ValueTaskSource")]
[MemoryDiagnoser(displayGenColumns: false)]
public class ValueTaskSourcePoolBenchmarks
{
    private readonly ValueTaskSourcePool<int> _pool = new(maxCapacity: 1);
    private readonly int _result = 42;

    [GlobalSetup]
    public void WarmPool() => _ = CompleteWithPooledSource();

    [GlobalCleanup]
    public void Cleanup() => _pool.Dispose();

    [Benchmark(Baseline = true)]
    public int TaskFromResult()
        => Task.FromResult(_result).GetAwaiter().GetResult();

    [Benchmark]
    public int TaskCompletionSource()
    {
        var source = new TaskCompletionSource<int>();
        source.SetResult(_result);
        return source.Task.GetAwaiter().GetResult();
    }

    [Benchmark]
    public int PooledValueTaskSource() => CompleteWithPooledSource();

    private int CompleteWithPooledSource()
    {
        PooledValueTaskSource<int> source = _pool.Rent();
        ValueTask<int> operation = source.CreateValueTask();
        source.SetResult(_result);
        return operation.GetAwaiter().GetResult();
    }
}
