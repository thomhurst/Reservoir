using System.Text;
using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[GenericTypeArguments(typeof(ListAdapter))]
[GenericTypeArguments(typeof(QueueAdapter))]
[GenericTypeArguments(typeof(StackAdapter))]
[GenericTypeArguments(typeof(DictionaryAdapter))]
[GenericTypeArguments(typeof(HashSetAdapter))]
[GenericTypeArguments(typeof(StringBuilderAdapter))]
[MemoryDiagnoser(displayGenColumns: false)]
public class ThreadLocalPoolBenchmarks<TAdapter>
    where TAdapter : struct, IThreadLocalPoolAdapter
{
    private TAdapter _adapter = default;

    [GlobalSetup]
    public void WarmPools()
    {
        object shared = _adapter.RentShared();
        _adapter.ReturnShared(shared);
        object threadLocal = _adapter.RentThreadLocal();
        _adapter.ReturnThreadLocal(threadLocal);
    }

    [Benchmark(Baseline = true)]
    public object Shared()
    {
        object item = _adapter.RentShared();
        _adapter.ReturnShared(item);
        return item;
    }

    [Benchmark]
    public object ThreadLocalShared()
    {
        object item = _adapter.RentThreadLocal();
        _adapter.ReturnThreadLocal(item);
        return item;
    }
}

public interface IThreadLocalPoolAdapter
{
    object RentShared();
    void ReturnShared(object item);
    object RentThreadLocal();
    void ReturnThreadLocal(object item);
}

public readonly struct ListAdapter : IThreadLocalPoolAdapter
{
    public object RentShared() => ListPool<int>.Shared.Rent();
    public void ReturnShared(object item) => ListPool<int>.Shared.Return((List<int>)item);
    public object RentThreadLocal() => ListPool<int>.ThreadLocalShared.Rent();
    public void ReturnThreadLocal(object item)
        => ListPool<int>.ThreadLocalShared.Return((List<int>)item);
}

public readonly struct QueueAdapter : IThreadLocalPoolAdapter
{
    public object RentShared() => QueuePool<int>.Shared.Rent();
    public void ReturnShared(object item) => QueuePool<int>.Shared.Return((Queue<int>)item);
    public object RentThreadLocal() => QueuePool<int>.ThreadLocalShared.Rent();
    public void ReturnThreadLocal(object item)
        => QueuePool<int>.ThreadLocalShared.Return((Queue<int>)item);
}

public readonly struct StackAdapter : IThreadLocalPoolAdapter
{
    public object RentShared() => StackPool<int>.Shared.Rent();
    public void ReturnShared(object item) => StackPool<int>.Shared.Return((Stack<int>)item);
    public object RentThreadLocal() => StackPool<int>.ThreadLocalShared.Rent();
    public void ReturnThreadLocal(object item)
        => StackPool<int>.ThreadLocalShared.Return((Stack<int>)item);
}

public readonly struct DictionaryAdapter : IThreadLocalPoolAdapter
{
    public object RentShared() => DictionaryPool<int, int>.Shared.Rent();
    public void ReturnShared(object item)
        => DictionaryPool<int, int>.Shared.Return((Dictionary<int, int>)item);
    public object RentThreadLocal() => DictionaryPool<int, int>.ThreadLocalShared.Rent();
    public void ReturnThreadLocal(object item)
        => DictionaryPool<int, int>.ThreadLocalShared.Return((Dictionary<int, int>)item);
}

public readonly struct HashSetAdapter : IThreadLocalPoolAdapter
{
    public object RentShared() => HashSetPool<int>.Shared.Rent();
    public void ReturnShared(object item) => HashSetPool<int>.Shared.Return((HashSet<int>)item);
    public object RentThreadLocal() => HashSetPool<int>.ThreadLocalShared.Rent();
    public void ReturnThreadLocal(object item)
        => HashSetPool<int>.ThreadLocalShared.Return((HashSet<int>)item);
}

public readonly struct StringBuilderAdapter : IThreadLocalPoolAdapter
{
    public object RentShared() => StringBuilderPool.Shared.Rent();
    public void ReturnShared(object item)
        => StringBuilderPool.Shared.Return((StringBuilder)item);
    public object RentThreadLocal() => StringBuilderPool.ThreadLocalShared.Rent();
    public void ReturnThreadLocal(object item)
        => StringBuilderPool.ThreadLocalShared.Return((StringBuilder)item);
}
