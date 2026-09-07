namespace Reservoir.Tests;

public class NestedScopedLeaseTests
{
    [Test]
    public async Task OutOfOrderReleaseAndStaleCopiesKeepAllActiveLeasesDistinct()
    {
        var firstValue = new Marker();
        var secondValue = new Marker();
        var thirdValue = new Marker();
        var first = new ScopedPoolLease<Marker>(firstValue);
        var second = new ScopedPoolLease<Marker>(secondValue);
        var third = new ScopedPoolLease<Marker>(thirdValue);
        ScopedPoolLease<Marker> stale = second;

        bool releasedSecond = second.TryRelease(out Marker returnedSecond);
        var replacement = new ScopedPoolLease<Marker>(secondValue);
        bool staleReleased = stale.TryRelease(out _);
        bool firstStillOwned = ReferenceEquals(first.Value, firstValue);
        bool thirdStillOwned = ReferenceEquals(third.Value, thirdValue);
        bool replacementStillOwned = ReferenceEquals(replacement.Value, secondValue);
        bool releasedFirst = first.TryRelease(out _);
        bool releasedThird = third.TryRelease(out _);
        bool releasedReplacement = replacement.TryRelease(out _);
        bool duplicateRelease = replacement.TryRelease(out _);

        await Assert.That(releasedSecond && ReferenceEquals(returnedSecond, secondValue)).IsTrue();
        await Assert.That(staleReleased || duplicateRelease).IsFalse();
        await Assert.That(firstStillOwned && thirdStillOwned && replacementStillOwned).IsTrue();
        await Assert.That(releasedFirst && releasedThird && releasedReplacement).IsTrue();
    }

    [Test]
    public async Task WarmDeepLeasesAllocateNothingAndKeepDistinctOwnership()
    {
        using var pool = new ObjectPool<Marker, Policy>(maxCapacity: 64);
        var active = new HashSet<Marker>(64);
        for (int i = 0; i < 100; i++)
        {
            RentNested(pool, active, 32);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            RentNested(pool, active, 32);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(allocated).IsEqualTo(0);
        await Assert.That(active.Count).IsEqualTo(0);
    }

    private static void RentNested(ObjectPool<Marker, Policy> pool, HashSet<Marker> active, int depth)
    {
        using PooledLease<Marker, Policy> lease = pool.RentScoped(out Marker item);
        if (!active.Add(item))
        {
            throw new InvalidOperationException("Two active leases own the same object.");
        }

        if (depth > 1)
        {
            RentNested(pool, active, depth - 1);
        }

        active.Remove(item);
    }

    private sealed class Marker;

    private readonly struct Policy : IPooledObjectPolicy<Marker>, INonThrowingResetPolicy
    {
        public Marker Create() => new();
        public bool TryReset(Marker obj) => true;
    }
}
