namespace Reservoir.Tests;

public class ObjectLifecycleTests
{
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
        var resetStarted = new ManualResetEventSlim();
        var continueReset = new ManualResetEventSlim();
        var pool = new ObjectPool<DisposableItem, BlockingPolicy>(
            new BlockingPolicy(resetStarted, continueReset),
            maxCapacity: 1);
        DisposableItem item = pool.Rent();
        Task returnTask = Task.Run(() => pool.Return(item));
        resetStarted.Wait();

        pool.Dispose();
        continueReset.Set();
        await returnTask;

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

    private readonly struct BlockingPolicy(
        ManualResetEventSlim resetStarted,
        ManualResetEventSlim continueReset) : IPooledObjectPolicy<DisposableItem>
    {
        public DisposableItem Create() => new();

        public bool TryReset(DisposableItem obj)
        {
            resetStarted.Set();
            continueReset.Wait();
            return true;
        }
    }
}
