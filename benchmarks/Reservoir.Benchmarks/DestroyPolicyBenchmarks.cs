using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

// Cover portable destruction dispatch as well as the warm path. Rejected items are created
// once per operation because returning them transfers ownership and consumes the item.
[MemoryDiagnoser]
#if !RESERVOIR_NETSTANDARD
[DisassemblyDiagnoser(maxDepth: 3)]
#endif
public class DestroyPolicyBenchmarks
{
    private readonly ObjectPool<Item, RetainPolicy> _warm = new(maxCapacity: 1);
    private readonly ObjectPool<Item, RejectPolicy> _generic = new(maxCapacity: 1);
    private readonly ObjectPool<Item> _runtime = new(new RejectPolicy(), 1);
    private readonly ObjectPool<Item, DefaultDestroyPolicy> _default = new(maxCapacity: 1);

    [GlobalSetup]
    public void Setup()
    {
        BenchmarkAsset.Verify();
        _warm.Return(_warm.Rent());
    }

    [Benchmark]
    public Item WarmGeneric()
    {
        Item item = _warm.Rent();
        _warm.Return(item);
        return item;
    }

    [Benchmark]
    public int RejectGeneric()
    {
        Item item = _generic.Rent();
        _generic.Return(item);
        return item.DestroyCount;
    }

    [Benchmark]
    public int RejectRuntime()
    {
        Item item = _runtime.Rent();
        _runtime.Return(item);
        return item.DestroyCount;
    }

    [Benchmark]
    public int RejectDefault()
    {
        Item item = _default.Rent();
        _default.Return(item);
        return item.DestroyCount;
    }

    public sealed class Item : IDisposable
    {
        public int DestroyCount;
        public void Dispose() => DestroyCount++;
    }

    public readonly struct RetainPolicy : IPooledObjectDestroyPolicy<Item>
    {
        public Item Create() => new();
        public bool TryReset(Item item) => true;
        public void Destroy(Item item) => item.DestroyCount++;
    }

    public readonly struct RejectPolicy : IPooledObjectDestroyPolicy<Item>
    {
        public Item Create() => new();
        public bool TryReset(Item item) => false;
        public void Destroy(Item item) => item.DestroyCount++;
    }

    public readonly struct DefaultDestroyPolicy : IPooledObjectDestroyPolicy<Item>
    {
        public Item Create() => new();
        public bool TryReset(Item item) => false;
#if RESERVOIR_NETSTANDARD
        public void Destroy(Item item) => item.Dispose();
#endif
    }
}
