namespace Reservoir.Tests;

public class PermanentObjectPoolTests
{
    [Test]
    public async Task ReturnThenRentYieldsSameInstance()
    {
        var pool = new PermanentObjectPool<PooledItem, CountingPolicy>(maxCapacity: 4);
        PooledItem expected = pool.Rent();

        pool.Return(expected);
        PooledItem actual = pool.Rent();

        await Assert.That(actual).IsSameReferenceAs(expected);
        await Assert.That(actual.ResetCount).IsEqualTo(1);
    }

    [Test]
    [Arguments(1)]
    [Arguments(64)]
    [Arguments(65)]
    [Arguments(256)]
    public async Task RetainsConfiguredCapacityAcrossStorageBackends(int capacity)
    {
        var state = new PolicyState();
        var pool = new PermanentObjectPool<PooledItem, CountingPolicy>(
            new CountingPolicy(state),
            capacity);
        var items = new PooledItem[capacity + 1];

        for (int i = 0; i < items.Length; i++)
        {
            items[i] = pool.Rent();
        }

        foreach (PooledItem item in items)
        {
            pool.Return(item);
        }

        var retained = new HashSet<PooledItem>();
        for (int i = 0; i < capacity; i++)
        {
            retained.Add(pool.Rent());
        }

        await Assert.That(retained.Count).IsEqualTo(capacity);
        await Assert.That(retained).DoesNotContain(items[^1]);
        await Assert.That(state.Created).IsEqualTo(capacity + 1);
        await Assert.That(state.Destroyed).IsEqualTo(1);
    }

    [Test]
    public async Task ResetRejectionDestroysInstance()
    {
        var state = new PolicyState { RejectReturns = true };
        var pool = new PermanentObjectPool<PooledItem, CountingPolicy>(
            new CountingPolicy(state),
            maxCapacity: 1);
        PooledItem rejected = pool.Rent();

        pool.Return(rejected);
        PooledItem replacement = pool.Rent();

        await Assert.That(replacement).IsNotSameReferenceAs(rejected);
        await Assert.That(state.Reset).IsEqualTo(1);
        await Assert.That(state.Destroyed).IsEqualTo(1);
    }

    [Test]
    public async Task ConcurrentRentAndReturnPreservesExclusiveOwnership()
    {
        const int workerCount = 16;
        const int iterations = 20_000;
        var state = new PolicyState();
        var pool = new PermanentObjectPool<PooledItem, CountingPolicy>(
            new CountingPolicy(state),
            maxCapacity: workerCount);
        var failures = new System.Collections.Concurrent.ConcurrentQueue<int>();

        await Task.WhenAll(Enumerable.Range(0, workerCount).Select(_ => Task.Run(() =>
        {
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                PooledItem item = pool.Rent();
                if (Interlocked.Exchange(ref item.InUse, 1) != 0)
                {
                    failures.Enqueue(item.Id);
                }

                Thread.SpinWait(4);
                Volatile.Write(ref item.InUse, 0);
                pool.Return(item);
            }
        })));

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task WarmRentAndReturnAllocatesNothing()
    {
        const int iterations = 10_000;
        var pool = new PermanentObjectPool<PooledItem, CountingPolicy>(maxCapacity: 32);
        pool.Return(pool.Rent());

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < iterations; i++)
        {
            PooledItem item = pool.Rent();
            pool.Return(item);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0);
    }

    [Test]
    public async Task NonPositiveCapacityThrows()
    {
        await Assert.That(() => new PermanentObjectPool<PooledItem, CountingPolicy>(0))
            .Throws<ArgumentOutOfRangeException>();
    }

    private sealed class PolicyState
    {
        internal int Created;
        internal int Reset;
        internal int Destroyed;
        internal bool RejectReturns;
    }

    private sealed class PooledItem(int id)
    {
        internal int Id { get; } = id;
        internal int InUse;
        internal int ResetCount;
    }

    private readonly struct CountingPolicy : IPooledObjectDestroyPolicy<PooledItem>
    {
        private readonly PolicyState? _state;

        internal CountingPolicy(PolicyState state) => _state = state;

        public PooledItem Create()
        {
            int id = _state is null ? 0 : Interlocked.Increment(ref _state.Created);
            return new PooledItem(id);
        }

        public bool TryReset(PooledItem obj)
        {
            obj.ResetCount++;
            if (_state is not null)
            {
                Interlocked.Increment(ref _state.Reset);
            }

            return _state?.RejectReturns != true;
        }

        public void Destroy(PooledItem obj)
        {
            if (_state is not null)
            {
                Interlocked.Increment(ref _state.Destroyed);
            }
        }
    }
}
