using System.Collections.Concurrent;

namespace Reservoir.Tests;

public class StripedObjectStoreTests
{
    [Test]
    [Arguments(65, 20, 4)]
    [Arguments(256, 20, 16)]
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
        await Assert.That(capacity / stripeCount).IsGreaterThanOrEqualTo(16);
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

        Task[] workers = Enumerable.Range(0, workerCount)
            .Select(workerIndex => Task.Factory.StartNew(
                () => RunTransitionStress(
                    store,
                    initialItems,
                    finalItems,
                    failures,
                    start,
                    phase,
                    workerIndex,
                    itemsPerWorker,
                    iterations),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default))
            .ToArray();

        start.SignalAndWait();
        var overflow = new StoreItem(-1);

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            phase.SignalAndWait();
            if (store.TryPush(overflow))
            {
                failures.Enqueue($"Store accepted an item past capacity in iteration {iteration}.");
                if (store.TryPop(out StoreItem? recovered))
                {
                    overflow = recovered!;
                }
            }

            phase.SignalAndWait();
            phase.SignalAndWait();
            if (store.TryPop(out StoreItem? unexpected))
            {
                failures.Enqueue($"Store retained an extra item after draining in iteration {iteration}.");
                overflow = unexpected!;
            }

            phase.SignalAndWait();
        }

        await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(30));

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
        int iterations)
    {
        var heldItems = new StoreItem[itemsPerWorker];
        Array.Copy(
            initialItems,
            workerIndex * itemsPerWorker,
            heldItems,
            0,
            itemsPerWorker);
        start.SignalAndWait();

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
                    Thread.Yield();
                }
            }

            phase.SignalAndWait();
            phase.SignalAndWait();

            for (int i = 0; i < heldItems.Length; i++)
            {
                StoreItem? item;
                while (!store.TryPop(out item))
                {
                    Thread.Yield();
                }

                if (Interlocked.Exchange(ref item!.InStore, 0) != 1)
                {
                    failures.Enqueue($"Item {item.Id} was popped without exclusive storage ownership.");
                }

                heldItems[i] = item;
            }

            phase.SignalAndWait();
            phase.SignalAndWait();
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
