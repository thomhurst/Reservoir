namespace Reservoir.Tests;

public class FactoryLifecycleTests
{
    [Test]
    [Arguments("manual")]
    [Arguments("scoped")]
    [Arguments("shared")]
    public async Task FactoryReturnsAfterDisposalDestroyOnce(string rental)
    {
        var pool = new ObjectPool<Item>(() => new Item(), maxCapacity: 1);
        Item item;
        if (rental == "manual")
        {
            item = pool.Rent();
            pool.Dispose();
            pool.Return(item);
        }
        else if (rental == "scoped")
        {
            var lease = pool.RentScoped(out item);
            var copy = lease;
            pool.Dispose();
            lease.Dispose();
            copy.Dispose();
        }
        else
        {
            var lease = pool.RentScopedShared(out item);
            var copy = lease;
            pool.Dispose();
            lease.Dispose();
            copy.Dispose();
        }

        await Assert.That(item.DestroyCount).IsEqualTo(1);
    }

    [Test]
    public async Task FactoryReturnStillDestroysExcessAndClearDestroysRetained()
    {
        using var pool = new ObjectPool<Item>(() => new Item(), maxCapacity: 1);
        Item retained = pool.Rent();
        Item excess = pool.Rent();
        pool.Return(retained);
        pool.Return(excess);
        int beforeClear = retained.DestroyCount;
        pool.Clear();

        await Assert.That(beforeClear).IsEqualTo(0);
        await Assert.That(retained.DestroyCount).IsEqualTo(1);
        await Assert.That(excess.DestroyCount).IsEqualTo(1);
    }

    private sealed class Item : IDisposable
    {
        internal int DestroyCount;

        public void Dispose() => DestroyCount++;
    }
}
