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
    public async Task FullPoolDisposesReturnedExcessItem()
    {
        var pool = new ObjectPool<DisposableItem, DisposablePolicy>(maxCapacity: 1);
        DisposableItem retained = pool.Rent();
        DisposableItem excess = pool.Rent();

        pool.Return(retained);
        pool.Return(excess);

        await Assert.That(retained.DisposeCount).IsEqualTo(0);
        await Assert.That(excess.DisposeCount).IsEqualTo(1);
        await Assert.That(pool.Rent()).IsSameReferenceAs(retained);
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

        public void Dispose() => DisposeCount++;
    }

    private readonly struct DisposablePolicy : IPooledObjectPolicy<DisposableItem>
    {
        public DisposableItem Create() => new();

        public bool TryReset(DisposableItem obj) => true;
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
