using Reservoir;

namespace Reservoir.Package.Tests;

public class ModernDestroyPolicyTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ModernDestructionContractsRemainAvailable(bool runtime)
    {
        await Assert.That(Discard<BasePolicy>(runtime)).IsEqualTo((1, 0));
        await Assert.That(Discard<ExplicitBasePolicy>(runtime)).IsEqualTo((1, 0));
        await Assert.That(Discard<ExplicitDerivedPolicy>(runtime)).IsEqualTo((1, 0));
        await Assert.That(Discard<ImplicitPolicy>(runtime)).IsEqualTo((1, 0));
        await Assert.That(Discard<DefaultBasePolicy>(runtime)).IsEqualTo((0, 1));
        await Assert.That(Discard<DefaultDerivedPolicy>(runtime)).IsEqualTo((0, 1));
    }

    private static (int DestroyCount, int DisposeCount) Discard<TPolicy>(bool runtime)
        where TPolicy : struct, IPooledObjectPolicy<Item>
    {
        Item item;
        if (runtime)
        {
            using var pool = new ObjectPool<Item>(default(TPolicy), 1);
            item = pool.Rent();
            pool.Return(item);
        }
        else
        {
            using var pool = new ObjectPool<Item, TPolicy>(default, 1);
            item = pool.Rent();
            pool.Return(item);
        }

        return (item.DestroyCount, item.DisposeCount);
    }

    public sealed class Item : IDisposable
    {
        public int DestroyCount;
        public int DisposeCount;
        public void Dispose() => DisposeCount++;
    }

    public readonly struct BasePolicy : IPooledObjectPolicy<Item>
    {
        public Item Create() => new();
        public bool TryReset(Item item) => false;
        void IPooledObjectPolicy<Item>.Destroy(Item item) => item.DestroyCount++;
    }

    public readonly struct ExplicitBasePolicy : IPooledObjectDestroyPolicy<Item>
    {
        public Item Create() => new();
        public bool TryReset(Item item) => false;
        void IPooledObjectPolicy<Item>.Destroy(Item item) => item.DestroyCount++;
    }

    public readonly struct ExplicitDerivedPolicy : IPooledObjectDestroyPolicy<Item>
    {
        public Item Create() => new();
        public bool TryReset(Item item) => false;
        void IPooledObjectDestroyPolicy<Item>.Destroy(Item item) => item.DestroyCount++;
    }

    public readonly struct ImplicitPolicy : IPooledObjectDestroyPolicy<Item>
    {
        public Item Create() => new();
        public bool TryReset(Item item) => false;
        public void Destroy(Item item) => item.DestroyCount++;
    }

    public readonly struct DefaultBasePolicy : IPooledObjectPolicy<Item>
    {
        public Item Create() => new();
        public bool TryReset(Item item) => false;
    }

    public readonly struct DefaultDerivedPolicy : IPooledObjectDestroyPolicy<Item>
    {
        public Item Create() => new();
        public bool TryReset(Item item) => false;
    }
}
