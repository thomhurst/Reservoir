namespace Reservoir.Tests;

public class NullCreationTests
{
    [Test]
    [Arguments(32)]
    [Arguments(65)]
    public async Task GenericNullCreationPreservesFailureAndAllowsRecovery(int capacity)
    {
        for (int mode = 0; mode < 3; mode++)
        {
            var state = new CreationState();
            using var pool = new ObjectPool<object, Policy>(new Policy(state), capacity);
            Action rent = () =>
            {
                if (mode == 0)
                {
                    pool.Return(pool.Rent());
                }
                else if (mode == 1)
                {
                    using PooledLease<object, Policy> lease = pool.RentScoped();
                }
                else
                {
                    using SharedPooledLease<object, Policy> lease = pool.RentScopedShared();
                }
            };

            await AssertFailureAndRecovery(rent, state);
        }
    }

    [Test]
    [Arguments(32)]
    [Arguments(65)]
    public async Task FactoryNullCreationPreservesFailureAndAllowsRecovery(int capacity)
    {
        for (int mode = 0; mode < 3; mode++)
        {
            var state = new CreationState();
            using var pool = new ObjectPool<object>(state.Create, capacity);
            Action rent = () =>
            {
                if (mode == 0)
                {
                    pool.Return(pool.Rent());
                }
                else if (mode == 1)
                {
                    using PooledLease<object> lease = pool.RentScoped();
                }
                else
                {
                    using SharedPooledLease<object> lease = pool.RentScopedShared();
                }
            };

            await AssertFailureAndRecovery(rent, state);
        }
    }

    private static async Task AssertFailureAndRecovery(Action rent, CreationState state)
    {
        Exception? exception = null;
        try
        {
            rent();
        }
        catch (Exception caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(exception!.Message).IsEqualTo("The pool policy returned null from Create().");
        rent();
        rent();
        await Assert.That(state.Created).IsEqualTo(2);
    }

    private sealed class CreationState
    {
        internal int Created;

        internal object Create() => ++Created == 1 ? null! : new object();
    }

    private readonly struct Policy(CreationState state) : IPooledObjectPolicy<object>
    {
        public object Create() => state.Create();
        public bool TryReset(object value) => true;
        public void Destroy(object value) { }
    }
}
