using System.Reflection;

namespace Reservoir.Tests;

public class SmallPoolScanTests
{
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(8)]
    [Arguments(31)]
    [Arguments(32)]
    [Arguments(63)]
    [Arguments(64)]
    public async Task EveryHomeSlotRetainsAndDrainsExactCapacity(int capacity)
    {
        int destroyed = 0;
        for (int home = 0; home < capacity; home++)
        {
            destroyed += VerifyHomeSlot(capacity, home);
        }

        await Assert.That(destroyed).IsEqualTo(capacity * (capacity + 1));
    }

    private static int VerifyHomeSlot(int capacity, int home)
    {
        using var pool = new ObjectPool<Item, Policy>(maxCapacity: capacity);
        FieldInfo affinity = typeof(ObjectPool<Item, Policy>)
            .GetField("_threadStripe", BindingFlags.NonPublic | BindingFlags.Static)!;
        object? previous = affinity.GetValue(null);
        uint ordinal = 0;
        while (pool.GetAffinityIndex(ordinal) != home)
        {
            ordinal++;
        }

        affinity.SetValue(null, checked((int)ordinal + 1));
        try
        {
            var items = new Item[capacity + 1];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = pool.Rent();
            }

            foreach (Item item in items)
            {
                pool.Return(item);
            }

            if (items[^1].DisposeCount != 1)
            {
                throw new InvalidOperationException("A full pool did not destroy the excess return exactly once.");
            }

            var retained = new HashSet<Item>();
            for (int i = 0; i < capacity; i++)
            {
                Item item = pool.Rent();
                if (!retained.Add(item) || item.DisposeCount != 0)
                {
                    throw new InvalidOperationException("A scan rented a duplicate or destroyed item.");
                }
            }

            if (!retained.SetEquals(items.Take(capacity)))
            {
                throw new InvalidOperationException("A scan missed a retained slot.");
            }

            Item miss = pool.Rent();
            if (retained.Contains(miss))
            {
                throw new InvalidOperationException("An empty pool returned an outstanding rental.");
            }

            miss.Dispose();
            foreach (Item item in retained)
            {
                pool.Return(item);
            }

            pool.Clear();
            if (items.Any(item => item.DisposeCount != 1))
            {
                throw new InvalidOperationException("Clear did not destroy every retained item exactly once.");
            }

            return items.Sum(item => item.DisposeCount);
        }
        finally
        {
            affinity.SetValue(null, previous);
        }
    }

    private sealed class Item : IDisposable
    {
        internal int DisposeCount;

        public void Dispose() => DisposeCount++;
    }

    private readonly struct Policy : IPooledObjectPolicy<Item>
    {
        public Item Create() => new();
        public bool TryReset(Item item) => true;
        public void Destroy(Item item) => item.Dispose();
    }
}
