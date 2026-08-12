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
    [Arguments(64)]
    [Arguments(65)]
    [Arguments(257)]
    public async Task PoolRetainsConfiguredCapacityAcrossStorageBackends(int capacity)
    {
        var policy = new CountingPolicy();
        var pool = new ObjectPool<PooledItem, CountingPolicy>(policy, capacity);
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
        await Assert.That(policy.Created).IsEqualTo(capacity + 1);
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

    [Test]
    public async Task RentReturnStressPreservesOwnershipAndStateAcrossWorkerCounts()
    {
        const int capacity = 32;
#if NET8_0
        const int iterations = 25_000;
#else
        const int iterations = 100_000;
#endif
        int[] workerCounts = [1, 4, 8, 16, 32];
        var state = new StressState();
        var pool = new ObjectPool<StressItem, StressPolicy>(
            new StressPolicy(state),
            maxCapacity: capacity);
        var initialItems = new StressItem[capacity];

        for (int i = 0; i < initialItems.Length; i++)
        {
            initialItems[i] = pool.Rent();
        }

        foreach (StressItem item in initialItems)
        {
            pool.Return(item);
        }

        foreach (int workerCount in workerCounts)
        {
            using var start = new Barrier(workerCount + 1);
            Task[] workers = Enumerable.Range(0, workerCount)
                .Select(workerIndex => Task.Factory.StartNew(
                    () => StressPool(pool, state, start, workerIndex, iterations),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default))
                .ToArray();

            start.SignalAndWait();
            await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(30));
        }

        var retainedItems = new HashSet<StressItem>();
        for (int i = 0; i < capacity; i++)
        {
            retainedItems.Add(pool.Rent());
        }

        int expectedResetCount = capacity + (workerCounts.Sum() * iterations);

        await Assert.That(state.Failures).IsEmpty();
        await Assert.That(state.CreatedCount).IsEqualTo(capacity);
        await Assert.That(state.ResetCount).IsEqualTo(expectedResetCount);
        await Assert.That(retainedItems.Count).IsEqualTo(capacity);
    }

    [Test]
    public async Task LargePoolPreservesOwnershipUnderContention()
    {
        const int capacity = 256;
        const int workerCount = 16;
        const int iterations = 20_000;
        var state = new StressState();
        var pool = new ObjectPool<StressItem, StressPolicy>(
            new StressPolicy(state),
            capacity);
        var initialItems = new StressItem[capacity];

        for (int i = 0; i < initialItems.Length; i++)
        {
            initialItems[i] = pool.Rent();
        }

        foreach (StressItem item in initialItems)
        {
            pool.Return(item);
        }

        using var start = new Barrier(workerCount + 1);
        Task[] workers = Enumerable.Range(0, workerCount)
            .Select(workerIndex => Task.Factory.StartNew(
                () => StressPool(pool, state, start, workerIndex, iterations),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default))
            .ToArray();

        start.SignalAndWait();
        await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(30));

        var retainedItems = new HashSet<StressItem>();
        for (int i = 0; i < capacity; i++)
        {
            retainedItems.Add(pool.Rent());
        }

        await Assert.That(state.Failures).IsEmpty();
        await Assert.That(state.CreatedCount).IsEqualTo(capacity);
        await Assert.That(retainedItems.Count).IsEqualTo(capacity);
    }

    [Test]
    public async Task LargePoolReusesInstanceAcrossThreadHandoffs()
    {
        const int iterations = 20_000;
        var pool = new ObjectPool<PooledItem, CountingPolicy>(maxCapacity: 65);
        PooledItem expected = pool.Rent();
        pool.Return(expected);
        using var rented = new AutoResetEvent(false);
        using var returned = new AutoResetEvent(false);
        PooledItem? handoff = null;

        Task producer = Task.Factory.StartNew(
            () =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    handoff = pool.Rent();
                    rented.Set();
                    if (!returned.WaitOne(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("Return thread did not complete the handoff.");
                    }
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        Task consumer = Task.Factory.StartNew(
            () =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    if (!rented.WaitOne(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("Rent thread did not complete the handoff.");
                    }

                    pool.Return(handoff!);
                    returned.Set();
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        await Task.WhenAll(producer, consumer).WaitAsync(TimeSpan.FromSeconds(30));

        await Assert.That(pool.Rent()).IsSameReferenceAs(expected);
        await Assert.That(expected.ResetCount).IsEqualTo(iterations + 1);
    }

    private static void StressPool(
        ObjectPool<StressItem, StressPolicy> pool,
        StressState state,
        Barrier start,
        int workerIndex,
        int iterations)
    {
        start.SignalAndWait();

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            StressItem item = pool.Rent();
            if (Interlocked.Exchange(ref item.InUse, 1) != 0)
            {
                state.RecordFailure($"Item {item.Id} was rented concurrently.");
            }

            if (item.Value != 0 || item.ValueComplement != 0)
            {
                state.RecordFailure($"Item {item.Id} was rented with stale or torn state.");
            }

            long value = ((long)workerIndex << 32) | (uint)(iteration + 1);
            item.Value = value;
            item.ValueComplement = ~value;
            Volatile.Write(ref item.InUse, 0);
            pool.Return(item);
        }
    }

    private sealed class PooledItem(int id)
    {
        internal int Id { get; } = id;
        internal int InUse;
        internal int ResetCount;
    }

    private sealed class StressItem(int id)
    {
        internal int Id { get; } = id;
        internal int InUse;
        internal long Value;
        internal long ValueComplement;
    }

    private sealed class StressState
    {
        private const int MaximumRecordedFailures = 100;
        private int _failureCount;

        internal ConcurrentQueue<string> Failures { get; } = new();
        internal int CreatedCount;
        internal int ResetCount;

        internal void RecordFailure(string message)
        {
            if (Interlocked.Increment(ref _failureCount) <= MaximumRecordedFailures)
            {
                Failures.Enqueue(message);
            }
        }
    }

    private readonly struct StressPolicy(StressState state) : IPooledObjectPolicy<StressItem>
    {
        public StressItem Create()
            => new(Interlocked.Increment(ref state.CreatedCount));

        public bool TryReset(StressItem obj)
        {
            if ((obj.Value != 0 || obj.ValueComplement != 0)
                && obj.ValueComplement != ~obj.Value)
            {
                state.RecordFailure($"Item {obj.Id} contained torn state during reset.");
            }

            obj.Value = 0;
            obj.ValueComplement = 0;
            Interlocked.Increment(ref state.ResetCount);
            return true;
        }
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
