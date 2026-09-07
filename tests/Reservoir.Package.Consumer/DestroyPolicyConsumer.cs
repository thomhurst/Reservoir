#if NETSTANDARD2_0
using Reservoir;

namespace ReservoirPackageConsumer;

// This fixture must be compiled against the netstandard2.0 package asset, then executed
// unchanged by a host that resolves a modern Reservoir asset.
public static class DestroyPolicyConsumer
{
    public static Type DestroyDeclaringInterface()
        => typeof(IPooledObjectPolicy<Item>);

    public static int ExplicitConstrained()
    {
        var policy = new StatefulExplicitPolicy();
        var item = new Item();
        DestroyConstrained(ref policy, item);
        DestroyConstrained(ref policy, item);
        return policy.DestroyCount;
    }

    private static void DestroyConstrained<TPolicy>(ref TPolicy policy, Item item)
        where TPolicy : struct, IPooledObjectDestroyPolicy<Item> => policy.Destroy(item);

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

    public static bool ExplicitStatefulRuntime()
    {
        using var pool = new ObjectPool<Item>(new StatefulExplicitPolicy(), 1);
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
        void IPooledObjectPolicy<Item>.Destroy(Item item) => item.DestroyCount++;
    }

    private readonly struct ImplicitPolicy : IPooledObjectDestroyPolicy<Item>
    {
        public Item Create() => new();
        public bool TryReset(Item item) => false;
        public void Destroy(Item item) => item.DestroyCount++;
    }

    private struct StatefulExplicitPolicy : IPooledObjectDestroyPolicy<Item>
    {
        internal int DestroyCount;
        public Item Create() => new();
        public bool TryReset(Item item) => DestroyCount != 0;
        void IPooledObjectPolicy<Item>.Destroy(Item item)
        {
            DestroyCount++;
            item.DestroyCount++;
        }
    }
}
#endif
