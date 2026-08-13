using Reservoir;

namespace ReservoirNativeAotTests;

internal static class Program
{
    private static void Main()
    {
        CancellationTokenSourcePoolSupportsNativeAot();
        CustomDestructionSupportsNativeAot();
    }

    private static void CancellationTokenSourcePoolSupportsNativeAot()
    {
        using var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        CancellationTokenSource source = pool.Rent();
        source.Cancel();
        source.Dispose();

        CancellationTokenSource reusedSource = pool.Rent();
        if (reusedSource.IsCancellationRequested)
        {
            throw new InvalidOperationException("Rented source was not reset.");
        }

        reusedSource.Dispose();
    }

    private static void CustomDestructionSupportsNativeAot()
    {
        using var pool = new ObjectPool<PooledItem, CustomDestructionPolicy>(maxCapacity: 1);
        PooledItem item = pool.Rent();

        pool.Return(item);

        if (!item.IsDestroyed)
        {
            throw new InvalidOperationException("Custom destroy policy was not invoked.");
        }
    }

    private sealed class PooledItem
    {
        internal bool IsDestroyed { get; set; }
    }

    private readonly struct CustomDestructionPolicy : IPooledObjectPolicy<PooledItem>
    {
        public PooledItem Create() => new();

        public bool TryReset(PooledItem obj) => false;

        public void Destroy(PooledItem obj) => obj.IsDestroyed = true;
    }
}
