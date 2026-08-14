using System.Collections.Concurrent;

namespace Reservoir.Tests;

public class GenericThreadLocalObjectPoolTests
{
    [Test]
    public async Task SameThreadReturnResetsAndReusesInstance()
    {
        var state = new PolicyState();
        var pool = CreatePool(state);
        PooledItem expected = pool.RentThreadLocal();

        pool.ReturnThreadLocal(expected);
        PooledItem actual = pool.RentThreadLocal();

        await Assert.That(actual).IsSameReferenceAs(expected);
        await Assert.That(expected.ResetCount).IsEqualTo(1);
    }

    [Test]
    public async Task OccupiedThreadSlotFallsBackToSharedStorage()
    {
        var pool = CreatePool(new PolicyState(), maxCapacity: 1);
        PooledItem first = pool.RentThreadLocal();
        PooledItem second = pool.RentThreadLocal();

        pool.ReturnThreadLocal(first);
        pool.ReturnThreadLocal(second);

        await Assert.That(pool.RentThreadLocal()).IsSameReferenceAs(first);
        await Assert.That(pool.RentThreadLocal()).IsSameReferenceAs(second);
    }

    [Test]
    public async Task PoolInstancesKeepThreadLocalItemsIsolated()
    {
        var firstPool = CreatePool(new PolicyState());
        var secondPool = CreatePool(new PolicyState());
        PooledItem first = firstPool.RentThreadLocal();
        PooledItem second = secondPool.RentThreadLocal();

        firstPool.ReturnThreadLocal(first);
        secondPool.ReturnThreadLocal(second);

        await Assert.That(firstPool.RentThreadLocal()).IsSameReferenceAs(first);
        await Assert.That(secondPool.RentThreadLocal()).IsSameReferenceAs(second);
    }

    [Test]
    public async Task CrossThreadReturnIsImmediatelyReusableOnReturningThread()
    {
        var pool = CreatePool(new PolicyState());
        PooledItem expected = pool.RentThreadLocal();
        PooledItem? actual = null;

        var thread = new Thread(() =>
        {
            pool.ReturnThreadLocal(expected);
            actual = pool.RentThreadLocal();
        });

        thread.Start();
        thread.Join();

        await Assert.That(actual).IsSameReferenceAs(expected);
    }

    [Test]
    public async Task RentalCanCrossAwaitAndFollowsReturningThread()
    {
        var pool = CreatePool(new PolicyState());
        PooledItem expected = pool.RentThreadLocal();

        await Task.Yield();

        pool.ReturnThreadLocal(expected);
        PooledItem actual = pool.RentThreadLocal();

        await Assert.That(actual).IsSameReferenceAs(expected);
    }

    [Test]
    public async Task ResetRejectionDestroysItem()
    {
        var state = new PolicyState { RejectReturns = true };
        var pool = CreatePool(state);
        PooledItem rejected = pool.RentThreadLocal();

        pool.ReturnThreadLocal(rejected);

        await Assert.That(state.ResetCount).IsEqualTo(1);
        await Assert.That(state.DestroyCount).IsEqualTo(1);
        await Assert.That(pool.RentThreadLocal()).IsNotSameReferenceAs(rejected);
    }

    [Test]
    public async Task ResetExceptionDestroysItemAndPropagates()
    {
        var expected = new InvalidOperationException("Reset failed.");
        var state = new PolicyState { ResetException = expected };
        var pool = CreatePool(state);
        PooledItem item = pool.RentThreadLocal();
        Exception? actual = null;

        try
        {
            pool.ReturnThreadLocal(item);
        }
        catch (Exception exception)
        {
            actual = exception;
        }

        await Assert.That(actual).IsSameReferenceAs(expected);
        await Assert.That(state.DestroyCount).IsEqualTo(1);
    }

    [Test]
    public async Task FullThreadLocalAndSharedTiersDestroyNewestReturn()
    {
        var state = new PolicyState();
        var pool = CreatePool(state, maxCapacity: 1);
        PooledItem threadLocal = pool.RentThreadLocal();
        PooledItem shared = pool.RentThreadLocal();
        PooledItem rejected = pool.RentThreadLocal();

        pool.ReturnThreadLocal(threadLocal);
        pool.ReturnThreadLocal(shared);
        pool.ReturnThreadLocal(rejected);

        await Assert.That(state.ResetCount).IsEqualTo(3);
        await Assert.That(state.DestroyCount).IsEqualTo(1);
        await Assert.That(rejected.Destroyed).IsTrue();
    }

    [Test]
    public async Task ClearDrainsSlotsFromParticipatingThreads()
    {
        const int threadCount = 8;
        var state = new PolicyState();
        var pool = CreatePool(state);
        using var start = new Barrier(threadCount + 1);
        var threads = new Thread[threadCount];

        for (int i = 0; i < threads.Length; i++)
        {
            threads[i] = new Thread(() =>
            {
                PooledItem item = pool.RentThreadLocal();
                start.SignalAndWait();
                pool.ReturnThreadLocal(item);
            });
            threads[i].Start();
        }

        start.SignalAndWait();
        foreach (Thread thread in threads)
        {
            thread.Join();
        }

        pool.Clear();

        await Assert.That(state.DestroyCount).IsEqualTo(threadCount);
    }

    [Test]
    public async Task AsyncRetentionFollowsReturningThreadsNotTaskCount()
    {
        const int taskCount = 1_024;
        const int capacity = 32;
        var state = new PolicyState();
        var pool = CreatePool(state, capacity);
        var returningThreads = new ConcurrentDictionary<int, byte>();

        await Task.WhenAll(Enumerable.Range(0, taskCount).Select(async _ =>
        {
            PooledItem item = pool.RentThreadLocal();
            await Task.Yield();
            returningThreads.TryAdd(Environment.CurrentManagedThreadId, 0);
            pool.ReturnThreadLocal(item);
        }));

        int destroyedBeforeClear = state.DestroyCount;
        pool.Clear();
        int retainedAfterQuiescence = state.DestroyCount - destroyedBeforeClear;

        await Assert.That(retainedAfterQuiescence)
            .IsLessThanOrEqualTo(returningThreads.Count + capacity);
        await Assert.That(retainedAfterQuiescence).IsLessThan(taskCount);
    }

    [Test]
    public async Task ReturnAfterDisposeDestroysItem()
    {
        var state = new PolicyState();
        var pool = CreatePool(state);
        PooledItem outstanding = pool.RentThreadLocal();

        pool.Dispose();
        pool.ReturnThreadLocal(outstanding);

        await Assert.That(state.DestroyCount).IsEqualTo(1);
        await Assert.That(() => pool.RentThreadLocal()).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task ConcurrentRentAndReturnPreservesExclusiveOwnership()
    {
        const int workerCount = 16;
        const int iterations = 20_000;
        var pool = CreatePool(new PolicyState(), maxCapacity: workerCount);
        var failures = new ConcurrentQueue<int>();

        await Task.WhenAll(Enumerable.Range(0, workerCount).Select(_ => Task.Run(() =>
        {
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                PooledItem item = pool.RentThreadLocal();
                if (Interlocked.Exchange(ref item.InUse, 1) != 0)
                {
                    failures.Enqueue(item.Id);
                }

                Thread.SpinWait(4);
                Volatile.Write(ref item.InUse, 0);
                pool.ReturnThreadLocal(item);
            }
        })));

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task WarmRentAndReturnAllocatesNothing()
    {
        const int iterations = 10_000;
        var pool = CreatePool(new PolicyState());
        pool.ReturnThreadLocal(pool.RentThreadLocal());

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < iterations; i++)
        {
            PooledItem item = pool.RentThreadLocal();
            pool.ReturnThreadLocal(item);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0);
    }

    [Test]
    public async Task RuntimePolicyPoolExposesThreadLocalManualRentals()
    {
        var policy = new RuntimePolicy();
        var pool = new ObjectPool<PooledItem>(policy, maxCapacity: 1);
        PooledItem expected = pool.RentThreadLocal();

        pool.ReturnThreadLocal(expected);

        await Assert.That(pool.RentThreadLocal()).IsSameReferenceAs(expected);
        await Assert.That(expected.ResetCount).IsEqualTo(1);
    }

    [Test]
    public async Task NullReturnThrows()
    {
        var pool = CreatePool(new PolicyState());

        await Assert.That(() => pool.ReturnThreadLocal(null!))
            .Throws<ArgumentNullException>();
    }

    private static ObjectPool<PooledItem, CountingPolicy> CreatePool(
        PolicyState state,
        int maxCapacity = 32)
        => new(new CountingPolicy(state), maxCapacity);

    private sealed class PolicyState
    {
        internal int CreatedCount;
        internal int ResetCount;
        internal int DestroyCount;
        internal bool RejectReturns;
        internal Exception? ResetException;
    }

    private sealed class PooledItem(int id)
    {
        internal int Id { get; } = id;
        internal int InUse;
        internal int ResetCount;
        internal bool Destroyed;
    }

    private readonly struct CountingPolicy(PolicyState state)
        : IPooledObjectDestroyPolicy<PooledItem>
    {
        public PooledItem Create()
            => new(Interlocked.Increment(ref state.CreatedCount));

        public bool TryReset(PooledItem obj)
        {
            obj.ResetCount++;
            Interlocked.Increment(ref state.ResetCount);
            if (state.ResetException is not null)
            {
                throw state.ResetException;
            }

            return !state.RejectReturns;
        }

        public void Destroy(PooledItem obj)
        {
            obj.Destroyed = true;
            Interlocked.Increment(ref state.DestroyCount);
        }
    }

    private sealed class RuntimePolicy : IPooledObjectPolicy<PooledItem>
    {
        public PooledItem Create() => new(0);

        public bool TryReset(PooledItem obj)
        {
            obj.ResetCount++;
            return true;
        }
    }
}
