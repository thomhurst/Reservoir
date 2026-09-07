namespace Reservoir.Tests;

public class ConcurrentTestWorkersTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RenterFailureStopsAndJoinsClearerAndOtherRenters(bool cleanupThrows)
    {
        using var started = new Barrier(3);
        var original = new InvalidOperationException("Injected renter failure.");
        int active = 0;
        int exited = 0;

        void RunUntilCanceled(CancellationToken token)
        {
            Interlocked.Increment(ref active);
            try
            {
                started.SignalAndWait(token);
                while (!token.IsCancellationRequested)
                {
                    Thread.Yield();
                }
            }
            finally
            {
                Interlocked.Decrement(ref active);
                Interlocked.Increment(ref exited);
                if (cleanupThrows)
                {
                    throw new ApplicationException("Injected worker cleanup failure.");
                }
            }
        }

        Exception? observed = null;
        try
        {
            await ConcurrentTestWorkers.RunAsync(
            [
                token =>
                {
                    started.SignalAndWait(token);
                    throw original;
                },
                RunUntilCanceled
            ], RunUntilCanceled);
        }
        catch (Exception exception)
        {
            observed = exception;
        }

        await Assert.That(observed).IsSameReferenceAs(original);
        await Assert.That(original.StackTrace).IsNotNull();
        await Assert.That(active).IsEqualTo(0);
        await Assert.That(exited).IsEqualTo(2);
    }

    [Test]
    public async Task DeadlineCancelsAndJoinsBarrierWaiters()
    {
        using var barrier = new Barrier(2);
        int exited = 0;
        await Assert.That(() => ConcurrentTestWorkers.RunAsync(
        [
            token =>
            {
                try
                {
                    barrier.SignalAndWait(token);
                }
                finally
                {
                    Interlocked.Increment(ref exited);
                }
            }
        ], timeout: TimeSpan.FromMilliseconds(100))).Throws<TimeoutException>();

        await Assert.That(exited).IsEqualTo(1);
    }

    [Test]
    public async Task SuccessfulRentersStopAndJoinClearer()
    {
        using var started = new Barrier(2);
        int exited = 0;
        await ConcurrentTestWorkers.RunAsync(
            [token => started.SignalAndWait(token)],
            token =>
            {
                try
                {
                    started.SignalAndWait(token);
                    token.WaitHandle.WaitOne();
                }
                finally
                {
                    Interlocked.Increment(ref exited);
                }
            });

        await Assert.That(exited).IsEqualTo(1);
    }
}
