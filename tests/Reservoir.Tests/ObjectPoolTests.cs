using System.Collections.Concurrent;

namespace Reservoir.Tests;

public class ObjectPoolTests
{
    [Test]
    public async Task RentCreatesWhenPoolIsEmpty()
    {
        var policy = new CountingPolicy();
        var pool = new ObjectPool<PooledItem, CountingPolicy>(policy, maxCapacity: 4);

        PooledItem item = pool.Rent();

        await Assert.That(item.Id).IsEqualTo(1);
    }

    [Test]
    public async Task ReturnThenRentYieldsSameInstance()
    {
        var pool = new ObjectPool<PooledItem, CountingPolicy>(maxCapacity: 4);
        PooledItem expected = pool.Rent();

        pool.Return(expected);
        PooledItem actual = pool.Rent();

        await Assert.That(actual).IsSameReferenceAs(expected);
        await Assert.That(actual.ResetCount).IsEqualTo(1);
    }

    [Test]
    public async Task PoolRetainsNoMoreThanConfiguredCapacity()
    {
        var policy = new CountingPolicy();
        var pool = new ObjectPool<PooledItem, CountingPolicy>(policy, maxCapacity: 2);
        PooledItem first = pool.Rent();
        PooledItem second = pool.Rent();
        PooledItem excess = pool.Rent();

        pool.Return(first);
        pool.Return(second);
        pool.Return(excess);

        var rented = new[] { pool.Rent(), pool.Rent(), pool.Rent() };

        await Assert.That(rented).Contains(first);
        await Assert.That(rented).Contains(second);
        await Assert.That(rented).DoesNotContain(excess);
        await Assert.That(policy.Created).IsEqualTo(4);
    }

    [Test]
    public async Task ResetFalseDiscardsInstance()
    {
        var policy = new CountingPolicy { DiscardReturned = true };
        var pool = new ObjectPool<PooledItem, CountingPolicy>(policy, maxCapacity: 1);
        PooledItem discarded = pool.Rent();

        pool.Return(discarded);
        PooledItem replacement = pool.Rent();

        await Assert.That(replacement).IsNotSameReferenceAs(discarded);
        await Assert.That(discarded.ResetCount).IsEqualTo(1);
    }

    [Test]
    public async Task FactoryConveniencePoolReusesInstances()
    {
        int created = 0;
        var pool = new ObjectPool<PooledItem>(() => new PooledItem(++created), maxCapacity: 1);
        PooledItem expected = pool.Rent();

        pool.Return(expected);

        await Assert.That(pool.Rent()).IsSameReferenceAs(expected);
        await Assert.That(created).IsEqualTo(1);
    }

    [Test]
    public async Task PolicyConveniencePoolUsesPolicyReset()
    {
        var policy = new ReferencePolicy();
        var pool = new ObjectPool<PooledItem>(policy, maxCapacity: 1);
        PooledItem item = pool.Rent();

        pool.Return(item);

        await Assert.That(item.ResetCount).IsEqualTo(1);
    }

    [Test]
    public async Task DefaultCapacityFollowsProcessorSizingRule()
    {
        int expected = Math.Max(32, 2 * Environment.ProcessorCount);

        await Assert.That(ObjectPool<PooledItem, CountingPolicy>.DefaultMaximumRetained)
            .IsEqualTo(expected);
        await Assert.That(new ObjectPool<PooledItem, CountingPolicy>().MaximumRetained)
            .IsEqualTo(expected);
    }

    [Test]
    public async Task NonPositiveCapacityThrows()
    {
        await Assert.That(() => new ObjectPool<PooledItem, CountingPolicy>(0))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task ConcurrentRentAndReturnNeverHandsOutOneInstanceTwice()
    {
        const int workerCount = 16;
        const int iterations = 20_000;
        var pool = new ObjectPool<PooledItem, CountingPolicy>(maxCapacity: workerCount);
        var failures = new ConcurrentQueue<string>();
        PooledItem warmItem = pool.Rent();
        pool.Return(warmItem);

        await Task.WhenAll(Enumerable.Range(0, workerCount).Select(_ => Task.Run(() =>
        {
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                PooledItem item = pool.Rent();
                if (Interlocked.Exchange(ref item.InUse, 1) != 0)
                {
                    failures.Enqueue($"Item {item.Id} was rented concurrently.");
                }

                Thread.SpinWait(4);
                Volatile.Write(ref item.InUse, 0);
                pool.Return(item);
            }
        })));

        await Assert.That(failures).IsEmpty();
    }

    private sealed class PooledItem(int id)
    {
        internal int Id { get; } = id;
        internal int InUse;
        internal int ResetCount;
    }

    private struct CountingPolicy : IPooledObjectPolicy<PooledItem>
    {
        private Counter? _counter = new();

        public CountingPolicy()
        {
        }

        internal bool DiscardReturned { get; init; }
        internal int Created => _counter?.Value ?? 0;

        public PooledItem Create()
        {
            Counter counter = _counter ??= new Counter();
            return new PooledItem(Interlocked.Increment(ref counter.Value));
        }

        public bool TryReset(PooledItem obj)
        {
            Interlocked.Increment(ref obj.ResetCount);
            return !DiscardReturned;
        }

        private sealed class Counter
        {
            internal int Value;
        }
    }

    private sealed class ReferencePolicy : IPooledObjectPolicy<PooledItem>
    {
        private int _created;

        public PooledItem Create() => new(Interlocked.Increment(ref _created));

        public bool TryReset(PooledItem obj)
        {
            Interlocked.Increment(ref obj.ResetCount);
            return true;
        }
    }
}
