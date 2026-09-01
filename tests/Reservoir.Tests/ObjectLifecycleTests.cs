namespace Reservoir.Tests;

public class ObjectLifecycleTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task ResettablePolicyCallsTryResetOnReturn()
    {
        var pool = new ObjectPool<ResettableItem, ResettablePooledObjectPolicy<ResettableItem>>(
            maxCapacity: 1);
        ResettableItem item = pool.Rent();

        pool.Return(item);

        await Assert.That(item.ResetCount).IsEqualTo(1);
        await Assert.That(pool.Rent()).IsSameReferenceAs(item);
    }

    [Test]
    public async Task ResetFailureDisposesAndDiscardsItem()
    {
        var pool = new ObjectPool<ResettableItem, ResettablePooledObjectPolicy<ResettableItem>>(
            maxCapacity: 1);
        ResettableItem discarded = pool.Rent();
        discarded.CanReset = false;

        pool.Return(discarded);
        ResettableItem replacement = pool.Rent();

        await Assert.That(discarded.ResetCount).IsEqualTo(1);
        await Assert.That(discarded.DisposeCount).IsEqualTo(1);
        await Assert.That(replacement).IsNotSameReferenceAs(discarded);
    }

    [Test]
    public async Task ResetExceptionDisposesItemAndPropagates()
    {
        var expected = new InvalidOperationException("Reset failed.");
        var pool = new ObjectPool<DisposableItem, ThrowingPolicy>(
            new ThrowingPolicy(expected),
            maxCapacity: 1);
        DisposableItem item = pool.Rent();
        Exception? caught = null;

        try
        {
            pool.Return(item);
        }
        catch (Exception exception)
        {
            caught = exception;
        }

        await Assert.That(caught).IsSameReferenceAs(expected);
        await Assert.That(item.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task ScopedResetExceptionDisposesItemAndPropagates()
    {
        var expected = new InvalidOperationException("Reset failed.");
        var pool = new ObjectPool<DisposableItem, ThrowingPolicy>(
            new ThrowingPolicy(expected),
            maxCapacity: 1);
        PooledLease<DisposableItem, ThrowingPolicy> lease = pool.RentScoped();
        DisposableItem item = lease.Value;
        Exception? caught = null;

        try
        {
            lease.Dispose();
        }
        catch (Exception exception)
        {
            caught = exception;
        }

        await Assert.That(caught).IsSameReferenceAs(expected);
        await Assert.That(item.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task PolicyCanOverrideDiscardDestruction()
    {
        var pool = new ObjectPool<DisposableItem, CustomDestructionPolicy>(maxCapacity: 1);
        DisposableItem item = pool.Rent();

        pool.Return(item);

        await Assert.That(item.DisposeCount).IsEqualTo(0);
        await Assert.That(item.DestroyCount).IsEqualTo(1);
    }

    [Test]
    public async Task CustomDestructionCanMutatePolicyState()
    {
        var pool = new ObjectPool<DisposableItem, StatefulDestructionPolicy>(maxCapacity: 1);
        DisposableItem discarded = pool.Rent();
        DisposableItem retained = pool.Rent();

        pool.Return(discarded);
        pool.Return(retained);

        await Assert.That(pool.Rent()).IsSameReferenceAs(retained);
    }

    [Test]
    public async Task PolicyAdapterPreservesCustomDestruction()
    {
        var pool = new ObjectPool<DisposableItem>(
            new CustomDestructionPolicy(),
            maxCapacity: 1);
        DisposableItem item = pool.Rent();

        pool.Return(item);

        await Assert.That(item.DisposeCount).IsEqualTo(0);
        await Assert.That(item.DestroyCount).IsEqualTo(1);
    }

    [Test]
    public async Task FullPoolDisposesReturnedExcessItem()
    {
        var pool = new ObjectPool<DisposableItem, DisposablePolicy>(
            default,
            maxCapacity: 1,
            threadLocalFastPath: false);
        DisposableItem retained = pool.Rent();
        DisposableItem excess = pool.Rent();

        pool.Return(retained);
        pool.Return(excess);

        await Assert.That(retained.DisposeCount).IsEqualTo(0);
        await Assert.That(excess.DisposeCount).IsEqualTo(1);
        await Assert.That(pool.Rent()).IsSameReferenceAs(retained);
    }

    [Test]
    public async Task LargeFullPoolDisposesReturnedExcessItem()
    {
        const int capacity = 65;
        var pool = new ObjectPool<DisposableItem, DisposablePolicy>(
            default,
            capacity,
            threadLocalFastPath: false);
        var retained = new DisposableItem[capacity];

        for (int i = 0; i < retained.Length; i++)
        {
            retained[i] = pool.Rent();
        }

        DisposableItem excess = pool.Rent();

        foreach (DisposableItem item in retained)
        {
            pool.Return(item);
        }

        pool.Return(excess);

        await Assert.That(excess.DisposeCount).IsEqualTo(1);
        await Assert.That(retained.All(item => item.DisposeCount == 0)).IsTrue();
    }

    [Test]
    public async Task ResetFailureDisposesRuntimeDisposableSubtype()
    {
        var pool = new ObjectPool<object, RejectingObjectPolicy>(maxCapacity: 1);
        var item = (DisposableItem)pool.Rent();

        pool.Return(item);

        await Assert.That(item.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task ClearDisposesRuntimeDisposableSubtype()
    {
        var pool = new ObjectPool<object, ObjectPolicy>(maxCapacity: 1);
        var item = (DisposableItem)pool.Rent();
        pool.Return(item);

        pool.Clear();

        await Assert.That(item.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task ClearDisposesRetainedItemsAndLeavesPoolUsable()
    {
        var pool = new ObjectPool<DisposableItem, DisposablePolicy>(maxCapacity: 2);
        DisposableItem first = pool.Rent();
        DisposableItem second = pool.Rent();
        pool.Return(first);
        pool.Return(second);

        pool.Clear();

        await Assert.That(first.DisposeCount).IsEqualTo(1);
        await Assert.That(second.DisposeCount).IsEqualTo(1);
        await Assert.That(pool.Rent()).IsNotSameReferenceAs(first);
    }

    [Test]
    public async Task ClearDisposesScopedThreadLocalItemAndLeavesPoolUsable()
    {
        var pool = new ObjectPool<DisposableItem, DisposablePolicy>(maxCapacity: 1);
        DisposableItem retained;

        {
            using PooledLease<DisposableItem, DisposablePolicy> lease = pool.RentScoped();
            retained = lease.Value;
        }

        pool.Clear();

        await Assert.That(retained.DisposeCount).IsEqualTo(1);
        await Assert.That(pool.Rent()).IsNotSameReferenceAs(retained);
    }

    [Test]
    public async Task ClearDisposesScopedItemsFromEveryParticipatingThread()
    {
        const int threadCount = 4;
        var pool = new ObjectPool<DisposableItem, DisposablePolicy>(maxCapacity: 1);
        var retained = new DisposableItem[threadCount];
        var threads = new Thread[threadCount];

        for (int i = 0; i < threads.Length; i++)
        {
            int index = i;
            threads[i] = new Thread(() =>
            {
                using PooledLease<DisposableItem, DisposablePolicy> lease = pool.RentScoped();
                retained[index] = lease.Value;
            });
            threads[i].Start();
        }

        foreach (Thread thread in threads)
        {
            thread.Join();
        }

        pool.Clear();

        await Assert.That(retained.All(item => item.DisposeCount == 1)).IsTrue();
        await Assert.That(retained.Distinct()).Count().IsEqualTo(threadCount);
    }

    [Test]
    public async Task LargePoolClearDisposesEveryRetainedItem()
    {
        const int capacity = 65;
        var pool = new ObjectPool<DisposableItem, DisposablePolicy>(capacity);
        var retained = new DisposableItem[capacity];

        for (int i = 0; i < retained.Length; i++)
        {
            retained[i] = pool.Rent();
        }

        foreach (DisposableItem item in retained)
        {
            pool.Return(item);
        }

        pool.Clear();

        await Assert.That(retained.All(item => item.DisposeCount == 1)).IsTrue();
        await Assert.That(retained).DoesNotContain(pool.Rent());
    }

    [Test]
    public async Task DisposeDrainsPoolAndDisposesLateReturns()
    {
        var pool = new ObjectPool<DisposableItem, DisposablePolicy>(maxCapacity: 2);
        DisposableItem retained = pool.Rent();
        DisposableItem outstanding = pool.Rent();
        pool.Return(retained);

        pool.Dispose();
        pool.Return(outstanding);

        await Assert.That(retained.DisposeCount).IsEqualTo(1);
        await Assert.That(outstanding.DisposeCount).IsEqualTo(1);
        await Assert.That(() => pool.Rent()).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task DisposeDrainsScopedThreadLocalItem()
    {
        var pool = new ObjectPool<DisposableItem, DisposablePolicy>(maxCapacity: 1);
        DisposableItem retained;

        {
            using PooledLease<DisposableItem, DisposablePolicy> lease = pool.RentScoped();
            retained = lease.Value;
        }

        pool.Dispose();

        await Assert.That(retained.DisposeCount).IsEqualTo(1);
        await Assert.That(() => pool.RentScoped()).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task ReturnRacingWithDisposeDoesNotLeaveItemRetained()
    {
        var state = new BlockingPolicyState();
        var pool = new ObjectPool<DisposableItem, BlockingPolicy>(
            new BlockingPolicy(state),
            maxCapacity: 1);
        DisposableItem item = pool.Rent();
        Task returnTask = Task.Run(() => pool.Return(item));
        bool resetObserved = state.ResetStarted.Wait(TestTimeout);

        if (resetObserved)
        {
            pool.Dispose();
        }

        state.ContinueReset.Set();
        await returnTask.WaitAsync(TestTimeout);

        await Assert.That(resetObserved).IsTrue();
        await Assert.That(state.ContinueResetObserved).IsTrue();
        await Assert.That(item.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task ScopedReturnRacingWithDisposeDoesNotLeaveItemRetained()
    {
        var state = new BlockingPolicyState();
        var pool = new ObjectPool<DisposableItem, BlockingPolicy>(
            new BlockingPolicy(state),
            maxCapacity: 1);
        Task<DisposableItem> returnTask = Task.Run(() => RentScopedAndDispose(pool));
        bool resetObserved = state.ResetStarted.Wait(TestTimeout);

        if (resetObserved)
        {
            pool.Dispose();
        }

        state.ContinueReset.Set();
        DisposableItem item = await returnTask.WaitAsync(TestTimeout);

        await Assert.That(resetObserved).IsTrue();
        await Assert.That(state.ContinueResetObserved).IsTrue();
        await Assert.That(item.DisposeCount).IsEqualTo(1);
    }

    private static DisposableItem RentScopedAndDispose(
        ObjectPool<DisposableItem, BlockingPolicy> pool)
    {
        using PooledLease<DisposableItem, BlockingPolicy> lease = pool.RentScoped();
        return lease.Value;
    }

    private sealed class ResettableItem : IResettable, IDisposable
    {
        public ResettableItem()
        {
        }

        internal bool CanReset { get; set; } = true;
        internal int DisposeCount { get; private set; }
        internal int ResetCount { get; private set; }

        public bool TryReset()
        {
            ResetCount++;
            return CanReset;
        }

        public void Dispose() => DisposeCount++;
    }

    private sealed class DisposableItem : IDisposable
    {
        internal int DisposeCount { get; private set; }
        internal int DestroyCount { get; private set; }

        public void Dispose() => DisposeCount++;

        internal void Destroy() => DestroyCount++;
    }

    private readonly struct DisposablePolicy : IPooledObjectPolicy<DisposableItem>
    {
        public DisposableItem Create() => new();

        public bool TryReset(DisposableItem obj) => true;
    }

    private readonly struct ThrowingPolicy(Exception exception)
        : IPooledObjectPolicy<DisposableItem>
    {
        public DisposableItem Create() => new();

        public bool TryReset(DisposableItem obj) => throw exception;
    }

    private readonly struct CustomDestructionPolicy
        : IPooledObjectDestroyPolicy<DisposableItem>
    {
        public DisposableItem Create() => new();

        public bool TryReset(DisposableItem obj) => false;

        public void Destroy(DisposableItem obj) => obj.Destroy();
    }

    private struct StatefulDestructionPolicy : IPooledObjectDestroyPolicy<DisposableItem>
    {
        private int _destroyCount;

        public DisposableItem Create() => new();

        public bool TryReset(DisposableItem obj) => _destroyCount > 0;

        public void Destroy(DisposableItem obj) => _destroyCount++;
    }

    private readonly struct ObjectPolicy : IPooledObjectPolicy<object>
    {
        public object Create() => new DisposableItem();

        public bool TryReset(object obj) => true;
    }

    private readonly struct RejectingObjectPolicy : IPooledObjectPolicy<object>
    {
        public object Create() => new DisposableItem();

        public bool TryReset(object obj) => false;
    }

    private readonly struct BlockingPolicy(BlockingPolicyState state)
        : IPooledObjectPolicy<DisposableItem>
    {
        public DisposableItem Create() => new();

        public bool TryReset(DisposableItem obj)
        {
            state.ResetStarted.Set();
            state.ContinueResetObserved = state.ContinueReset.Wait(TestTimeout);
            return state.ContinueResetObserved;
        }
    }

    private sealed class BlockingPolicyState
    {
        internal ManualResetEventSlim ResetStarted { get; } = new();
        internal ManualResetEventSlim ContinueReset { get; } = new();
        internal bool ContinueResetObserved { get; set; }
    }
}
