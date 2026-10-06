using System.Collections.Concurrent;

namespace Reservoir.Tests;

public class StripedObjectStoreTests
{
    [Test]
    [Arguments(65, 20, 8)]
    [Arguments(80, 20, 10)]
    [Arguments(96, 20, 12)]
    [Arguments(128, 20, 16)]
    [Arguments(256, 20, 20)]
    [Arguments(4_096, 20, 20)]
    [Arguments(4_096, 24, 24)]
    [Arguments(65_536, 40, 32)]
    public async Task StripeCountUsesEveryCapacitySupportedProcessor(
        int capacity,
        int processorLimit,
        int expected)
    {
        int stripeCount = StripedObjectStore<StoreItem>.GetStripeCount(
            capacity,
            processorLimit);

        await Assert.That(stripeCount).IsEqualTo(expected);
        await Assert.That(capacity / stripeCount).IsGreaterThanOrEqualTo(8);
    }

    [Test]
    [Arguments(20)]
    [Arguments(24)]
    public async Task InitialThreadOrdinalsUseEveryStripeBeforeRepeating(int stripeCount)
    {
        var store = new StripedObjectStore<StoreItem>(stripeCount * 16, stripeCount);
        var distribution = new int[stripeCount];
        bool matchesModulo = true;

        for (uint threadOrdinal = 0; threadOrdinal < 65_536; threadOrdinal++)
        {
            int stripeIndex = store.GetAffinityIndex(threadOrdinal);
            matchesModulo &= stripeIndex == (int)(threadOrdinal % (uint)stripeCount);

            if (threadOrdinal < stripeCount)
            {
                distribution[stripeIndex]++;
            }
        }

        matchesModulo &= store.GetAffinityIndex(uint.MaxValue)
            == (int)(uint.MaxValue % (uint)stripeCount);

        await Assert.That(matchesModulo).IsTrue();
        await Assert.That(distribution).IsEquivalentTo(Enumerable.Repeat(1, stripeCount));
    }

    [Test]
    [Arguments(65, 4)]
    [Arguments(320, 20)]
    public async Task PushPopPreservesItemsAcrossFullAndEmptyTransitions(
        int capacity,
        int processorLimit)
    {
        var store = new StripedObjectStore<StoreItem>(capacity, processorLimit);
        StoreItem[] expected = Enumerable.Range(0, capacity)
            .Select(static id => new StoreItem(id))
            .ToArray();

        foreach (StoreItem item in expected)
        {
            await Assert.That(store.TryPush(item)).IsTrue();
        }

        await Assert.That(store.TryPush(new StoreItem(-1))).IsFalse();

        var actual = new HashSet<StoreItem>();
        while (store.TryPop(out StoreItem? item))
        {
            await Assert.That(item).IsNotNull();
            actual.Add(item!);
        }

        await Assert.That(actual).IsEquivalentTo(expected);
        await Assert.That(store.TryPop(out StoreItem? emptyItem)).IsFalse();
        await Assert.That(emptyItem).IsNull();
    }

    [Test]
    [Arguments(65)]
    [Arguments(72)]
    [Arguments(100)]
    public async Task RingKeepsExactCapacityAcrossWrappedTransitions(int capacity)
    {
        // One stripe: the direct item plus a ring that is exactly a power of two (65) or
        // rounded up past the capacity (72, 100), so the capacity check and cell sequences
        // both have to hold as positions wrap the ring many times at shifting offsets.
        var store = new StripedObjectStore<StoreItem>(capacity, processorLimit: 1);
        StoreItem[] expected = Enumerable.Range(0, capacity)
            .Select(static id => new StoreItem(id))
            .ToArray();
        var overflow = new StoreItem(-1);
        var failures = new List<string>();

        for (int round = 0; round < 64; round++)
        {
            for (int shift = 0; shift < round % 7; shift++)
            {
                if (!store.TryPush(expected[0]) || !store.TryPop(out _))
                {
                    failures.Add($"Round {round} could not shift the ring offset.");
                }
            }

            foreach (StoreItem item in expected)
            {
                if (!store.TryPush(item))
                {
                    failures.Add($"Round {round} rejected item {item.Id} below capacity.");
                }
            }

            if (store.TryPush(overflow))
            {
                failures.Add($"Round {round} accepted an item past capacity.");
            }

            var drained = new HashSet<StoreItem>();
            while (store.TryPop(out StoreItem? item))
            {
                if (!drained.Add(item!))
                {
                    failures.Add($"Round {round} popped item {item!.Id} twice.");
                }
            }

            if (!drained.SetEquals(expected))
            {
                failures.Add($"Round {round} drained {drained.Count} of {capacity} items.");
            }
        }

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task ConcurrentPushPopStressPreservesOwnershipAcrossFullAndEmptyTransitions()
    {
        const int workerCount = 8;
        const int itemsPerWorker = 8;
        const int capacity = workerCount * itemsPerWorker;
#if NET8_0
        const int iterations = 250;
#else
        const int iterations = 1_000;
#endif
        var store = new StripedObjectStore<StoreItem>(capacity);
        var failures = new ConcurrentQueue<string>();
        using var start = new Barrier(workerCount + 1);
        using var phase = new Barrier(workerCount + 1);
        var finalItems = new StoreItem[capacity];
        StoreItem[] initialItems = Enumerable.Range(0, capacity)
            .Select(static id => new StoreItem(id))
            .ToArray();

        IEnumerable<Action<CancellationToken>> workers = Enumerable.Range(0, workerCount)
            .Select<int, Action<CancellationToken>>(workerIndex => token => RunTransitionStress(
                store,
                initialItems,
                finalItems,
                failures,
                start,
                phase,
                workerIndex,
                itemsPerWorker,
                iterations,
                token));

        await ConcurrentTestWorkers.RunAsync(workers.Append(token =>
        {
            start.SignalAndWait(token);
            var overflow = new StoreItem(-1);

            for (int iteration = 0; iteration < iterations; iteration++)
            {
                phase.SignalAndWait(token);
                if (store.TryPush(overflow))
                {
                    failures.Enqueue($"Store accepted an item past capacity in iteration {iteration}.");
                    if (store.TryPop(out StoreItem? recovered))
                    {
                        overflow = recovered!;
                    }
                }

                phase.SignalAndWait(token);
                phase.SignalAndWait(token);
                if (store.TryPop(out StoreItem? unexpected))
                {
                    failures.Enqueue($"Store retained an extra item after draining in iteration {iteration}.");
                    overflow = unexpected!;
                }

                phase.SignalAndWait(token);
            }
        }));

        await Assert.That(failures).IsEmpty();
        await Assert.That(finalItems.ToHashSet().Count).IsEqualTo(capacity);
        await Assert.That(finalItems).IsEquivalentTo(initialItems);
    }

    private static void RunTransitionStress(
        StripedObjectStore<StoreItem> store,
        StoreItem[] initialItems,
        StoreItem[] finalItems,
        ConcurrentQueue<string> failures,
        Barrier start,
        Barrier phase,
        int workerIndex,
        int itemsPerWorker,
        int iterations,
        CancellationToken token)
    {
        var heldItems = new StoreItem[itemsPerWorker];
        Array.Copy(
            initialItems,
            workerIndex * itemsPerWorker,
            heldItems,
            0,
            itemsPerWorker);
        start.SignalAndWait(token);

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            foreach (StoreItem item in heldItems)
            {
                if (Interlocked.Exchange(ref item.InStore, 1) != 0)
                {
                    failures.Enqueue($"Item {item.Id} was pushed without exclusive ownership.");
                }

                while (!store.TryPush(item))
                {
                    token.ThrowIfCancellationRequested();
                    Thread.Yield();
                }
            }

            phase.SignalAndWait(token);
            phase.SignalAndWait(token);

            for (int i = 0; i < heldItems.Length; i++)
            {
                StoreItem? item;
                while (!store.TryPop(out item))
                {
                    token.ThrowIfCancellationRequested();
                    Thread.Yield();
                }

                if (Interlocked.Exchange(ref item!.InStore, 0) != 1)
                {
                    failures.Enqueue($"Item {item.Id} was popped without exclusive storage ownership.");
                }

                heldItems[i] = item;
            }

            phase.SignalAndWait(token);
            phase.SignalAndWait(token);
        }

        Array.Copy(
            heldItems,
            0,
            finalItems,
            workerIndex * itemsPerWorker,
            itemsPerWorker);
    }

    private sealed class StoreItem(int id)
    {
        internal int Id { get; } = id;
        internal int InStore;
    }
}
