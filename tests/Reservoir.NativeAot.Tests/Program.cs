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
        ConstrainedDestructionPreservesCallerState();
        InheritedDestructionSupportsNativeAot();
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
        internal bool IsCustomDestroyed { get; set; }

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
        internal int DestroyCount;
        public PooledItem Create() => new();
        public bool TryReset(PooledItem obj) => DestroyCount != 0;
        void IPooledObjectPolicy<PooledItem>.Destroy(PooledItem obj)
        {
            DestroyCount++;
            obj.IsDestroyed = true;
        }
    }

    private static void ConstrainedDestructionPreservesCallerState()
    {
        var policy = new ExplicitDestructionPolicy();
        DestroyConstrained(ref policy, new PooledItem());
        DestroyConstrained(ref policy, new PooledItem());
        if (policy.DestroyCount != 2)
        {
            throw new InvalidOperationException("Constrained destruction lost caller-owned policy state.");
        }
    }

    private static void DestroyConstrained<TPolicy>(ref TPolicy policy, PooledItem item)
        where TPolicy : struct, IPooledObjectDestroyPolicy<PooledItem> => policy.Destroy(item);

    private static void InheritedDestructionSupportsNativeAot()
    {
        using var generic = new ObjectPool<PooledItem, InheritedPolicy>(maxCapacity: 1);
        using var runtime = new ObjectPool<PooledItem>(new InheritedPolicy(), 1);
        PooledItem first = generic.Rent();
        PooledItem second = runtime.Rent();
        generic.Return(first);
        runtime.Return(second);
        if (!first.IsCustomDestroyed || !second.IsCustomDestroyed || first.IsDestroyed || second.IsDestroyed)
        {
            throw new InvalidOperationException("Inherited destruction was bypassed.");
        }
    }

    private interface IInheritedPolicy : IPooledObjectDestroyPolicy<PooledItem>
    {
        void IPooledObjectPolicy<PooledItem>.Destroy(PooledItem obj) => obj.IsCustomDestroyed = true;
    }

    private readonly struct InheritedPolicy : IInheritedPolicy
    {
        public PooledItem Create() => new();
        public bool TryReset(PooledItem obj) => false;
    }

    private readonly struct CustomDestructionPolicy : IPooledObjectPolicy<PooledItem>
    {
        public PooledItem Create() => new();

        public bool TryReset(PooledItem obj) => false;

        public void Destroy(PooledItem obj) => obj.IsDestroyed = true;
    }
}
