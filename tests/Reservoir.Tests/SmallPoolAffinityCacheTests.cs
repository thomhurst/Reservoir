using System.Reflection;

namespace Reservoir.Tests;

public class SmallPoolAffinityCacheTests
{
    private static readonly object CounterGate = new();

    [Test]
    [Arguments(0)]
    [Arguments(int.MaxValue - 1)]
    [Arguments(int.MaxValue)]
    [Arguments(int.MinValue)]
    [Arguments(-2)]
    [Arguments(-1)]
    public async Task CachedAffinityPreservesHomeAndSkipsZeroAcrossCounterWrap(int seed)
    {
        // Reflection state and public calls stay on one dedicated thread. The private policy
        // isolates the generic counter from other tests; the gate serializes these seed cases.
        bool correct = await Task.Factory.StartNew(
            () =>
            {
                lock (CounterGate)
                {
                    return VerifySeed(seed);
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        await Assert.That(correct).IsTrue();
    }

    private static bool VerifySeed(int seed)
    {
        Type poolType = typeof(ObjectPool<Item, Policy>);
        FieldInfo counter = poolType.GetField("s_nextThreadStripe", BindingFlags.NonPublic | BindingFlags.Static)!;
        FieldInfo hash = poolType.GetField("_threadStripeHash", BindingFlags.NonPublic | BindingFlags.Static)!;
        FieldInfo items = poolType.GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance)!;
        object? previousCounter = counter.GetValue(null);
        object? previousHash = hash.GetValue(null);
        try
        {
            int next = unchecked(seed + 1);
            if (next == 0)
            {
                next = 1;
            }

            foreach (int capacity in new[] { 1, 3, 31, 32, 63, 64 })
            {
                counter.SetValue(null, seed);
                hash.SetValue(null, 0u);
                using var pool = new ObjectPool<Item, Policy>(capacity);
                Item item = pool.Rent();
                uint cached = (uint)hash.GetValue(null)!;
                if (cached == 0 || (int)counter.GetValue(null)! != next)
                {
                    return false;
                }

                pool.Return(item);
                uint mixed = unchecked((uint)(next - 1) * 2_654_435_769u);
                int expectedHome = (capacity & (capacity - 1)) == 0
                    ? (int)(mixed & (uint)(capacity - 1))
                    : (int)(((ulong)mixed * (uint)capacity) >> 32);
                var slots = (Array)items.GetValue(pool)!;
                object slot = slots.GetValue(8 + expectedHome * 8)!;
                object? retained = slot.GetType()
                    .GetField("Element", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(slot);
                if (!ReferenceEquals(retained, item))
                {
                    return false;
                }

                // A second pool on the same thread reuses the cached affinity independently
                // of capacity. Neither a warm call nor the new pool should claim an ordinal.
                using var other = new ObjectPool<Item, Policy>(capacity == 3 ? 32 : 3);
                Item otherItem = other.Rent();
                other.Return(otherItem);
                if (!ReferenceEquals(pool.Rent(), item)
                    || (uint)hash.GetValue(null)! != cached
                    || (int)counter.GetValue(null)! != next)
                {
                    return false;
                }

                pool.Return(item);
            }

            return true;
        }
        finally
        {
            counter.SetValue(null, previousCounter);
            hash.SetValue(null, previousHash);
        }
    }

    private sealed class Item;

    private readonly struct Policy : IPooledObjectPolicy<Item>, INonThrowingResetPolicy
    {
        public Item Create() => new();
        public bool TryReset(Item item) => true;
        public void Destroy(Item item)
        {
        }
    }
}
