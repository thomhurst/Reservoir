namespace Reservoir.Tests;

public class NestedScopedLeaseTests
{
    [Test]
    public async Task FailedAcquisitionPreservesOwnerAcrossVersionWrap()
    {
        var state = new PaddedScopedPoolLeaseState();
        typeof(ScopedPoolLeaseState)
            .GetField("_version", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(state, long.MaxValue - 1);

        bool acquiredBeforeWrap = state.TryAcquire(out long beforeWrap);
        bool acquiredWhileOwned = state.TryAcquire(out _);
        state.ValidateOwnership(beforeWrap);
        bool releasedBeforeWrap = state.TryRelease(beforeWrap);
        bool acquiredAfterWrap = state.TryAcquire(out long afterWrap);
        bool staleReleased = state.TryRelease(beforeWrap);
        state.ValidateOwnership(afterWrap);
        bool releasedAfterWrap = state.TryRelease(afterWrap);

        await Assert.That(acquiredBeforeWrap && acquiredAfterWrap).IsTrue();
        await Assert.That(acquiredWhileOwned).IsFalse();
        await Assert.That(beforeWrap).IsEqualTo(long.MaxValue);
        await Assert.That(afterWrap).IsEqualTo(long.MinValue + 1);
        await Assert.That(staleReleased).IsFalse();
        await Assert.That(releasedBeforeWrap && releasedAfterWrap && state.IsAvailable).IsTrue();
    }

    [Test]
    public async Task VersionWrapPreservesAvailabilityAndOwnership()
    {
        var state = new PaddedScopedPoolLeaseState();
        typeof(ScopedPoolLeaseState)
            .GetField("_version", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(state, long.MaxValue - 1);

        long beforeWrap = state.AcquireAvailable();
        bool availableWhileOwned = state.IsAvailable;
        bool releasedBeforeWrap = state.TryRelease(beforeWrap);
        long afterWrap = state.AcquireAvailable();
        bool staleReleased = state.TryRelease(beforeWrap);
        state.ValidateOwnership(afterWrap);
        bool releasedAfterWrap = state.TryRelease(afterWrap);

        await Assert.That(beforeWrap).IsEqualTo(long.MaxValue);
        await Assert.That(availableWhileOwned).IsFalse();
        await Assert.That(afterWrap).IsEqualTo(long.MinValue + 1);
        await Assert.That(staleReleased).IsFalse();
        await Assert.That(releasedBeforeWrap && releasedAfterWrap && state.IsAvailable).IsTrue();
    }

    [Test]
    public async Task DeepOutOfOrderReleasePreservesOtherOwnersAndInvalidatesCopies()
    {
        var first = new ScopedPoolLease<Marker>(new Marker());
        var second = new ScopedPoolLease<Marker>(new Marker());
        var third = new ScopedPoolLease<Marker>(new Marker());
        var fourth = new ScopedPoolLease<Marker>(new Marker());
        var fifth = new ScopedPoolLease<Marker>(new Marker());
        ScopedPoolLease<Marker> stale = fourth;
        Marker thirdValue = third.Value;
        Marker fourthValue = fourth.Value;
        Marker fifthValue = fifth.Value;
        bool releasedFourth = fourth.TryRelease(out _);
        var replacement = new ScopedPoolLease<Marker>(fourthValue);
        bool staleReleased = stale.TryRelease(out _);
        bool staleValueThrew = false;
        try
        {
            _ = stale.Value;
        }
        catch (ObjectDisposedException)
        {
            staleValueThrew = true;
        }

        bool ownersUnchanged = ReferenceEquals(third.Value, thirdValue)
            && ReferenceEquals(fifth.Value, fifthValue)
            && ReferenceEquals(replacement.Value, fourthValue);
        bool releasedThird = third.TryRelease(out _);
        bool releasedFirst = first.TryRelease(out _);
        bool releasedReplacement = replacement.TryRelease(out _);
        bool releasedFifth = fifth.TryRelease(out _);
        bool releasedSecond = second.TryRelease(out _);

        await Assert.That(releasedFirst && releasedSecond && releasedThird
            && releasedFourth && releasedFifth && releasedReplacement).IsTrue();
        await Assert.That(staleReleased).IsFalse();
        await Assert.That(staleValueThrew && ownersUnchanged).IsTrue();
    }

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
    [Arguments(2)]
    [Arguments(32)]
    [Arguments(64)]
    public async Task WarmDeepLeasesAllocateNothingAndKeepDistinctOwnership(int depth)
    {
        using var pool = new ObjectPool<Marker, Policy>(maxCapacity: 64);
        var active = new HashSet<Marker>(64);
        for (int i = 0; i < 100; i++)
        {
            RentNested(pool, active, depth);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            RentNested(pool, active, depth);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(allocated).IsEqualTo(0);
        await Assert.That(active.Count).IsEqualTo(0);
    }

    [Test]
    public async Task PrimaryCanBeReusedWhileNestedLeaseRemainsActive()
    {
        var first = new ScopedPoolLease<Marker>(new Marker());
        var nested = new ScopedPoolLease<Marker>(new Marker());
        ScopedPoolLease<Marker> stale = first;
        Marker nestedValue = nested.Value;
        bool releasedFirst = first.TryRelease(out _);
        var replacement = new ScopedPoolLease<Marker>(new Marker());
        bool staleReleased = stale.TryRelease(out _);
        bool nestedStillOwned = ReferenceEquals(nested.Value, nestedValue);
        bool releasedReplacement = replacement.TryRelease(out _);
        bool releasedNested = nested.TryRelease(out _);

        await Assert.That(releasedFirst && releasedReplacement && releasedNested).IsTrue();
        await Assert.That(staleReleased).IsFalse();
        await Assert.That(nestedStillOwned).IsTrue();
    }

    [Test]
    public async Task NestedStateReuseKeepsPoolsAndThreadsIndependent()
    {
        using var firstPool = new ObjectPool<Marker, Policy>(maxCapacity: 64);
        using var secondPool = new ObjectPool<Marker, Policy>(maxCapacity: 64);
        var active = new System.Collections.Concurrent.ConcurrentDictionary<Marker, byte>();
        Task[] workers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                RentAcrossPools(firstPool, secondPool, active, 32);
            }
        })).ToArray();

        await Task.WhenAll(workers);
        await Assert.That(active.Count).IsEqualTo(0);
    }

    private static void RentAcrossPools(
        ObjectPool<Marker, Policy> firstPool,
        ObjectPool<Marker, Policy> secondPool,
        System.Collections.Concurrent.ConcurrentDictionary<Marker, byte> active,
        int depth)
    {
        using PooledLease<Marker, Policy> lease = firstPool.RentScoped();
        Marker item = lease.Value;
        if (!active.TryAdd(item, 0))
        {
            throw new InvalidOperationException("Two active leases own the same object.");
        }

        if (depth > 1)
        {
            RentAcrossPools(secondPool, firstPool, active, depth - 1);
        }

        if (!ReferenceEquals(lease.Value, item) || !active.TryRemove(item, out _))
        {
            throw new InvalidOperationException("Nested rentals invalidated an active lease.");
        }
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
        public void Destroy(Marker obj) { }
    }
}
