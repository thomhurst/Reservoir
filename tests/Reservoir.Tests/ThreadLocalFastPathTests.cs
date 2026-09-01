using System.Collections.Concurrent;

namespace Reservoir.Tests;

public class ThreadLocalFastPathTests
{
    [Test]
    public async Task SameThreadRoundTripReusesTheThreadLocalObject()
    {
        var pool = new ObjectPool<Item, Policy>(default, maxCapacity: 4, threadLocalFastPath: true);
        Item expected = pool.Rent();

        pool.Return(expected);
        Item actual = pool.Rent();

        await Assert.That(actual).IsSameReferenceAs(expected);
        await Assert.That(actual.ResetCount).IsEqualTo(1);
    }

    [Test]
    public async Task RetainsOneExtraObjectPerThreadBeyondSharedCapacity()
    {
        var pool = new ObjectPool<Item, Policy>(default, maxCapacity: 2, threadLocalFastPath: true);
        var items = new[] { pool.Rent(), pool.Rent(), pool.Rent(), pool.Rent() };

        foreach (Item item in items)
        {
            pool.Return(item);
        }

        var retained = new HashSet<Item>();
        for (int i = 0; i < 3; i++)
        {
            retained.Add(pool.Rent());
        }

        // One thread-local slot plus the two shared slots retain three of the four returns.
        await Assert.That(retained.Count).IsEqualTo(3);
        await Assert.That(items[^1].Destroyed).IsTrue();
    }

    [Test]
    public async Task ResetFailureDestroysInsteadOfRetaining()
    {
        var pool = new ObjectPool<Item, Policy>(default, maxCapacity: 4, threadLocalFastPath: true);
        Item rejected = pool.Rent();
        rejected.RejectReset = true;

        pool.Return(rejected);
        Item actual = pool.Rent();

        await Assert.That(actual).IsNotSameReferenceAs(rejected);
        await Assert.That(rejected.Destroyed).IsTrue();
    }

    [Test]
    public async Task ClearDestroysTheThreadLocalObject()
    {
        var pool = new ObjectPool<Item, Policy>(default, maxCapacity: 4, threadLocalFastPath: true);
        Item item = pool.Rent();
        pool.Return(item);

        pool.Clear();

        await Assert.That(item.Destroyed).IsTrue();
        await Assert.That(pool.Rent()).IsNotSameReferenceAs(item);
    }

    [Test]
    public async Task DisposeDestroysTheThreadLocalObjectAndClosesThePool()
    {
        var pool = new ObjectPool<Item, Policy>(default, maxCapacity: 4, threadLocalFastPath: true);
        Item item = pool.Rent();
        pool.Return(item);

        pool.Dispose();

        await Assert.That(item.Destroyed).IsTrue();
        await Assert.That(() => pool.Rent()).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task ReturnAfterDisposeDestroysTheObject()
    {
        var pool = new ObjectPool<Item, Policy>(default, maxCapacity: 4, threadLocalFastPath: true);
        Item item = pool.Rent();

        pool.Dispose();
        pool.Return(item);

        await Assert.That(item.Destroyed).IsTrue();
    }

    [Test]
    public async Task ConcurrentClearNeverYieldsADestroyedRentalNorDestroysTwice()
    {
        var pool = new ObjectPool<AuditedItem, AuditedPolicy>(
            default,
            maxCapacity: 4,
            threadLocalFastPath: true);
        using var stop = new CancellationTokenSource();

        Task clearing = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                pool.Clear();
            }
        });

        Task[] renters = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 50_000; i++)
            {
                AuditedItem item = pool.Rent();
                if (Volatile.Read(ref item.DestroyCount) != 0)
                {
                    throw new InvalidOperationException("Rented a destroyed item.");
                }

                pool.Return(item);
            }
        })).ToArray();

        await Task.WhenAll(renters);
        stop.Cancel();
        await clearing;
        pool.Clear();

        await Assert.That(AuditedPolicy.Items.All(item => item.DestroyCount <= 1)).IsTrue();
    }

    public sealed class Item
    {
        public int ResetCount { get; set; }

        public bool RejectReset { get; set; }

        public bool Destroyed { get; set; }
    }

    public sealed class AuditedItem
    {
        public int DestroyCount;
    }

    public readonly struct AuditedPolicy : IPooledObjectDestroyPolicy<AuditedItem>
    {
        public static readonly ConcurrentBag<AuditedItem> Items = [];

        public AuditedItem Create()
        {
            var item = new AuditedItem();
            Items.Add(item);
            return item;
        }

        public bool TryReset(AuditedItem obj) => true;

        public void Destroy(AuditedItem obj) => Interlocked.Increment(ref obj.DestroyCount);
    }

    public readonly struct Policy : IPooledObjectDestroyPolicy<Item>
    {
        public Item Create() => new();

        public bool TryReset(Item obj)
        {
            obj.ResetCount++;
            return !obj.RejectReset;
        }

        public void Destroy(Item obj) => obj.Destroyed = true;
    }
}
