namespace Reservoir.Tests;

public class ReturnOnlyAllocationTests
{
    [Test]
    public async Task FirstReturnOnCompletionThreadDoesNotAllocateAndLaterRentCanCache()
    {
        using var pool = new ObjectPool<Item, Policy>(default, 1, threadLocalFastPath: true);
        Item item = pool.Rent();
        long allocated = -1;
        bool reused = false;
        Exception? failure = null;
        var returner = new Thread(() =>
        {
            try
            {
                // Warm generic statics and affinity on this thread without touching the
                // measured pool's thread-local tier.
                using var warm = new ObjectPool<Item, Policy>(default, 1, threadLocalFastPath: true);
                warm.Return(warm.Rent());

                long before = GC.GetAllocatedBytesForCurrentThread();
                pool.Return(item);
                allocated = GC.GetAllocatedBytesForCurrentThread() - before;

                Item rented = pool.Rent();
                pool.Return(rented);
                reused = ReferenceEquals(rented, item) && ReferenceEquals(pool.Rent(), item);
                pool.Return(item);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        returner.Start();
        returner.Join();

        await Assert.That(failure).IsNull();
        await Assert.That(allocated).IsEqualTo(0);
        await Assert.That(reused).IsTrue();
    }

    [Test]
    public async Task ReturningExternalObjectDoesNotInitializeAnUnusedTier()
    {
        using var pool = new ObjectPool<Item, Policy>(default, 1, threadLocalFastPath: true);
        using var warm = new ObjectPool<Item, Policy>(default, 1, threadLocalFastPath: true);
        warm.Return(warm.Rent());
        var item = new Item();

        long before = GC.GetAllocatedBytesForCurrentThread();
        pool.Return(item);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Item rented = pool.Rent();
        pool.Return(rented);

        await Assert.That(allocated).IsEqualTo(0);
        await Assert.That(rented).IsSameReferenceAs(item);
    }

    private sealed class Item;

    private readonly struct Policy : IPooledObjectPolicy<Item>, INonThrowingResetPolicy
    {
        public Item Create() => new();
        public bool TryReset(Item obj) => true;
    }
}
