using System.Collections.Concurrent;

namespace Reservoir.Tests;

public class CancellationTokenSourcePoolTests
{
    [Test]
    public async Task UnfiredSourceIsResetAndReused()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        CancellationTokenSource expected = pool.Rent();
        expected.CancelAfter(TimeSpan.FromMilliseconds(250));

        expected.Dispose();
        CancellationTokenSource actual = pool.Rent();
        await Task.Delay(TimeSpan.FromMilliseconds(750));

        await Assert.That(actual).IsSameReferenceAs(expected);
        await Assert.That(actual.IsCancellationRequested).IsFalse();

        actual.Dispose();
    }

    [Test]
    public async Task CanceledSourceIsDiscarded()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        CancellationTokenSource canceled = pool.Rent();
        canceled.Cancel();

        canceled.Dispose();
        CancellationTokenSource replacement = pool.Rent();

        await Assert.That(replacement).IsNotSameReferenceAs(canceled);
        await Assert.That(replacement.IsCancellationRequested).IsFalse();
        await Assert.That(() => canceled.Cancel()).Throws<ObjectDisposedException>();

        replacement.Dispose();
    }

    [Test]
    public async Task TimerFiredSourceIsDiscarded()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        CancellationTokenSource fired = pool.Rent();
        fired.CancelAfter(TimeSpan.Zero);
        await WaitForCancellation(fired.Token);

        fired.Dispose();
        CancellationTokenSource replacement = pool.Rent();

        await Assert.That(replacement).IsNotSameReferenceAs(fired);
        await Assert.That(replacement.IsCancellationRequested).IsFalse();

        replacement.Dispose();
    }

    [Test]
    public async Task UpstreamCancellationCancelsLinkedRental()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        using var upstream = new CancellationTokenSource();
        CancellationTokenSource source = pool.RentLinked(upstream.Token);

        upstream.Cancel();

        await Assert.That(source.IsCancellationRequested).IsTrue();
        source.Dispose();
    }

    [Test]
    public async Task AlreadyCanceledUpstreamProducesCanceledRental()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        using var upstream = new CancellationTokenSource();
        upstream.Cancel();

        CancellationTokenSource canceled = pool.RentLinked(upstream.Token);
        canceled.Dispose();
        CancellationTokenSource replacement = pool.Rent();

        await Assert.That(canceled.IsCancellationRequested).IsTrue();
        await Assert.That(replacement).IsNotSameReferenceAs(canceled);
        await Assert.That(() => canceled.Cancel()).Throws<ObjectDisposedException>();

        replacement.Dispose();
    }

    [Test]
    public async Task OldUpstreamCannotCancelReusedSource()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        using var upstream = new CancellationTokenSource();
        CancellationTokenSource linked = pool.RentLinked(upstream.Token);

        linked.Dispose();
        CancellationTokenSource reused = pool.Rent();
        upstream.Cancel();

        await Assert.That(reused).IsSameReferenceAs(linked);
        await Assert.That(reused.IsCancellationRequested).IsFalse();

        reused.Dispose();
    }

    [Test]
    public async Task NonCancelableUpstreamUsesNormalRentalPath()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        CancellationTokenSource expected = pool.Rent();
        expected.Dispose();

        CancellationTokenSource actual = pool.RentLinked(CancellationToken.None);

        await Assert.That(actual).IsSameReferenceAs(expected);
        actual.Dispose();
    }

    [Test]
    public async Task LinkedAndUnlinkedRentalsReuseSameSource()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        using var firstUpstream = new CancellationTokenSource();
        using var secondUpstream = new CancellationTokenSource();
        CancellationTokenSource first = pool.RentLinked(firstUpstream.Token);
        first.Dispose();

        CancellationTokenSource second = pool.Rent();
        second.Dispose();
        CancellationTokenSource third = pool.RentLinked(secondUpstream.Token);

        await Assert.That(second).IsSameReferenceAs(first);
        await Assert.That(third).IsSameReferenceAs(first);

        third.Dispose();
    }

    [Test]
    public async Task LinkedDisposalWaitsForInFlightUpstreamCallback()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        using var upstream = new CancellationTokenSource();
        using var callbackEntered = new ManualResetEventSlim();
        using var releaseCallback = new ManualResetEventSlim();
        using var disposeStarted = new ManualResetEventSlim();
        CancellationTokenSource source = pool.RentLinked(upstream.Token);
        _ = source.Token.Register(
            static state =>
            {
                var signals = ((ManualResetEventSlim Entered, ManualResetEventSlim Release))state!;
                signals.Entered.Set();
                signals.Release.Wait();
            },
            (callbackEntered, releaseCallback));

        Task cancelTask = Task.Run(upstream.Cancel);
        bool callbackWasEntered = callbackEntered.Wait(TimeSpan.FromSeconds(5));
        Task disposeTask = Task.Run(() =>
        {
            disposeStarted.Set();
            source.Dispose();
        });
        bool disposalWasStarted = disposeStarted.Wait(TimeSpan.FromSeconds(5));

        try
        {
            await Assert.That(callbackWasEntered).IsTrue();
            await Assert.That(disposalWasStarted).IsTrue();
            await Assert.That(disposeTask.IsCompleted).IsFalse();
        }
        finally
        {
            releaseCallback.Set();
        }

        await Task.WhenAll(cancelTask, disposeTask).WaitAsync(TimeSpan.FromSeconds(5));

        CancellationTokenSource replacement = pool.Rent();
        await Assert.That(replacement).IsNotSameReferenceAs(source);
        replacement.Dispose();
    }

    [Test]
    public async Task OutstandingLinkedRentalIsDisposedWhenPoolIsClosed()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        using var upstream = new CancellationTokenSource();
        CancellationTokenSource source = pool.RentLinked(upstream.Token);

        pool.Dispose();
        source.Dispose();
        upstream.Cancel();

        await Assert.That(() => source.Cancel()).Throws<ObjectDisposedException>();
        await Assert.That(() => pool.Rent()).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task ConcurrentStressPreservesOwnershipAndFreshState()
    {
        const int iterations = 10_000;
        int[] workerCounts = [1, 4, 8, 16, 32];
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
    public async Task ConcurrentTimerDisarmStressNeverProducesCanceledRental()
    {
        const int workerCount = 16;
        const int iterations = 5_000;
        var pool = new CancellationTokenSourcePool(maxCapacity: 32);
        var state = new StressState();
        using var start = new Barrier(workerCount + 1);
        Task[] workers = Enumerable.Range(0, workerCount)
            .Select(_ => Task.Factory.StartNew(
                () => StressTimerDisarm(pool, state, start, iterations),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default))
            .ToArray();

        start.SignalAndWait();
        await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(30));

        await Assert.That(state.Failures).IsEmpty();
    }

    [Test]
    public async Task ConcurrentScopedStressPreservesOwnershipAndFreshState()
    {
        const int workerCount = 8;
        const int iterations = 5_000;
        using var pool = new CancellationTokenSourcePool(maxCapacity: workerCount);
        var state = new StressState();
        using var start = new Barrier(workerCount + 1);
        Task[] workers = Enumerable.Range(0, workerCount)
            .Select(_ => Task.Factory.StartNew(
                () => StressScopedPool(pool, state, start, iterations),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default))
            .ToArray();

        start.SignalAndWait();
        await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(30));

        await Assert.That(state.Failures).IsEmpty();
        await Assert.That(state.ActiveSources).IsEmpty();
    }

    [Test]
    public async Task PreviousRentalCallbacksAreUnregistered()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        CancellationTokenSource source = pool.Rent();
        int callbackCount = 0;
        _ = source.Token.Register(() => callbackCount++);

        source.Dispose();
        CancellationTokenSource reused = pool.Rent();
        reused.Cancel();

        await Assert.That(reused).IsSameReferenceAs(source);
        await Assert.That(callbackCount).IsEqualTo(0);

        reused.Dispose();
    }

    [Test]
    public async Task ReusedSourceCanArmNewTimer()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        CancellationTokenSource source = pool.Rent();
        source.Dispose();

        CancellationTokenSource reused = pool.Rent();
        reused.CancelAfter(TimeSpan.Zero);
        await WaitForCancellation(reused.Token);

        await Assert.That(reused).IsSameReferenceAs(source);
        await Assert.That(reused.IsCancellationRequested).IsTrue();

        reused.Dispose();
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

        bool sourceWasReused;
        {
            using CancellationTokenSourcePool.Lease lease = pool.RentScoped();
            sourceWasReused = ReferenceEquals(lease.Value, expected);
        }

        await Assert.That(valuesMatch).IsTrue();
        await Assert.That(sourceWasReused).IsTrue();
    }

    [Test]
    public async Task StaleScopedLeaseCopyCannotReturnLaterRental()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 2);
        CancellationTokenSourcePool.Lease first = pool.RentScoped();
        CancellationTokenSourcePool.Lease stale = first;
        CancellationTokenSource firstSource = first.Value;
        first.Dispose();

        CancellationTokenSourcePool.Lease second = pool.RentScoped();
        stale.Dispose();
        CancellationTokenSource concurrent = pool.Rent();
        bool reusedFirstSource = ReferenceEquals(second.Value, firstSource);
        bool sourcesAreDistinct = !ReferenceEquals(concurrent, second.Value);

        second.Dispose();
        concurrent.Dispose();

        await Assert.That(reusedFirstSource).IsTrue();
        await Assert.That(sourcesAreDistinct).IsTrue();
    }

    [Test]
    public async Task ClearDisposesScopedThreadLocalSourceAndLeavesPoolUsable()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        CancellationTokenSource retained;

        {
            using CancellationTokenSourcePool.Lease lease = pool.RentScoped(out retained);
            _ = retained.Token.WaitHandle;
        }

        pool.Clear();
        CancellationTokenSource replacement = pool.Rent();

        await Assert.That(() => retained.Cancel()).Throws<ObjectDisposedException>();
        await Assert.That(replacement).IsNotSameReferenceAs(retained);

        replacement.Dispose();
    }

    [Test]
    public async Task DisposeReleasesScopedThreadLocalSourceAndClosesPool()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        CancellationTokenSource retained;

        {
            using CancellationTokenSourcePool.Lease lease = pool.RentScoped(out retained);
            _ = retained.Token.WaitHandle;
        }

        pool.Dispose();

        await Assert.That(() => retained.Cancel()).Throws<ObjectDisposedException>();
        await Assert.That(() => pool.RentScoped()).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task ScopedRentalDisposedAfterPoolIsClosedIsPermanentlyDisposed()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        CancellationTokenSourcePool.Lease lease = pool.RentScoped();
        CancellationTokenSource outstanding = lease.Value;

        pool.Dispose();
        lease.Dispose();

        await Assert.That(() => outstanding.Cancel()).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task ConcurrentScopedReturnAfterDisposePermanentlyDisposesSource()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        using var rented = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        CancellationTokenSource? outstanding = null;
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                using CancellationTokenSourcePool.Lease lease = pool.RentScoped(out outstanding);
                rented.Set();
                release.Wait();
            }
            catch (Exception exception)
            {
                failure = exception;
                rented.Set();
            }
        });

        worker.Start();
        if (!rented.Wait(TimeSpan.FromSeconds(5)))
        {
            release.Set();
            worker.Join();
            throw new TimeoutException("The scoped rental worker did not start.");
        }

        pool.Dispose();
        release.Set();
        bool joined = worker.Join(TimeSpan.FromSeconds(5));

        await Assert.That(joined).IsTrue();
        await Assert.That(failure).IsNull();
        await Assert.That(outstanding).IsNotNull();
        await Assert.That(() => outstanding!.Cancel()).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task ClearDisposesRetainedSourceAndLeavesPoolUsable()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        CancellationTokenSource retained = pool.Rent();
        _ = retained.Token.WaitHandle;
        retained.Dispose();

        pool.Clear();
        CancellationTokenSource replacement = pool.Rent();

        await Assert.That(() => retained.Cancel()).Throws<ObjectDisposedException>();
        await Assert.That(replacement).IsNotSameReferenceAs(retained);

        replacement.Dispose();
    }

    [Test]
    public async Task DisposeReleasesRetainedSourceAndClosesPool()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        CancellationTokenSource retained = pool.Rent();
        _ = retained.Token.WaitHandle;
        retained.Dispose();

        pool.Dispose();

        await Assert.That(() => retained.Cancel()).Throws<ObjectDisposedException>();
        await Assert.That(() => pool.Rent()).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task RentalDisposedAfterPoolIsClosedIsPermanentlyDisposed()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        CancellationTokenSource outstanding = pool.Rent();

        pool.Dispose();
        outstanding.Dispose();

        await Assert.That(() => outstanding.Cancel()).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task CapacityOverflowPermanentlyDisposesExcessSource()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        CancellationTokenSource retained = pool.Rent();
        CancellationTokenSource excess = pool.Rent();

        retained.Dispose();
        excess.Dispose();

        await Assert.That(() => excess.Cancel()).Throws<ObjectDisposedException>();

        CancellationTokenSource reused = pool.Rent();
        await Assert.That(reused).IsSameReferenceAs(retained);
        reused.Dispose();
    }

    [Test]
    public async Task DisposeClearsSharedPoolWithoutClosingIt()
    {
        CancellationTokenSourcePool pool = CancellationTokenSourcePool.Shared;
        using var upstream = new CancellationTokenSource();
        CancellationTokenSource outstanding = pool.RentLinked(upstream.Token);
        CancellationTokenSource retained = pool.Rent();
        CancellationTokenSource scopedRetained;
        _ = retained.Token.WaitHandle;
        retained.Dispose();
        {
            using CancellationTokenSourcePool.Lease lease = pool.RentScoped(out scopedRetained);
            _ = scopedRetained.Token.WaitHandle;
        }

        pool.Dispose();
        outstanding.Dispose();
        CancellationTokenSource replacement = pool.Rent();
        upstream.Cancel();

        await Assert.That(() => retained.Cancel()).Throws<ObjectDisposedException>();
        await Assert.That(() => scopedRetained.Cancel()).Throws<ObjectDisposedException>();
        await Assert.That(replacement).IsNotSameReferenceAs(retained);
        await Assert.That(replacement).IsSameReferenceAs(outstanding);
        await Assert.That(replacement.IsCancellationRequested).IsFalse();

        replacement.Dispose();
    }

    [Test]
    public async Task WarmRentAndDisposeAllocatesNothing()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        CancellationTokenSource warm = pool.Rent();
        warm.Dispose();

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 1_000; i++)
        {
            CancellationTokenSource source = pool.Rent();
            source.Dispose();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0);
    }

    [Test]
    public async Task WarmLinkedRentAndDisposeAllocatesNothing()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        using var upstream = new CancellationTokenSource();
        CancellationTokenSource warm = pool.RentLinked(upstream.Token);
        warm.Dispose();

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 1_000; i++)
        {
            CancellationTokenSource source = pool.RentLinked(upstream.Token);
            source.Dispose();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0);
    }

    [Test]
    public async Task WarmScopedRentAndDisposeAllocatesNothing()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: 1);

        {
            using CancellationTokenSourcePool.Lease lease = pool.RentScoped();
            _ = lease.Value.IsCancellationRequested;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        int canceledCount = 0;

        for (int i = 0; i < 1_000; i++)
        {
            using CancellationTokenSourcePool.Lease lease = pool.RentScoped();
            canceledCount += lease.Value.IsCancellationRequested ? 1 : 0;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(canceledCount).IsEqualTo(0);
        await Assert.That(allocated).IsEqualTo(0);
    }

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
            source.Dispose();

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
                canceled.Dispose();
            }
        }
    }

    private static void StressTimerDisarm(
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
            source.CancelAfter(TimeSpan.FromMinutes(1));
            state.CompleteRental(source);
            source.Dispose();

            CancellationTokenSource next = pool.Rent();
            state.TrackRental(next);
            if (next.IsCancellationRequested)
            {
                state.RecordFailure("Timer disarm produced a canceled rental.");
            }

            state.CompleteRental(next);
            next.Dispose();
        }
    }

    private static void StressScopedPool(
        CancellationTokenSourcePool pool,
        StressState state,
        Barrier start,
        int iterations)
    {
        start.SignalAndWait();

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            using CancellationTokenSourcePool.Lease lease = pool.RentScoped();
            CancellationTokenSource source = lease.Value;
            state.TrackRental(source);

            if (source.IsCancellationRequested)
            {
                state.RecordFailure("Scoped pool returned a canceled source.");
            }

            if ((iteration & 7) == 0)
            {
                source.CancelAfter(TimeSpan.FromMinutes(1));
            }

            Thread.SpinWait(8);
            state.CompleteRental(source);
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
