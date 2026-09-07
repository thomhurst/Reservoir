#if NETSTANDARD2_0
using Reservoir;

namespace ReservoirPackageConsumer;

// This fixture must be compiled against the netstandard2.0 package asset, then executed
// unchanged by a host that resolves a modern Reservoir asset.
public static class DestroyPolicyConsumer
{
    public static int ExplicitGeneric()
    {
        using var pool = new ObjectPool<Item, ExplicitPolicy>(default, 1);
        Item item = pool.Rent();
        pool.Return(item);
        return item.DestroyCount;
    }

    public static int ExplicitRuntime()
    {
        using var pool = new ObjectPool<Item>(new ExplicitPolicy(), 1);
        Item item = pool.Rent();
        pool.Return(item);
        return item.DestroyCount;
    }

    public static int ImplicitGeneric()
    {
        using var pool = new ObjectPool<Item, ImplicitPolicy>(default, 1);
        Item item = pool.Rent();
        pool.Return(item);
        return item.DestroyCount;
    }

    public static int ImplicitRuntime()
    {
        using var pool = new ObjectPool<Item>(new ImplicitPolicy(), 1);
        Item item = pool.Rent();
        pool.Return(item);
        return item.DestroyCount;
    }

    public static bool ExplicitStatefulGeneric()
    {
        using var pool = new ObjectPool<Item, StatefulExplicitPolicy>(default, 1);
        pool.Return(pool.Rent());
        Item retained = pool.Rent();
        pool.Return(retained);
        return retained.DestroyCount == 0;
    }

    private sealed class Item
    {
        internal int DestroyCount;
    }

    private readonly struct ExplicitPolicy : IPooledObjectDestroyPolicy<Item>
    {
        public Item Create() => new();
        public bool TryReset(Item item) => false;
        void IPooledObjectDestroyPolicy<Item>.Destroy(Item item) => item.DestroyCount++;
    }

    private readonly struct ImplicitPolicy : IPooledObjectDestroyPolicy<Item>
    {
        public Item Create() => new();
        public bool TryReset(Item item) => false;
        public void Destroy(Item item) => item.DestroyCount++;
    }

    private struct StatefulExplicitPolicy : IPooledObjectDestroyPolicy<Item>
    {
        private int _destroyCount;
        public Item Create() => new();
        public bool TryReset(Item item) => _destroyCount != 0;
        void IPooledObjectDestroyPolicy<Item>.Destroy(Item item)
        {
            _destroyCount++;
            item.DestroyCount++;
        }
    }
}
#endif
