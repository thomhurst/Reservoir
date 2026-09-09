namespace Reservoir.Tests;

public class PolicyResetStateTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ResetAndFailureCleanupPreserveMutablePolicyState(bool runtimePolicy)
    {
        using var specialized = new ObjectPool<Item, Policy>(maxCapacity: 1);
        using var runtime = new ObjectPool<Item>(new Policy(), maxCapacity: 1);
        Func<Item> rent = runtimePolicy ? runtime.Rent : specialized.Rent;
        Action<Item> giveBack = runtimePolicy ? runtime.Return : specialized.Return;

        Item item = rent();
        giveBack(item);
        Item reused = rent();
        reused.FailReset = true;
        Exception? failure = null;
        try
        {
            giveBack(reused);
        }
        catch (InvalidOperationException exception)
        {
            failure = exception;
        }

        Item next = rent();
        giveBack(next);
        await Assert.That(reused).IsSameReferenceAs(item);
        await Assert.That(failure).IsNotNull();
        await Assert.That(next).IsNotSameReferenceAs(item);
        await Assert.That(next.PriorResets).IsEqualTo(2);
        await Assert.That(next.PriorDestructions).IsEqualTo(1);
    }

    private sealed class Item(int priorResets, int priorDestructions)
    {
        internal readonly int PriorResets = priorResets;
        internal readonly int PriorDestructions = priorDestructions;
        internal bool FailReset;
    }

    private struct Policy : IPooledObjectPolicy<Item>
    {
        private int _resets;
        private int _destructions;

        public Item Create() => new(_resets, _destructions);

        public bool TryReset(Item item)
        {
            _resets++;
            if (item.FailReset)
            {
                throw new InvalidOperationException("Reset failed after updating policy state.");
            }

            return true;
        }

        public void Destroy(Item item) => _destructions++;
    }
}
