using System.Reflection;

namespace Reservoir.Tests;

public class SmallPoolHintTests
{
    [Test]
    [Arguments(1)]
    [Arguments(3)]
    [Arguments(31)]
    [Arguments(32)]
    public async Task RemoteReuseAcrossPoolSizesPreservesOwnership(int capacity)
    {
        bool correct = await OnDedicatedThread(() =>
        {
            using var large = new ObjectPool<Item, Policy>(64);
            using var small = new ObjectPool<Item, Policy>(capacity);
            var first = new Item();
            var second = new Item();
            SetHome(large, 63);
            large.Return(first);
            SetHome(large, 0);
            Item rentedFirst = large.Rent();

            // The remote hint from the larger pool is outside this pool's capacity.
            SetHome(small, capacity - 1);
            small.Return(second);
            SetHome(small, 0);
            Item rentedSecond = small.Rent();
            Item miss = small.Rent();
            return ReferenceEquals(first, rentedFirst)
                && ReferenceEquals(second, rentedSecond)
                && !ReferenceEquals(miss, first) && !ReferenceEquals(miss, second);
        });

        await Assert.That(correct).IsTrue();
    }

    [Test]
    public async Task EmptyHintFallsBackToWrappedScan()
    {
        bool correct = await OnDedicatedThread(() =>
        {
            using var pool = new ObjectPool<Item, Policy>(32);
            var first = new Item();
            var second = new Item();
            SetHome(pool, 30);
            pool.Return(first);
            SetHome(pool, 0);
            Item rentedFirst = pool.Rent();
            SetHome(pool, 2);
            pool.Return(second);
            SetHome(pool, 31);
            Item rentedSecond = pool.Rent();
            return ReferenceEquals(first, rentedFirst) && ReferenceEquals(second, rentedSecond);
        });

        await Assert.That(correct).IsTrue();
    }

    [Test]
    public async Task HomeSlotRemainsPreferredAfterRemoteReuse()
    {
        bool correct = await OnDedicatedThread(() =>
        {
            using var pool = new ObjectPool<Item, Policy>(32);
            var first = new Item();
            var remote = new Item();
            var local = new Item();
            SetHome(pool, 30);
            pool.Return(first);
            SetHome(pool, 0);
            Item rentedFirst = pool.Rent();
            SetHome(pool, 30);
            pool.Return(remote);
            SetHome(pool, 0);
            pool.Return(local);
            Item rentedLocal = pool.Rent();
            Item rentedRemote = pool.Rent();
            return ReferenceEquals(first, rentedFirst)
                && ReferenceEquals(local, rentedLocal) && ReferenceEquals(remote, rentedRemote);
        });

        await Assert.That(correct).IsTrue();
    }

    private static Task<bool> OnDedicatedThread(Func<bool> action)
        => Task.Factory.StartNew(action, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static void SetHome(ObjectPool<Item, Policy> pool, int home)
    {
        uint ordinal = 0;
        while (pool.GetAffinityIndex(ordinal) != home)
        {
            ordinal++;
        }

        typeof(ObjectPool<Item, Policy>)
            .GetField("_threadStripe", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, checked((int)ordinal + 1));
    }

    private sealed class Item;

    private readonly struct Policy : IPooledObjectPolicy<Item>
    {
        public Item Create() => new();
        public bool TryReset(Item item) => true;
        public void Destroy(Item item) { }
    }
}
