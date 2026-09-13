using System.Reflection;

namespace Reservoir.Tests;

public class StripedObjectStoreStaleHintTests
{
    [Test]
    [Arguments(2, false)]
    [Arguments(2, true)]
    [Arguments(20, false)]
    [Arguments(20, true)]
    public async Task StaleHintsPreserveEveryStripeAndItemOwnership(int stripeCount, bool lastHome)
    {
        FieldInfo threadStripe = typeof(StripedObjectStore<HintItem>)
            .GetField("_threadStripe", BindingFlags.NonPublic | BindingFlags.Static)!;
        FieldInfo lastRentStripe = typeof(StripedObjectStore<HintItem>)
            .GetField("_lastRentStripe", BindingFlags.NonPublic | BindingFlags.Static)!;
        int previousOrdinal = (int)threadStripe.GetValue(null)!;
        int previousHint = (int)lastRentStripe.GetValue(null)!;
        int home = lastHome ? stripeCount - 1 : 0;
        bool preserved = true;

        // Keep all thread-static setup and restoration synchronous, before any assertion await.
        try
        {
            foreach (int hint in new[] { 0, home + 1, stripeCount - home, stripeCount + 1 })
            {
                for (int target = 0; target < stripeCount; target++)
                {
                    var store = new StripedObjectStore<HintItem>(stripeCount * 16, stripeCount);
                    var expected = new HintItem();
                    threadStripe.SetValue(null, target + 1);
                    preserved &= store.TryPush(expected);
                    threadStripe.SetValue(null, home + 1);
                    lastRentStripe.SetValue(null, hint);

                    preserved &= store.TryPop(out HintItem? actual)
                        && ReferenceEquals(actual, expected);
                    preserved &= !store.TryPop(out HintItem? empty) && empty is null;
                    preserved &= store.TryPush(expected);
                    preserved &= store.TryPop(out actual) && ReferenceEquals(actual, expected);
                    preserved &= !store.TryPop(out empty) && empty is null;
                }
            }
        }
        finally
        {
            threadStripe.SetValue(null, previousOrdinal);
            lastRentStripe.SetValue(null, previousHint);
        }

        await Assert.That(preserved).IsTrue();
    }

    private sealed class HintItem;
}
