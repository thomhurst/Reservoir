using Reservoir;

namespace ReservoirPackageConsumer;

internal static class PackageConsumer
{
    internal static object RentAndReturn()
    {
        var pool = new ObjectPool<PooledItem, Policy>(maxCapacity: 1);
        PooledItem item = pool.Rent();
        pool.Return(item);
        return pool.Rent();
    }

    internal static int ScopedCollection()
    {
        using ListPool<int>.Lease lease = ListPool<int>.Shared.RentScoped(out List<int> values);
        values.Add(42);
        return values[0];
    }

    internal static CancellationToken ScopedCancellationTokenSource()
    {
        using CancellationTokenSourcePool.Lease lease
            = CancellationTokenSourcePool.Shared.RentScoped(out CancellationTokenSource source);
        return source.Token;
    }

    private sealed class PooledItem
    {
    }

    private readonly struct Policy : IPooledObjectDestroyPolicy<PooledItem>
    {
        public PooledItem Create() => new();

        public bool TryReset(PooledItem obj) => true;

        public void Destroy(PooledItem obj)
        {
        }
    }
}
