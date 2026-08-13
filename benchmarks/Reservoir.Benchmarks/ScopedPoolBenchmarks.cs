using System.Text;
using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[GenericTypeArguments(typeof(ScopedListAdapter))]
[GenericTypeArguments(typeof(ScopedQueueAdapter))]
[GenericTypeArguments(typeof(ScopedStackAdapter))]
[GenericTypeArguments(typeof(ScopedDictionaryAdapter))]
[GenericTypeArguments(typeof(ScopedHashSetAdapter))]
[GenericTypeArguments(typeof(ScopedStringBuilderAdapter))]
[MemoryDiagnoser(displayGenColumns: false)]
public class ScopedPoolBenchmarks<TAdapter>
    where TAdapter : struct, IScopedPoolBenchmarkAdapter
{
    private TAdapter _adapter = default;

    [GlobalSetup]
    public void WarmPools()
    {
        _ = _adapter.Shared();
        _ = _adapter.ThreadLocalShared();
        _ = _adapter.ScopedValue();
        _ = _adapter.ScopedOut();
    }

    [Benchmark(Baseline = true)]
    public int Shared() => _adapter.Shared();

    [Benchmark]
    public int ThreadLocalShared() => _adapter.ThreadLocalShared();

    [Benchmark]
    public int ScopedValue() => _adapter.ScopedValue();

    [Benchmark]
    public int ScopedOut() => _adapter.ScopedOut();
}

public interface IScopedPoolBenchmarkAdapter
{
    int Shared();
    int ThreadLocalShared();
    int ScopedValue();
    int ScopedOut();
}

public readonly struct ScopedListAdapter : IScopedPoolBenchmarkAdapter
{
    public int Shared()
    {
        List<int> item = ListPool<int>.Shared.Rent();
        int result = item.Count;
        ListPool<int>.Shared.Return(item);
        return result;
    }

    public int ThreadLocalShared()
    {
        List<int> item = ListPool<int>.ThreadLocalShared.Rent();
        int result = item.Count;
        ListPool<int>.ThreadLocalShared.Return(item);
        return result;
    }

    public int ScopedValue()
    {
        using ListPool<int>.Lease lease = ListPool<int>.Shared.RentScoped();
        return lease.Value.Count;
    }

    public int ScopedOut()
    {
        using ListPool<int>.Lease lease = ListPool<int>.Shared.RentScoped(out List<int> item);
        return item.Count;
    }
}

public readonly struct ScopedQueueAdapter : IScopedPoolBenchmarkAdapter
{
    public int Shared()
    {
        Queue<int> item = QueuePool<int>.Shared.Rent();
        int result = item.Count;
        QueuePool<int>.Shared.Return(item);
        return result;
    }

    public int ThreadLocalShared()
    {
        Queue<int> item = QueuePool<int>.ThreadLocalShared.Rent();
        int result = item.Count;
        QueuePool<int>.ThreadLocalShared.Return(item);
        return result;
    }

    public int ScopedValue()
    {
        using QueuePool<int>.Lease lease = QueuePool<int>.Shared.RentScoped();
        return lease.Value.Count;
    }

    public int ScopedOut()
    {
        using QueuePool<int>.Lease lease = QueuePool<int>.Shared.RentScoped(out Queue<int> item);
        return item.Count;
    }
}

public readonly struct ScopedStackAdapter : IScopedPoolBenchmarkAdapter
{
    public int Shared()
    {
        Stack<int> item = StackPool<int>.Shared.Rent();
        int result = item.Count;
        StackPool<int>.Shared.Return(item);
        return result;
    }

    public int ThreadLocalShared()
    {
        Stack<int> item = StackPool<int>.ThreadLocalShared.Rent();
        int result = item.Count;
        StackPool<int>.ThreadLocalShared.Return(item);
        return result;
    }

    public int ScopedValue()
    {
        using StackPool<int>.Lease lease = StackPool<int>.Shared.RentScoped();
        return lease.Value.Count;
    }

    public int ScopedOut()
    {
        using StackPool<int>.Lease lease = StackPool<int>.Shared.RentScoped(out Stack<int> item);
        return item.Count;
    }
}

public readonly struct ScopedDictionaryAdapter : IScopedPoolBenchmarkAdapter
{
    public int Shared()
    {
        Dictionary<int, int> item = DictionaryPool<int, int>.Shared.Rent();
        int result = item.Count;
        DictionaryPool<int, int>.Shared.Return(item);
        return result;
    }

    public int ThreadLocalShared()
    {
        Dictionary<int, int> item = DictionaryPool<int, int>.ThreadLocalShared.Rent();
        int result = item.Count;
        DictionaryPool<int, int>.ThreadLocalShared.Return(item);
        return result;
    }

    public int ScopedValue()
    {
        using DictionaryPool<int, int>.Lease lease
            = DictionaryPool<int, int>.Shared.RentScoped();
        return lease.Value.Count;
    }

    public int ScopedOut()
    {
        using DictionaryPool<int, int>.Lease lease
            = DictionaryPool<int, int>.Shared.RentScoped(out Dictionary<int, int> item);
        return item.Count;
    }
}

public readonly struct ScopedHashSetAdapter : IScopedPoolBenchmarkAdapter
{
    public int Shared()
    {
        HashSet<int> item = HashSetPool<int>.Shared.Rent();
        int result = item.Count;
        HashSetPool<int>.Shared.Return(item);
        return result;
    }

    public int ThreadLocalShared()
    {
        HashSet<int> item = HashSetPool<int>.ThreadLocalShared.Rent();
        int result = item.Count;
        HashSetPool<int>.ThreadLocalShared.Return(item);
        return result;
    }

    public int ScopedValue()
    {
        using HashSetPool<int>.Lease lease = HashSetPool<int>.Shared.RentScoped();
        return lease.Value.Count;
    }

    public int ScopedOut()
    {
        using HashSetPool<int>.Lease lease
            = HashSetPool<int>.Shared.RentScoped(out HashSet<int> item);
        return item.Count;
    }
}

public readonly struct ScopedStringBuilderAdapter : IScopedPoolBenchmarkAdapter
{
    public int Shared()
    {
        StringBuilder item = StringBuilderPool.Shared.Rent();
        int result = item.Length;
        StringBuilderPool.Shared.Return(item);
        return result;
    }

    public int ThreadLocalShared()
    {
        StringBuilder item = StringBuilderPool.ThreadLocalShared.Rent();
        int result = item.Length;
        StringBuilderPool.ThreadLocalShared.Return(item);
        return result;
    }

    public int ScopedValue()
    {
        using StringBuilderPool.Lease lease = StringBuilderPool.Shared.RentScoped();
        return lease.Value.Length;
    }

    public int ScopedOut()
    {
        using StringBuilderPool.Lease lease
            = StringBuilderPool.Shared.RentScoped(out StringBuilder item);
        return item.Length;
    }
}
