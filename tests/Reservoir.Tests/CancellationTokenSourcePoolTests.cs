namespace Reservoir.Tests;

public class CancellationTokenSourcePoolTests
{
    [Test]
    public async Task UnfiredSourceIsResetAndReused()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        CancellationTokenSource expected = pool.Rent();
        expected.CancelAfter(TimeSpan.FromMilliseconds(20));

        pool.Return(expected);
        CancellationTokenSource actual = pool.Rent();
        await Task.Delay(TimeSpan.FromMilliseconds(50));

        await Assert.That(actual).IsSameReferenceAs(expected);
        await Assert.That(actual.IsCancellationRequested).IsFalse();

        pool.Return(actual);
    }

    [Test]
    public async Task CanceledSourceIsDiscarded()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        CancellationTokenSource canceled = pool.Rent();
        canceled.Cancel();

        pool.Return(canceled);
        CancellationTokenSource replacement = pool.Rent();

        await Assert.That(replacement).IsNotSameReferenceAs(canceled);
        await Assert.That(replacement.IsCancellationRequested).IsFalse();

        pool.Return(replacement);
    }

    [Test]
    public async Task TimerFiredSourceIsDiscarded()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        CancellationTokenSource fired = pool.Rent();
        fired.CancelAfter(TimeSpan.Zero);
        await WaitForCancellation(fired.Token);

        pool.Return(fired);
        CancellationTokenSource replacement = pool.Rent();

        await Assert.That(replacement).IsNotSameReferenceAs(fired);
        await Assert.That(replacement.IsCancellationRequested).IsFalse();

        pool.Return(replacement);
    }

    [Test]
    public async Task TimerRacingWithReturnNeverProducesCanceledRental()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);

        for (int i = 0; i < 100; i++)
        {
            CancellationTokenSource source = pool.Rent();
            source.CancelAfter(TimeSpan.Zero);
            pool.Return(source);

            CancellationTokenSource next = pool.Rent();
            await Task.Yield();
            await Assert.That(next.IsCancellationRequested).IsFalse();
            pool.Return(next);
        }
    }

    [Test]
    public async Task PreviousRentalCallbacksAreUnregistered()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        CancellationTokenSource source = pool.Rent();
        int callbackCount = 0;
        _ = source.Token.Register(() => callbackCount++);

        pool.Return(source);
        CancellationTokenSource reused = pool.Rent();
        reused.Cancel();

        await Assert.That(reused).IsSameReferenceAs(source);
        await Assert.That(callbackCount).IsEqualTo(0);

        pool.Return(reused);
    }

    [Test]
    public async Task ReusedSourceCanArmNewTimer()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        CancellationTokenSource source = pool.Rent();
        pool.Return(source);

        CancellationTokenSource reused = pool.Rent();
        reused.CancelAfter(TimeSpan.Zero);
        await WaitForCancellation(reused.Token);

        await Assert.That(reused).IsSameReferenceAs(source);
        await Assert.That(reused.IsCancellationRequested).IsTrue();

        pool.Return(reused);
    }

    [Test]
    public async Task ScopedLeaseReturnsSource()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        CancellationTokenSource expected;
        bool valuesMatch;

        {
            using CancellationTokenSourcePool.Lease lease = pool.RentScoped(out expected);
            valuesMatch = ReferenceEquals(lease.Value, expected);
        }

        CancellationTokenSource actual = pool.Rent();
        await Assert.That(valuesMatch).IsTrue();
        await Assert.That(actual).IsSameReferenceAs(expected);
        pool.Return(actual);
    }

#if !DEBUG && !RESERVOIR_DIAGNOSTICS
    [Test]
    public async Task WarmRentAndReturnAllocatesNothing()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        CancellationTokenSource warm = pool.Rent();
        pool.Return(warm);

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 1_000; i++)
        {
            CancellationTokenSource source = pool.Rent();
            pool.Return(source);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0);
    }
#endif

    [Test]
    public async Task InvalidCapacityThrows()
    {
        await Assert.That(() => new CancellationTokenSourcePool(0))
            .Throws<ArgumentOutOfRangeException>();
    }

    private static async Task WaitForCancellation(CancellationToken token)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token)
                .WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
        }
    }
}
