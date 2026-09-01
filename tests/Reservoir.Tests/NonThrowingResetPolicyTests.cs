namespace Reservoir.Tests;

public class NonThrowingResetPolicyTests
{
    [Test]
    public async Task MarkedPolicyRoundTripsThroughThePool()
    {
        var pool = new ObjectPool<Item, MarkedPolicy>(maxCapacity: 4);
        Item expected = pool.Rent();

        pool.Return(expected);
        Item actual = pool.Rent();

        await Assert.That(actual).IsSameReferenceAs(expected);
        await Assert.That(actual.ResetCount).IsEqualTo(1);
    }

    [Test]
    public async Task MarkedPolicyDiscardsWhenResetReturnsFalse()
    {
        var pool = new ObjectPool<Item, MarkedPolicy>(maxCapacity: 4);
        Item rejected = pool.Rent();
        rejected.RejectReset = true;

        pool.Return(rejected);
        Item actual = pool.Rent();

        await Assert.That(actual).IsNotSameReferenceAs(rejected);
        await Assert.That(rejected.Destroyed).IsTrue();
    }

    [Test]
    public async Task MarkedPolicyThrowPropagatesWithoutDestroy()
    {
        var pool = new ObjectPool<Item, MarkedPolicy>(maxCapacity: 4);
        Item item = pool.Rent();
        item.ThrowOnReset = true;

        await Assert.That(() => pool.Return(item)).Throws<InvalidOperationException>();
        await Assert.That(item.Destroyed).IsFalse();
    }

    [Test]
    public async Task UnmarkedPolicyThrowStillDestroys()
    {
        var pool = new ObjectPool<Item, UnmarkedPolicy>(maxCapacity: 4);
        Item item = pool.Rent();
        item.ThrowOnReset = true;

        await Assert.That(() => pool.Return(item)).Throws<InvalidOperationException>();
        await Assert.That(item.Destroyed).IsTrue();
    }

    public sealed class Item
    {
        public int ResetCount { get; set; }

        public bool RejectReset { get; set; }

        public bool ThrowOnReset { get; set; }

        public bool Destroyed { get; set; }
    }

    public readonly struct MarkedPolicy
        : IPooledObjectDestroyPolicy<Item>, INonThrowingResetPolicy
    {
        public Item Create() => new();

        public bool TryReset(Item obj)
        {
            if (obj.ThrowOnReset)
            {
                throw new InvalidOperationException("reset failed");
            }

            obj.ResetCount++;
            return !obj.RejectReset;
        }

        public void Destroy(Item obj) => obj.Destroyed = true;
    }

    public readonly struct UnmarkedPolicy : IPooledObjectDestroyPolicy<Item>
    {
        public Item Create() => new();

        public bool TryReset(Item obj)
        {
            if (obj.ThrowOnReset)
            {
                throw new InvalidOperationException("reset failed");
            }

            obj.ResetCount++;
            return true;
        }

        public void Destroy(Item obj) => obj.Destroyed = true;
    }
}
