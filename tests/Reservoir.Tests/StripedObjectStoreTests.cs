using System.Collections.Concurrent;
using System.Reflection;

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
    [Arguments(0)]
    [Arguments(int.MaxValue - 64)]
    [Arguments(-64)]
    public async Task ConcurrentPushPopStressPreservesOwnershipAcrossFullAndEmptyTransitions(
        int initialVersion)
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
        SeedHeadVersions(store, initialVersion);
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

    private static void SeedHeadVersions(StripedObjectStore<StoreItem> store, int version)
    {
        // Start near both signed and unsigned version boundaries without billions of operations.
        // Preserve the initialized node indices and change versions before publishing to workers.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var stripes = (Array)typeof(StripedObjectStore<StoreItem>)
            .GetField("_stripes", flags)!.GetValue(store)!;
        foreach (object stripe in stripes)
        {
            foreach (string name in new[] { "AvailableHead", "FreeHead" })
            {
                FieldInfo field = stripe.GetType().GetField(name, flags)!;
                long head = (long)field.GetValue(stripe)!;
                field.SetValue(stripe, ((long)version << 32) | (uint)head);
            }
        }
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
