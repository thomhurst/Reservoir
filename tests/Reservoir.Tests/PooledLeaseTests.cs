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
    public async Task RepeatedDisposeReturnsValueOnce()
    {
        var pool = new ObjectPool<PooledItem, CountingPolicy>(maxCapacity: 1);
        PooledItem value = RentAndDisposeTwice(pool);

        await Assert.That(value.ResetCount).IsEqualTo(1);
        await Assert.That(pool.Rent()).IsSameReferenceAs(value);
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
