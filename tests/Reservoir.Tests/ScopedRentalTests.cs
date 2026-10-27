using System.Collections.Concurrent;

namespace Reservoir.Tests;

public class ScopedRentalTests
{
    [Test]
    public async Task ClearPreservesIndependentLeaseOwners()
    {
        using var pool = new ObjectPool<Item, Policy>(maxCapacity: 2);
        var first = pool.RentScoped(out Item firstItem);
        var second = pool.RentScoped(out Item secondItem);
        var staleFirst = first;
        var staleSecond = second;
        var nested = pool.RentScoped(out Item idleItem);
        nested.Dispose();

        pool.Clear();
        bool activeOwnersSurvivedClear = ReferenceEquals(first.Value, firstItem)
            && ReferenceEquals(second.Value, secondItem)
            && firstItem.DestroyCount == 0 && secondItem.DestroyCount == 0;
        first.Dispose();
        second.Dispose();

        var replacementFirst = pool.RentScoped();
        var replacementSecond = pool.RentScoped();
        staleFirst.Dispose();
        staleSecond.Dispose();
        Item replacementFirstValue = replacementFirst.Value;
        Item replacementSecondValue = replacementSecond.Value;
        replacementSecond.Dispose();
        replacementFirst.Dispose();
        pool.Dispose();

        await Assert.That(activeOwnersSurvivedClear).IsTrue();
        await Assert.That(firstItem).IsNotSameReferenceAs(secondItem);
        await Assert.That(replacementFirstValue).IsNotSameReferenceAs(replacementSecondValue);
        await Assert.That(new[] { firstItem, secondItem }.Contains(replacementFirstValue)).IsTrue();
        await Assert.That(new[] { firstItem, secondItem }.Contains(replacementSecondValue)).IsTrue();
        await Assert.That(idleItem.DestroyCount).IsEqualTo(1);
        await Assert.That(firstItem.DestroyCount).IsEqualTo(1);
        await Assert.That(secondItem.DestroyCount).IsEqualTo(1);
    }

    [Test]
    public async Task StaleCopiesCannotReturnLaterRentals()
    {
        using var pool = new ObjectPool<Item, Policy>(new Policy(), maxCapacity: 1);
        var first = pool.RentScoped(out Item item);
        var stale = first;
        first.Dispose();
        var second = pool.RentScoped();
        stale.Dispose();
        Item secondValue = second.Value;
        int resetBeforeSecondReturn = item.ResetCount;
        bool staleValueThrows = false;
        try
        {
            _ = stale.Value;
        }
        catch (ObjectDisposedException)
        {
            staleValueThrows = true;
        }
        second.Dispose();
        PooledLease<Item, Policy> empty = default;
        empty.Dispose();
        await Assert.That(secondValue).IsSameReferenceAs(item);
        await Assert.That(resetBeforeSecondReturn).IsEqualTo(1);
        await Assert.That(staleValueThrows).IsTrue();
        await Assert.That(item.ResetCount).IsEqualTo(2);
    }

    [Test]
    public async Task RuntimePolicyLeasesPreserveOwnershipAndSharedReuse()
    {
        using var pool = new ObjectPool<Item>(new Policy(), maxCapacity: 1);
        var first = pool.RentScoped(out Item item);
        var stale = first;
        first.Dispose();
        var second = pool.RentScoped();
        stale.Dispose();
        Item secondValue = second.Value;
        int resetBeforeSecondReturn = item.ResetCount;
        second.Dispose();
        pool.Clear();
        PooledLease<Item> empty = default;
        empty.Dispose();
        await Assert.That(secondValue).IsSameReferenceAs(item);
        await Assert.That(resetBeforeSecondReturn).IsEqualTo(1);
        await Assert.That(item.DestroyCount).IsEqualTo(1);
    }

    [Test]
    [Arguments(1)]
    [Arguments(32)]
    [Arguments(65)]
    [Arguments(256)]
    public async Task NestedRentalsAcrossThreadsKeepIdleRetentionBounded(int capacity)
    {
        const int workers = 8;
        const int depth = 40;
        var created = new ConcurrentBag<Item>();
        using var pool = new ObjectPool<Item, Policy>(new Policy(created), capacity);
        using var barrier = new Barrier(workers);
        await ConcurrentTestWorkers.RunAsync(Enumerable.Range(0, workers).Select(_ =>
            (Action<CancellationToken>)(token => RentNested(pool, depth, barrier, token))));
        await Assert.That(created.Count).IsEqualTo(workers * depth);
        int retained = created.Count(item => item.DestroyCount == 0);
        await Assert.That(retained <= capacity).IsTrue();
        await Assert.That(retained > 0).IsTrue();
        await Assert.That(created.All(item => item.ResetCount == 1 && item.DestroyCount <= 1)).IsTrue();
        pool.Clear();
        await Assert.That(created.All(item => item.DestroyCount == 1)).IsTrue();
    }

    private static void RentNested(ObjectPool<Item, Policy> pool, int depth, Barrier barrier, CancellationToken token)
    {
        using var lease = pool.RentScoped();
        if (depth == 1)
        {
            barrier.SignalAndWait(token);
        }
        else
        {
            RentNested(pool, depth - 1, barrier, token);
        }
    }

    [Test]
    [Arguments("reject")]
    [Arguments("reset-throw")]
    [Arguments("both-throw")]
    public async Task ResetFailuresDestroyOnceAndNeverRetain(string failure)
    {
        using var pool = new ObjectPool<Item, Policy>(maxCapacity: 1);
        var lease = pool.RentScoped(out Item item);
        var copy = lease;
        item.Failure = failure;
        Exception? caught = null;
        try
        {
            lease.Dispose();
        }
        catch (Exception exception)
        {
            caught = exception;
        }
        copy.Dispose();
        await Assert.That(item.ResetCount).IsEqualTo(1);
        await Assert.That(item.DestroyCount).IsEqualTo(1);
        var next = pool.RentScoped();
        Item nextValue = next.Value;
        next.Dispose();
        await Assert.That(nextValue).IsNotSameReferenceAs(item);
        if (failure == "reject")
        {
            await Assert.That(caught).IsNull();
        }
        else if (failure == "reset-throw")
        {
            await Assert.That(caught).IsTypeOf<InvalidOperationException>();
        }
        else
        {
            await Assert.That(caught).IsTypeOf<AggregateException>();
        }
    }

    [Test]
    public async Task ClearPreservesOutstandingOwnershipAndDisposeDestroysLateReturn()
    {
        var pool = new ObjectPool<Item, Policy>(maxCapacity: 1);
        var lease = pool.RentScoped(out Item item);
        pool.Clear();
        int destroyedAfterClear = item.DestroyCount;
        pool.Dispose();
        int destroyedAfterDispose = item.DestroyCount;
        lease.Dispose();
        await Assert.That(destroyedAfterClear).IsEqualTo(0);
        await Assert.That(destroyedAfterDispose).IsEqualTo(0);
        await Assert.That(item.DestroyCount).IsEqualTo(1);
        await Assert.That(item.ResetCount).IsEqualTo(0);
        await Assert.That(() => { pool.RentScoped(); }).Throws<ObjectDisposedException>();
    }

    [Test]
    [Arguments(1)]
    [Arguments(65)]
    public async Task ConcurrentClearAndDisposeNeverExposeDestroyedItems(int capacity)
    {
        var created = new ConcurrentBag<Item>();
        using var pool = new ObjectPool<Item, Policy>(new Policy(created), capacity);
        using var start = new Barrier(5);
        await ConcurrentTestWorkers.RunAsync(Enumerable.Range(0, 4).Select(_ =>
            (Action<CancellationToken>)(token =>
            {
                for (int i = 0; i < 10000; i++)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        using var lease = pool.RentScoped(out Item item);
                        if (Volatile.Read(ref item.DestroyCount) != 0 || Interlocked.Exchange(ref item.Owner, 1) != 0)
                        {
                            throw new InvalidOperationException("A destroyed or concurrently owned item was rented.");
                        }
                        if (i == 0)
                        {
                            start.SignalAndWait(token);
                        }
                        Thread.SpinWait(10);
                        if (Volatile.Read(ref item.DestroyCount) != 0)
                        {
                            throw new InvalidOperationException("An outstanding item was destroyed.");
                        }
                        Volatile.Write(ref item.Owner, 0);
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                }
            })), background: token =>
            {
                start.SignalAndWait(token);
                for (int i = 0; i < 100 && !token.IsCancellationRequested; i++)
                {
                    pool.Clear();
                    Thread.Yield();
                }
                pool.Dispose();
            });
        pool.Dispose();
        await Assert.That(created.Count >= 4).IsTrue();
        await Assert.That(created.All(item => item.DestroyCount == 1 && item.Owner == 0)).IsTrue();
    }

    private sealed class Item
    {
        internal int ResetCount;
        internal int DestroyCount;
        internal int Owner;
        internal string? Failure;
    }

    private readonly struct Policy(ConcurrentBag<Item>? created = null) : IPooledObjectDestroyPolicy<Item>
    {
        public Item Create()
        {
            var item = new Item();
            created?.Add(item);
            return item;
        }
        public bool TryReset(Item item)
        {
            item.ResetCount++;
            if (item.Failure is "reset-throw" or "both-throw")
            {
                throw new InvalidOperationException("reset");
            }
            return item.Failure != "reject";
        }
        public void Destroy(Item item)
        {
            Interlocked.Increment(ref item.DestroyCount);
            if (item.Failure == "both-throw")
            {
                throw new ApplicationException("destroy");
            }
        }
    }
}
