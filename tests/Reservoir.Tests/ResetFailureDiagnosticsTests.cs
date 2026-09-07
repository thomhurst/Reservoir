using System.Runtime.CompilerServices;

namespace Reservoir.Tests;

public class ResetFailureDiagnosticsTests
{
    [Test]
    [Arguments("manual", false)]
    [Arguments("manual", true)]
    [Arguments("manual-tls", false)]
    [Arguments("manual-tls", true)]
    [Arguments("scoped", false)]
    [Arguments("scoped", true)]
    [Arguments("runtime", false)]
    [Arguments("runtime", true)]
    [Arguments("runtime-scoped", false)]
    [Arguments("runtime-scoped", true)]
    public async Task FailedResetPreservesBothExceptionsAndDiscardsOnce(string rental, bool destroyThrows)
    {
        var resetFailure = new InvalidOperationException("reset failed");
        var destroyFailure = new ApplicationException("destroy failed");
        var policy = new ThrowingPolicy(resetFailure, destroyThrows ? destroyFailure : null);
        Item failed;
        Item replacement;
        Exception? caught = null;

        if (rental.StartsWith("runtime", StringComparison.Ordinal))
        {
            using var pool = new ObjectPool<Item>(policy, maxCapacity: 1);
            if (rental == "runtime-scoped")
            {
                var lease = pool.RentScoped();
                var copy = lease;
                failed = lease.Value;
                try
                {
                    lease.Dispose();
                }
                catch (Exception exception)
                {
                    caught = exception;
                }
                copy.Dispose();
            }
            else
            {
                failed = pool.Rent();
                try
                {
                    pool.Return(failed);
                }
                catch (Exception exception)
                {
                    caught = exception;
                }
            }
            replacement = pool.Rent();
            pool.Clear();
        }
        else
        {
            using var pool = new ObjectPool<Item, ThrowingPolicy>(policy, maxCapacity: 1,
                threadLocalFastPath: rental == "manual-tls");
            if (rental == "scoped")
            {
                var lease = pool.RentScoped();
                var copy = lease;
                failed = lease.Value;
                try
                {
                    lease.Dispose();
                }
                catch (Exception exception)
                {
                    caught = exception;
                }
                copy.Dispose();
            }
            else
            {
                failed = pool.Rent();
                try
                {
                    pool.Return(failed);
                }
                catch (Exception exception)
                {
                    caught = exception;
                }
            }
            replacement = pool.Rent();
            pool.Clear();
        }

        await Assert.That(failed.ResetCount).IsEqualTo(1);
        await Assert.That(failed.DestroyCount).IsEqualTo(1);
        await Assert.That(replacement).IsNotSameReferenceAs(failed);
        if (destroyThrows)
        {
            await Assert.That(caught).IsTypeOf<AggregateException>();
            var aggregate = (AggregateException)caught!;
            await Assert.That(aggregate.InnerExceptions).Count().IsEqualTo(2);
            await Assert.That(aggregate.InnerExceptions[0]).IsSameReferenceAs(resetFailure);
            await Assert.That(aggregate.InnerExceptions[1]).IsSameReferenceAs(destroyFailure);
            await Assert.That(destroyFailure.StackTrace!).Contains(nameof(ThrowingPolicy.Destroy));
        }
        else
        {
            await Assert.That(caught).IsSameReferenceAs(resetFailure);
        }
        await Assert.That(resetFailure.StackTrace!).Contains(nameof(ThrowingPolicy.TryReset));
    }

    private sealed class Item
    {
        internal int ResetCount;
        internal int DestroyCount;
    }

    private readonly struct ThrowingPolicy(Exception resetFailure, Exception? destroyFailure)
        : IPooledObjectDestroyPolicy<Item>
    {
        public Item Create() => new();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool TryReset(Item item)
        {
            item.ResetCount++;
            throw resetFailure;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Destroy(Item item)
        {
            item.DestroyCount++;
            if (destroyFailure is not null)
            {
                throw destroyFailure;
            }
        }
    }
}
