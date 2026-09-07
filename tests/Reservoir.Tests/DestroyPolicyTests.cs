using System.Reflection;

namespace Reservoir.Tests;

public class DestroyPolicyTests
{
    [Test]
    public async Task DestructionHasOneDeclaringInterface()
    {
        MethodInfo? destroy = typeof(IPooledObjectPolicy<Item>).GetMethod(nameof(IPooledObjectPolicy<Item>.Destroy));
        await Assert.That(destroy?.DeclaringType).IsEqualTo(typeof(IPooledObjectPolicy<Item>));
        await Assert.That(typeof(IPooledObjectDestroyPolicy<Item>).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)).IsEmpty();
    }

    [Test]
    public async Task MarkerConstraintMutatesCallerOwnedPolicyByReference()
    {
        var policy = new MarkerPolicy();
        var item = new Item();
        DestroyConstrained(ref policy, item);
        DestroyConstrained(ref policy, item);

        await Assert.That(policy.DestroyCount).IsEqualTo(2);
        await Assert.That(item.DestroyCount).IsEqualTo(2);
        await Assert.That(item.DisposeCount).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BaseExplicitDestructionSharesPoolPolicyState(bool runtime)
    {
        var policy = new BasePolicy();
        Item first;
        Item second;
        if (runtime)
        {
            using var pool = new ObjectPool<Item>(policy, 1);
            first = pool.Rent();
            pool.Return(first);
            second = pool.Rent();
            pool.Return(second);
            await Assert.That(second.DestroyCount).IsEqualTo(0);
            pool.Clear();
        }
        else
        {
            using var pool = new ObjectPool<Item, BasePolicy>(policy, 1);
            first = pool.Rent();
            pool.Return(first);
            second = pool.Rent();
            pool.Return(second);
            await Assert.That(second.DestroyCount).IsEqualTo(0);
            pool.Clear();
        }

        await Assert.That(first.DestroyCount).IsEqualTo(1);
        await Assert.That(second.DestroyCount).IsEqualTo(2);
        await Assert.That(first.DisposeCount + second.DisposeCount).IsEqualTo(0);
        await Assert.That(policy.DestroyCount).IsEqualTo(0);
    }

    private static void DestroyConstrained<TPolicy>(ref TPolicy policy, Item item)
        where TPolicy : struct, IPooledObjectDestroyPolicy<Item> => policy.Destroy(item);

    private sealed class Item : IDisposable
    {
        internal int DestroyCount;
        internal int DisposeCount;
        public void Dispose() => DisposeCount++;
    }

    private struct MarkerPolicy : IPooledObjectDestroyPolicy<Item>
    {
        internal int DestroyCount;
        public Item Create() => new();
        public bool TryReset(Item item) => false;
        void IPooledObjectPolicy<Item>.Destroy(Item item) => item.DestroyCount = ++DestroyCount;
    }

    private struct BasePolicy : IPooledObjectPolicy<Item>
    {
        internal int DestroyCount;
        public Item Create() => new();
        public bool TryReset(Item item) => DestroyCount > 0;
        void IPooledObjectPolicy<Item>.Destroy(Item item) => item.DestroyCount = ++DestroyCount;
    }
}
