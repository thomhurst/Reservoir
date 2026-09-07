using Reservoir;

namespace ReservoirNativeAotTests;

internal static class Program
{
    private static void Main()
    {
        CancellationTokenSourcePoolSupportsNativeAot();
        CustomDestructionSupportsNativeAot();
        SharedScopedDestructionSupportsNativeAot();
        ExplicitDestructionPreservesPolicyState();
        DefaultPortableDestructionSupportsNativeAot();
        RuntimeDestructionPreservesPolicyState();
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

    private static void SharedScopedDestructionSupportsNativeAot()
    {
        using var generic = new ObjectPool<PooledItem, CustomDestructionPolicy>(maxCapacity: 1);
        var lease = generic.RentScopedShared(out PooledItem item);
        lease.Dispose();
        if (!item.IsDestroyed)
        {
            throw new InvalidOperationException("Shared-store scoped destruction failed.");
        }

        using var runtime = new ObjectPool<PooledItem>(new CustomDestructionPolicy(), maxCapacity: 1);
        var runtimeLease = runtime.RentScopedShared(out PooledItem runtimeItem);
        runtimeLease.Dispose();
        if (!runtimeItem.IsDestroyed)
        {
            throw new InvalidOperationException("Runtime shared-store scoped destruction failed.");
        }
    }

    private sealed class PooledItem : IDisposable
    {
        internal bool IsDestroyed { get; set; }

        public void Dispose() => IsDestroyed = true;
    }

    private static void DefaultPortableDestructionSupportsNativeAot()
    {
        using var pool = new ObjectPool<PooledItem, DefaultPortablePolicy>(maxCapacity: 1);
        PooledItem item = pool.Rent();
        pool.Return(item);
        if (!item.IsDestroyed)
        {
            throw new InvalidOperationException("Default portable destruction was not invoked.");
        }
    }

    private readonly struct DefaultPortablePolicy : IPooledObjectDestroyPolicy<PooledItem>
    {
        public PooledItem Create() => new();
        public bool TryReset(PooledItem item) => false;
    }

    private static void RuntimeDestructionPreservesPolicyState()
    {
        using var pool = new ObjectPool<PooledItem>(new ExplicitDestructionPolicy(), 1);
        PooledItem first = pool.Rent();
        pool.Return(first);
        PooledItem second = pool.Rent();
        pool.Return(second);
        if (!first.IsDestroyed || second.IsDestroyed)
        {
            throw new InvalidOperationException("Runtime destruction did not preserve policy state.");
        }
    }

    private static void ExplicitDestructionPreservesPolicyState()
    {
        using var pool = new ObjectPool<PooledItem, ExplicitDestructionPolicy>(maxCapacity: 1);
        PooledItem first = pool.Rent();
        pool.Return(first);
        PooledItem second = pool.Rent();
        pool.Return(second);
        if (!first.IsDestroyed || second.IsDestroyed)
        {
            throw new InvalidOperationException("Explicit destruction did not preserve policy state.");
        }
    }

    private struct ExplicitDestructionPolicy : IPooledObjectDestroyPolicy<PooledItem>
    {
        private int _destroyCount;
        public PooledItem Create() => new();
        public bool TryReset(PooledItem obj) => _destroyCount != 0;
        void IPooledObjectDestroyPolicy<PooledItem>.Destroy(PooledItem obj)
        {
            _destroyCount++;
            obj.IsDestroyed = true;
        }
    }

    private readonly struct CustomDestructionPolicy : IPooledObjectPolicy<PooledItem>
    {
        public PooledItem Create() => new();

        public bool TryReset(PooledItem obj) => false;

        public void Destroy(PooledItem obj) => obj.IsDestroyed = true;
    }
}
