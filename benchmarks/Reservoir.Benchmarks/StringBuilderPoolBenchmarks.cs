using System.Text;
using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
public class StringBuilderPoolBenchmarks
{
    private const int MaximumRetainedCapacity = 4096;

    [ThreadStatic]
    private static StringBuilder? s_cachedBuilder;

    private readonly StringBuilderPool _pool = new(MaximumRetainedCapacity);
    private readonly string _text = new('x', 128);

    [GlobalSetup]
    public void WarmCaches()
    {
        StringBuilder pooledBuilder = _pool.Rent();
        pooledBuilder.EnsureCapacity(_text.Length);
        _pool.Return(pooledBuilder);

        StringBuilder cachedBuilder = AcquireCachedBuilder();
        cachedBuilder.EnsureCapacity(_text.Length);
        ReleaseCachedBuilder(cachedBuilder);
    }

    [Benchmark(Baseline = true)]
    public int NewStringBuilder()
    {
        var builder = new StringBuilder();
        builder.Append(_text);
        return builder.Length;
    }

    [Benchmark]
    public int Reservoir()
    {
        StringBuilder builder = _pool.Rent();
        builder.Append(_text);
        int result = builder.Length;
        _pool.Return(builder);
        return result;
    }

    [Benchmark]
    public int ThreadStaticCache()
    {
        StringBuilder builder = AcquireCachedBuilder();
        builder.Append(_text);
        int result = builder.Length;
        ReleaseCachedBuilder(builder);
        return result;
    }

    private static StringBuilder AcquireCachedBuilder()
    {
        StringBuilder? builder = s_cachedBuilder;
        if (builder is null)
        {
            return new StringBuilder();
        }

        s_cachedBuilder = null;
        return builder;
    }

    private static void ReleaseCachedBuilder(StringBuilder builder)
    {
        builder.Clear();
        if (builder.Capacity <= MaximumRetainedCapacity)
        {
            s_cachedBuilder = builder;
        }
    }
}
