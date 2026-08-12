using Reservoir;

namespace ReservoirPackageConsumer;

internal static class PackageConsumer
{
    internal static void EnableDiagnosticsForDebugBuilds()
        => ObjectPoolDiagnostics.EnableForDebugBuilds();

    internal static object RentAndReturn()
    {
        var pool = new ObjectPool<PooledItem, Policy>(maxCapacity: 1);
        PooledItem item = pool.Rent();
        pool.Return(item);
        return pool.Rent();
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
