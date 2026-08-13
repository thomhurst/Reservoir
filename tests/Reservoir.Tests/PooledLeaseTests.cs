namespace Reservoir.Tests;

public class PooledLeaseTests
{
    [Test]
    public async Task DisposeReturnsValueToGenericScopedPool()
    {
        var pool = new ObjectPool<PooledItem, CountingPolicy>(maxCapacity: 1);
        PooledItem expected;

        {
            using PooledLease<PooledItem, CountingPolicy> lease = pool.RentScoped();
            expected = lease.Value;
        }

        PooledItem actual;
        int resetCount;
        {
            using PooledLease<PooledItem, CountingPolicy> lease = pool.RentScoped();
            actual = lease.Value;
            resetCount = expected.ResetCount;
        }

        await Assert.That(actual).IsSameReferenceAs(expected);
        await Assert.That(resetCount).IsEqualTo(1);
    }

    [Test]
    public async Task DisposeReturnsValueToConvenienceScopedPool()
    {
        var pool = new ObjectPool<PooledItem>(() => new PooledItem(), maxCapacity: 1);
        PooledItem expected;

        {
            using PooledLease<PooledItem> lease = pool.RentScoped();
            expected = lease.Value;
        }

        PooledItem actual;
        {
            using PooledLease<PooledItem> lease = pool.RentScoped();
            actual = lease.Value;
        }

        await Assert.That(actual).IsSameReferenceAs(expected);
    }

    [Test]
    public async Task OutOverloadExposesLeasedValue()
    {
        var pool = new ObjectPool<PooledItem, CountingPolicy>(maxCapacity: 1);
        PooledItem value;
        bool valuesMatch;

        {
            using PooledLease<PooledItem, CountingPolicy> lease = pool.RentScoped(out value);
            valuesMatch = ReferenceEquals(value, lease.Value);
        }

        await Assert.That(valuesMatch).IsTrue();
        PooledItem actual;
        {
            using PooledLease<PooledItem, CountingPolicy> lease = pool.RentScoped();
            actual = lease.Value;
        }

        await Assert.That(actual).IsSameReferenceAs(value);
    }

    [Test]
    public async Task RepeatedDisposeReturnsValueOnce()
    {
        var pool = new ObjectPool<PooledItem, CountingPolicy>(maxCapacity: 1);
        PooledItem value = RentAndDisposeTwice(pool);
        PooledItem actual;
        int resetCount;

        {
            using PooledLease<PooledItem, CountingPolicy> lease = pool.RentScoped();
            actual = lease.Value;
            resetCount = value.ResetCount;
        }

        await Assert.That(resetCount).IsEqualTo(1);
        await Assert.That(actual).IsSameReferenceAs(value);
    }

    [Test]
    public async Task CopiedLeaseReturnsValueOnce()
    {
        var pool = new ObjectPool<PooledItem, CountingPolicy>(maxCapacity: 2);
        PooledLease<PooledItem, CountingPolicy> lease = pool.RentScoped();
        PooledLease<PooledItem, CountingPolicy> copy = lease;
        PooledItem value = lease.Value;

        lease.Dispose();
        PooledLease<PooledItem, CountingPolicy> firstRentalLease = pool.RentScoped();
        PooledItem firstRental = firstRentalLease.Value;
        copy.Dispose();
        PooledItem secondRental = pool.Rent();
        int resetCount = value.ResetCount;

        firstRentalLease.Dispose();
        pool.Return(secondRental);

        await Assert.That(firstRental).IsSameReferenceAs(value);
        await Assert.That(secondRental).IsNotSameReferenceAs(value);
        await Assert.That(resetCount).IsEqualTo(1);
    }

    [Test]
    public async Task CopiedConvenienceLeaseReturnsValueOnce()
    {
        var pool = new ObjectPool<PooledItem>(() => new PooledItem(), maxCapacity: 2);
        PooledLease<PooledItem> lease = pool.RentScoped();
        PooledLease<PooledItem> copy = lease;
        PooledItem value = lease.Value;

        lease.Dispose();
        PooledLease<PooledItem> firstRentalLease = pool.RentScoped();
        PooledItem firstRental = firstRentalLease.Value;
        copy.Dispose();
        PooledItem secondRental = pool.Rent();

        firstRentalLease.Dispose();
        pool.Return(secondRental);

        await Assert.That(firstRental).IsSameReferenceAs(value);
        await Assert.That(secondRental).IsNotSameReferenceAs(value);
    }

    [Test]
    public async Task ValueThrowsAfterLeaseIsDisposed()
    {
        var pool = new ObjectPool<PooledItem, CountingPolicy>(maxCapacity: 1);
        PooledLease<PooledItem, CountingPolicy> lease = pool.RentScoped();

        lease.Dispose();

        bool threw = false;
        try
        {
            _ = lease.Value;
        }
        catch (ObjectDisposedException)
        {
            threw = true;
        }

        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task StaleCopyCannotReturnLaterRental()
    {
        var pool = new ObjectPool<PooledItem, CountingPolicy>(maxCapacity: 2);
        PooledLease<PooledItem, CountingPolicy> first = pool.RentScoped();
        PooledLease<PooledItem, CountingPolicy> stale = first;
        PooledItem firstValue = first.Value;
        first.Dispose();

        PooledLease<PooledItem, CountingPolicy> second = pool.RentScoped();
        stale.Dispose();
        PooledItem concurrent = pool.Rent();
        bool reusedFirstValue = ReferenceEquals(second.Value, firstValue);
        bool valuesAreDistinct = !ReferenceEquals(concurrent, second.Value);

        second.Dispose();
        pool.Return(concurrent);

        await Assert.That(reusedFirstValue).IsTrue();
        await Assert.That(valuesAreDistinct).IsTrue();
    }

    [Test]
    public async Task NestedLeasesUseIndependentState()
    {
        var pool = new ObjectPool<PooledItem, CountingPolicy>(maxCapacity: 2);
        PooledLease<PooledItem, CountingPolicy> firstLease = pool.RentScoped();
        PooledLease<PooledItem, CountingPolicy> secondLease = pool.RentScoped();
        PooledItem first = firstLease.Value;
        PooledItem second = secondLease.Value;

        secondLease.Dispose();
        firstLease.Dispose();

        PooledLease<PooledItem, CountingPolicy> firstRentalLease = pool.RentScoped();
        PooledLease<PooledItem, CountingPolicy> secondRentalLease = pool.RentScoped();
        PooledItem firstRental = firstRentalLease.Value;
        PooledItem secondRental = secondRentalLease.Value;
        firstRentalLease.Dispose();
        secondRentalLease.Dispose();

        await Assert.That(new[] { firstRental, secondRental }).Contains(first);
        await Assert.That(new[] { firstRental, secondRental }).Contains(second);
    }

    [Test]
    public async Task ScopedPoolsKeepThreadLocalItemsIsolated()
    {
        var firstPool = new ObjectPool<PooledItem, CountingPolicy>(maxCapacity: 1);
        var secondPool = new ObjectPool<PooledItem, CountingPolicy>(maxCapacity: 1);
        PooledItem first;
        PooledItem second;

        {
            using PooledLease<PooledItem, CountingPolicy> lease = firstPool.RentScoped();
            first = lease.Value;
        }

        {
            using PooledLease<PooledItem, CountingPolicy> lease = secondPool.RentScoped();
            second = lease.Value;
        }

        PooledItem firstRental;
        PooledItem secondRental;

        {
            using PooledLease<PooledItem, CountingPolicy> lease = firstPool.RentScoped();
            firstRental = lease.Value;
        }

        {
            using PooledLease<PooledItem, CountingPolicy> lease = secondPool.RentScoped();
            secondRental = lease.Value;
        }

        await Assert.That(firstRental).IsSameReferenceAs(first);
        await Assert.That(secondRental).IsSameReferenceAs(second);
        await Assert.That(first).IsNotSameReferenceAs(second);
    }

    [Test]
    public async Task WarmScopedPoolAllocatesNothing()
    {
        var pool = new ObjectPool<PooledItem, CountingPolicy>(maxCapacity: 1);

        {
            using PooledLease<PooledItem, CountingPolicy> lease = pool.RentScoped();
            _ = lease.Value.ResetCount;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        int resetCount = 0;

        for (int i = 0; i < 1_000; i++)
        {
            using PooledLease<PooledItem, CountingPolicy> lease = pool.RentScoped();
            resetCount = lease.Value.ResetCount;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(resetCount).IsEqualTo(1_000);
        await Assert.That(allocated).IsEqualTo(0);
    }

    private static PooledItem RentAndDisposeTwice(
        ObjectPool<PooledItem, CountingPolicy> pool)
    {
        PooledLease<PooledItem, CountingPolicy> lease = pool.RentScoped();
        PooledItem value = lease.Value;
        lease.Dispose();
        lease.Dispose();
        return value;
    }

    private sealed class PooledItem
    {
        internal int ResetCount;
    }

    private readonly struct CountingPolicy : IPooledObjectPolicy<PooledItem>
    {
        public PooledItem Create() => new();

        public bool TryReset(PooledItem obj)
        {
            obj.ResetCount++;
            return true;
        }
    }
}
