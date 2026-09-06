using System.Reflection;

namespace Reservoir.Tests;

public class StripedObjectStoreHintTests
{
    [Test]
    [Arguments(1)]
    [Arguments(3)]
    [Arguments(8)]
    [Arguments(20)]
    public async Task RemoteReuseAcrossDifferentStoreSizesPreservesOwnership(int stripeCount)
    {
        bool correct = await OnDedicatedThread(() =>
        {
            var large = new StripedObjectStore<Item>(512, 32);
            var smaller = new StripedObjectStore<Item>(stripeCount * 8, stripeCount);
            var first = new Item();
            var second = new Item();
            var third = new Item();

            SetThreadOrdinal(32);
            bool pushed = large.TryPush(first) && large.TryPush(third);
            SetThreadOrdinal(1);
            bool firstPopped = large.TryPop(out Item? actualFirst);

            // The previous remote hit was stripe 31, outside the smaller store's range. Hints
            // belong to the closed item type, so using another store must validate that index.
            SetThreadOrdinal(stripeCount);
            pushed &= smaller.TryPush(second);
            SetThreadOrdinal(1);
            bool secondPopped = smaller.TryPop(out Item? actualSecond);
            bool smallerEmpty = !smaller.TryPop(out Item? empty) && empty is null;
            bool thirdPopped = large.TryPop(out Item? actualThird);

            return pushed && firstPopped && secondPopped && thirdPopped && smallerEmpty
                && ReferenceEquals(actualFirst, first)
                && ReferenceEquals(actualSecond, second)
                && ReferenceEquals(actualThird, third)
                && !large.TryPop(out _);
        });

        await Assert.That(correct).IsTrue();
    }

    [Test]
    public async Task EmptyRemoteHintFallsBackToWrappedScan()
    {
        bool correct = await OnDedicatedThread(() =>
        {
            var store = new StripedObjectStore<Item>(128, 8);
            var first = new Item();
            var second = new Item();

            SetThreadOrdinal(7);
            bool pushed = store.TryPush(first);
            SetThreadOrdinal(1);
            bool firstPopped = store.TryPop(out Item? actualFirst);

            SetThreadOrdinal(3);
            pushed &= store.TryPush(second);
            SetThreadOrdinal(8);
            // Stripe 6 is now empty; finding stripe 2 from home stripe 7 requires wrapping.
            bool secondPopped = store.TryPop(out Item? actualSecond);
            return pushed && firstPopped && secondPopped
                && ReferenceEquals(actualFirst, first)
                && ReferenceEquals(actualSecond, second)
                && !store.TryPop(out _);
        });

        await Assert.That(correct).IsTrue();
    }

    [Test]
    public async Task HomeStripeRemainsPreferredAfterRemoteReuse()
    {
        bool correct = await OnDedicatedThread(() =>
        {
            var store = new StripedObjectStore<Item>(128, 8);
            var remoteFast = new Item();
            var remoteNode = new Item();
            var local = new Item();

            SetThreadOrdinal(7);
            bool pushed = store.TryPush(remoteFast) && store.TryPush(remoteNode);
            SetThreadOrdinal(1);
            bool firstPopped = store.TryPop(out Item? first);
            pushed &= store.TryPush(local);
            bool localPopped = store.TryPop(out Item? second);
            bool nodePopped = store.TryPop(out Item? third);

            return pushed && firstPopped && localPopped && nodePopped
                && ReferenceEquals(first, remoteFast)
                && ReferenceEquals(second, local)
                && ReferenceEquals(third, remoteNode)
                && !store.TryPop(out _);
        });

        await Assert.That(correct).IsTrue();
    }

    // No awaits inside these actions: ordinal and hint state must stay on the same thread.
    private static Task<bool> OnDedicatedThread(Func<bool> action)
        => Task.Factory.StartNew(
            action,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    private static void SetThreadOrdinal(int ordinal)
        => typeof(StripedObjectStore<Item>)
            .GetField("_threadStripe", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, ordinal);

    private sealed class Item;
}
