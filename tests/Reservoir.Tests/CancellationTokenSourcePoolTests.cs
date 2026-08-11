using System.Collections.Concurrent;

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
    public async Task ConcurrentStressPreservesOwnershipAndFreshState()
    {
        const int iterations = 10_000;
        int[] workerCounts = [1, 4, 16, 32];
        var pool = new CancellationTokenSourcePool(maxCapacity: 32);
        var state = new StressState();

        foreach (int workerCount in workerCounts)
        {
            using var start = new Barrier(workerCount + 1);
            Task[] workers = Enumerable.Range(0, workerCount)
                .Select(_ => Task.Factory.StartNew(
                    () => StressPool(pool, state, start, iterations),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default))
                .ToArray();

            start.SignalAndWait();
            await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(30));
        }

        await Assert.That(state.Failures).IsEmpty();
        await Assert.That(state.ActiveSources).IsEmpty();
    }

    [Test]
    public async Task ConcurrentTimerRaceStressNeverProducesCanceledRental()
    {
        const int workerCount = 16;
        const int iterations = 5_000;
        var pool = new CancellationTokenSourcePool(maxCapacity: 32);
        var state = new StressState();
        using var start = new Barrier(workerCount + 1);
        Task[] workers = Enumerable.Range(0, workerCount)
            .Select(_ => Task.Factory.StartNew(
                () => StressTimerRace(pool, state, start, iterations),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default))
            .ToArray();

        start.SignalAndWait();
        await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(30));

        await Assert.That(state.Failures).IsEmpty();
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

    [Test]
    public async Task ClearDisposesRetainedSourceAndLeavesPoolUsable()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        CancellationTokenSource retained = pool.Rent();
        _ = retained.Token.WaitHandle;
        pool.Return(retained);

        pool.Clear();
        CancellationTokenSource replacement = pool.Rent();

        await Assert.That(() => retained.Cancel()).Throws<ObjectDisposedException>();
        await Assert.That(replacement).IsNotSameReferenceAs(retained);

        pool.Return(replacement);
    }

    [Test]
    public async Task DisposeReleasesRetainedSourceAndClosesPool()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        CancellationTokenSource retained = pool.Rent();
        _ = retained.Token.WaitHandle;
        pool.Return(retained);

        pool.Dispose();

        await Assert.That(() => retained.Cancel()).Throws<ObjectDisposedException>();
        await Assert.That(() => pool.Rent()).Throws<ObjectDisposedException>();
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

    private static void StressPool(
        CancellationTokenSourcePool pool,
        StressState state,
        Barrier start,
        int iterations)
    {
        start.SignalAndWait();

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            CancellationTokenSource source = pool.Rent();
            state.TrackRental(source);

            if (source.IsCancellationRequested)
            {
                state.RecordFailure("Pool returned a canceled source.");
            }

            if ((iteration & 3) == 0)
            {
                _ = source.Token.Register(
                    static callbackState => ((StressState)callbackState!).RecordFailure(
                        "Callback survived a prior rental."),
                    state);
            }

            if ((iteration & 7) == 0)
            {
                source.CancelAfter(TimeSpan.FromMinutes(1));
            }

            Thread.SpinWait(8);
            state.CompleteRental(source);
            pool.Return(source);

            if ((iteration & 63) == 0)
            {
                CancellationTokenSource canceled = pool.Rent();
                state.TrackRental(canceled);

                if (canceled.IsCancellationRequested)
                {
                    state.RecordFailure("Pool returned a canceled source.");
                }

                canceled.Cancel();
                state.CompleteRental(canceled);
                pool.Return(canceled);
            }
        }
    }

    private static void StressTimerRace(
        CancellationTokenSourcePool pool,
        StressState state,
        Barrier start,
        int iterations)
    {
        start.SignalAndWait();

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            CancellationTokenSource source = pool.Rent();
            source.CancelAfter(TimeSpan.Zero);
            pool.Return(source);

            CancellationTokenSource next = pool.Rent();
            Thread.Yield();

            if (next.IsCancellationRequested)
            {
                state.RecordFailure("Timer race produced a canceled rental.");
            }

            pool.Return(next);
        }
    }

    private sealed class StressState
    {
        private const int MaximumRecordedFailures = 100;
        private int _failureCount;

        internal ConcurrentDictionary<CancellationTokenSource, byte> ActiveSources { get; }
            = new(ReferenceEqualityComparer.Instance);

        internal ConcurrentQueue<string> Failures { get; } = new();

        internal void TrackRental(CancellationTokenSource source)
        {
            if (!ActiveSources.TryAdd(source, 0))
            {
                RecordFailure("One source was rented concurrently.");
            }
        }

        internal void CompleteRental(CancellationTokenSource source)
        {
            if (!ActiveSources.TryRemove(source, out _))
            {
                RecordFailure("Rental ownership tracking was lost.");
            }
        }

        internal void RecordFailure(string message)
        {
            if (Interlocked.Increment(ref _failureCount) <= MaximumRecordedFailures)
            {
                Failures.Enqueue(message);
            }
        }
    }
}
