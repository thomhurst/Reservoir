namespace Reservoir.Tests;

public class RuntimeNonThrowingPolicyTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MarkedRuntimePolicyResetsAndRejects(bool boxedStruct)
    {
        IPooledObjectPolicy<Item> policy = boxedStruct ? new ValuePolicy() : new ReferencePolicy();
        using var pool = new ObjectPool<Item>(policy, maxCapacity: 1);
        Item item = pool.Rent();
        pool.Return(item);
        Item reused = pool.Rent();
        reused.Reject = true;
        pool.Return(reused);
        Item replacement = pool.Rent();

        await Assert.That(reused).IsSameReferenceAs(item);
        await Assert.That(reused.ResetSequence).IsEqualTo(2);
        await Assert.That(reused.DestroyCount).IsEqualTo(1);
        await Assert.That(replacement).IsNotSameReferenceAs(item);
        await Assert.That(replacement.PriorResets).IsEqualTo(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MarkedScopedReturnKeepsStaleCopiesInert(bool shared)
    {
        using var pool = new ObjectPool<Item>(new ReferencePolicy(), maxCapacity: 1);
        Item first;
        Item current;
        if (shared)
        {
            SharedPooledLease<Item> lease = pool.RentScopedShared(out first);
            SharedPooledLease<Item> stale = lease;
            lease.Dispose();
            lease.Dispose();
            SharedPooledLease<Item> next = pool.RentScopedShared(out current);
            stale.Dispose();
            next.Dispose();
        }
        else
        {
            PooledLease<Item> lease = pool.RentScoped(out first);
            PooledLease<Item> stale = lease;
            lease.Dispose();
            lease.Dispose();
            PooledLease<Item> next = pool.RentScoped(out current);
            stale.Dispose();
            next.Dispose();
        }

        await Assert.That(current).IsSameReferenceAs(first);
        await Assert.That(current.ResetSequence).IsEqualTo(2);
        await Assert.That(current.DestroyCount).IsEqualTo(0);
    }

    [Test]
    public async Task DisposalDuringMarkedResetDestroysExactlyOnce()
    {
        using var entered = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        var policy = new ReferencePolicy
        {
            DuringReset = () =>
            {
                entered.Set();
                resume.Wait(TimeSpan.FromSeconds(10));
            }
        };
        using var pool = new ObjectPool<Item>(policy, maxCapacity: 1);
        Item item = pool.Rent();
        Task returning = Task.Run(() => pool.Return(item));
        bool reachedReset;
        try
        {
            reachedReset = entered.Wait(TimeSpan.FromSeconds(10));
            pool.Dispose();
        }
        finally
        {
            resume.Set();
        }

        await returning.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(reachedReset).IsTrue();
        await Assert.That(item.ResetSequence).IsEqualTo(1);
        await Assert.That(item.DestroyCount).IsEqualTo(1);
    }

    [Test]
    public async Task RuntimeMarkerUsesDocumentedThrowContractOnModernAsset()
    {
        using var pool = new ObjectPool<NonThrowingResetPolicyTests.Item>(
            new NonThrowingResetPolicyTests.MarkedPolicy(), maxCapacity: 1);
        NonThrowingResetPolicyTests.Item item = pool.Rent();
        item.ThrowOnReset = true;

        await Assert.That(() => pool.Return(item)).Throws<InvalidOperationException>();
#if NET10_0_OR_GREATER
        await Assert.That(item.Destroyed).IsFalse();
#else
        // Older assets keep their existing guarded adapter behavior.
        await Assert.That(item.Destroyed).IsTrue();
#endif
    }

    private sealed class Item(int priorResets)
    {
        internal readonly int PriorResets = priorResets;
        internal int ResetSequence;
        internal int DestroyCount;
        internal bool Reject;
    }

    private sealed class ReferencePolicy : IPooledObjectDestroyPolicy<Item>, INonThrowingResetPolicy
    {
        private int _resets;
        internal Action? DuringReset;

        public Item Create() => new(_resets);
        public bool TryReset(Item item)
        {
            item.ResetSequence = ++_resets;
            DuringReset?.Invoke();
            return !item.Reject;
        }
        public void Destroy(Item item) => item.DestroyCount++;
    }

    private struct ValuePolicy : IPooledObjectDestroyPolicy<Item>, INonThrowingResetPolicy
    {
        private int _resets;
        public Item Create() => new(_resets);
        public bool TryReset(Item item)
        {
            item.ResetSequence = ++_resets;
            return !item.Reject;
        }
        public void Destroy(Item item) => item.DestroyCount++;
    }
}
