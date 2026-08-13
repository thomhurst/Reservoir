namespace Reservoir.Tests;

public class PooledLeaseTests
{
    [Test]
    public async Task DisposeReturnsValueToGenericPool()
    {
        var pool = new ObjectPool<PooledItem, CountingPolicy>(maxCapacity: 1);
        PooledItem expected;

        {
            using PooledLease<PooledItem, CountingPolicy> lease = pool.RentScoped();
            expected = lease.Value;
        }

        await Assert.That(pool.Rent()).IsSameReferenceAs(expected);
        await Assert.That(expected.ResetCount).IsEqualTo(1);
    }

    [Test]
    public async Task DisposeReturnsValueToConveniencePool()
    {
        var pool = new ObjectPool<PooledItem>(() => new PooledItem(), maxCapacity: 1);
        PooledItem expected;

        {
            using PooledLease<PooledItem> lease = pool.RentScoped();
            expected = lease.Value;
        }

        await Assert.That(pool.Rent()).IsSameReferenceAs(expected);
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
        await Assert.That(pool.Rent()).IsSameReferenceAs(value);
    }

    [Test]
    public async Task UncheckedLeaseReturnsValueToGenericPool()
    {
        var pool = new ObjectPool<PooledItem, CountingPolicy>(maxCapacity: 1);
        PooledItem value;
        bool valuesMatch;

        {
            using UncheckedPooledLease<PooledItem, CountingPolicy> lease =
                pool.RentScopedUnchecked(out value);
            valuesMatch = ReferenceEquals(value, lease.Value);
        }

        await Assert.That(valuesMatch).IsTrue();
        await Assert.That(pool.Rent()).IsSameReferenceAs(value);
        await Assert.That(value.ResetCount).IsEqualTo(1);
    }

    [Test]
    public async Task UncheckedLeaseReturnsValueToConveniencePool()
    {
        var pool = new ObjectPool<PooledItem>(() => new PooledItem(), maxCapacity: 1);
        PooledItem expected;

        {
            using UncheckedPooledLease<PooledItem> lease = pool.RentScopedUnchecked();
            expected = lease.Value;
        }

        await Assert.That(pool.Rent()).IsSameReferenceAs(expected);
    }

    [Test]
    public async Task RepeatedDisposeReturnsValueOnce()
    {
        var pool = new ObjectPool<PooledItem, CountingPolicy>(maxCapacity: 1);
        PooledItem value = RentAndDisposeTwice(pool);

        await Assert.That(value.ResetCount).IsEqualTo(1);
        await Assert.That(pool.Rent()).IsSameReferenceAs(value);
    }

    [Test]
    public async Task CopiedLeaseReturnsValueOnce()
    {
        var pool = new ObjectPool<PooledItem, CountingPolicy>(maxCapacity: 2);
        PooledLease<PooledItem, CountingPolicy> lease = pool.RentScoped();
        PooledLease<PooledItem, CountingPolicy> copy = lease;
        PooledItem value = lease.Value;

        lease.Dispose();
        PooledItem firstRental = pool.Rent();
        copy.Dispose();
        PooledItem secondRental = pool.Rent();

        await Assert.That(firstRental).IsSameReferenceAs(value);
        await Assert.That(secondRental).IsNotSameReferenceAs(value);
        await Assert.That(value.ResetCount).IsEqualTo(1);
    }

    [Test]
    public async Task CopiedConvenienceLeaseReturnsValueOnce()
    {
        var pool = new ObjectPool<PooledItem>(() => new PooledItem(), maxCapacity: 2);
        PooledLease<PooledItem> lease = pool.RentScoped();
        PooledLease<PooledItem> copy = lease;
        PooledItem value = lease.Value;

        lease.Dispose();
        PooledItem firstRental = pool.Rent();
        copy.Dispose();
        PooledItem secondRental = pool.Rent();

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

        PooledItem firstRental = pool.Rent();
        PooledItem secondRental = pool.Rent();
        await Assert.That(new[] { firstRental, secondRental }).Contains(first);
        await Assert.That(new[] { firstRental, secondRental }).Contains(second);
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
