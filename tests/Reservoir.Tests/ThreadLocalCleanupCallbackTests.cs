using System.Collections.Concurrent;

namespace Reservoir.Tests;

public class ThreadLocalCleanupCallbackTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConcurrentCrossPoolCleanupRunsOutsideLocks(bool scoped)
    {
        using var callbacksEntered = new Barrier(2);
        var nestedClears = new ConcurrentBag<Task>();
        int blockedCallbacks = 0;
        var first = new ObjectPool<Item, Policy>(default, 1, threadLocalFastPath: !scoped);
        var second = new ObjectPool<Item, Policy>(default, 1, threadLocalFastPath: !scoped);
        Item firstItem = Seed(first, scoped);
        Item secondItem = Seed(second, scoped);

        void ClearOtherPool(ObjectPool<Item, Policy> other)
        {
            if (!callbacksEntered.SignalAndWait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("Both destruction callbacks must enter cleanup.");
            }

            // A bounded callback wait releases the original Clear locks on regression,
            // allowing every nested worker to finish instead of leaving deadlocked threads.
            Task nested = StartClear(other);
            nestedClears.Add(nested);
            if (!nested.Wait(TimeSpan.FromSeconds(5)))
            {
                Interlocked.Increment(ref blockedCallbacks);
            }
        }

        firstItem.OnDestroy = () => ClearOtherPool(second);
        secondItem.OnDestroy = () => ClearOtherPool(first);
        Task[] clears = [StartClear(first), StartClear(second)];
        try
        {
            await Task.WhenAll(clears).WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            await Task.WhenAll(nestedClears).WaitAsync(TimeSpan.FromSeconds(30));
            first.Dispose();
            second.Dispose();
        }

        await Assert.That(blockedCallbacks).IsEqualTo(0);
        await Assert.That(firstItem.DestroyCount).IsEqualTo(1);
        await Assert.That(secondItem.DestroyCount).IsEqualTo(1);
    }

    [Test]
    public async Task ConcurrentScopedRentalsAndClearsPreserveExclusiveOwnership()
    {
        using var pool = new ObjectPool<Item, Policy>(default, 4);
        var items = new ConcurrentBag<Item>();
        await ConcurrentTestWorkers.RunAsync(
            Enumerable.Range(0, 4).Select<int, Action<CancellationToken>>(_ => token =>
            {
                for (int i = 0; i < 50_000; i++)
                {
                    token.ThrowIfCancellationRequested();
                    using var lease = pool.RentScoped();
                    Item item = lease.Value;
                    if (Volatile.Read(ref item.DestroyCount) != 0)
                    {
                        throw new InvalidOperationException("Rented a destroyed item.");
                    }

                    if (item.OnDestroy is null)
                    {
                        items.Add(item);
                        item.OnDestroy = () =>
                        {
                            if (Volatile.Read(ref item.DestroyCount) != 1)
                            {
                                throw new InvalidOperationException("Destroyed an item more than once.");
                            }
                        };
                    }
                }
            }),
            token =>
            {
                while (!token.IsCancellationRequested)
                {
                    pool.Clear();
                }
            });
        pool.Clear();

        await Assert.That(items.All(item => item.DestroyCount == 1)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CleanupCanRentReturnAndClearSamePool(bool scoped)
    {
        using var pool = new ObjectPool<Item, Policy>(default, 1, threadLocalFastPath: !scoped);
        Item first = Seed(pool, scoped);
        Item? reentrant = null;
        first.OnDestroy = () =>
        {
            reentrant = Seed(pool, scoped);
            pool.Clear();
        };

        pool.Clear();

        await Assert.That(first.DestroyCount).IsEqualTo(1);
        await Assert.That(reentrant).IsNotNull();
        await Assert.That(reentrant!.DestroyCount).IsEqualTo(1);
        Item next = pool.Rent();
        await Assert.That(next.DestroyCount).IsEqualTo(0);
        pool.Return(next);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task CleanupFailureStillDrainsEverySlotAndSharedStorage(bool scoped, bool dispose)
    {
        var pool = new ObjectPool<Item, Policy>(default, 1, threadLocalFastPath: !scoped);
        var items = new ConcurrentBag<Item>();
        var failure = new InvalidOperationException("Cleanup failed.");
        await ConcurrentTestWorkers.RunAsync(Enumerable.Range(0, 3).Select<int, Action<CancellationToken>>(_ => _ =>
        {
            Item item = Seed(pool, scoped);
            item.OnDestroy = () => throw failure;
            items.Add(item);
        }));
        Item shared = pool.Rent();
        items.Add(Seed(pool, scoped));
        pool.Return(shared);
        items.Add(shared);

        Exception? observed = null;
        try
        {
            if (dispose)
            {
                pool.Dispose();
            }
            else
            {
                pool.Clear();
            }
        }
        catch (Exception exception)
        {
            observed = exception;
        }
        finally
        {
            pool.Dispose();
        }

        await Assert.That(observed).IsSameReferenceAs(failure);
        await Assert.That(items.All(item => item.DestroyCount == 1)).IsTrue();
    }

    private static Task StartClear(ObjectPool<Item, Policy> pool) => Task.Factory.StartNew(
        pool.Clear, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static Item Seed(ObjectPool<Item, Policy> pool, bool scoped)
    {
        if (scoped)
        {
            using var lease = pool.RentScoped();
            return lease.Value;
        }

        Item item = pool.Rent();
        pool.Return(item);
        return item;
    }

    public sealed class Item
    {
        public Action? OnDestroy;
        public int DestroyCount;
    }

    public readonly struct Policy : IPooledObjectDestroyPolicy<Item>
    {
        public Item Create() => new();
        public bool TryReset(Item item) => true;
        public void Destroy(Item item)
        {
            Interlocked.Increment(ref item.DestroyCount);
            item.OnDestroy?.Invoke();
        }
    }
}
