#if DEBUG || RESERVOIR_DIAGNOSTICS
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Reservoir.Tests;

public class ObjectPoolDiagnosticsTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task ReturningObjectTwiceThrows()
    {
        var pool = new ObjectPool<PooledItem, PooledItemPolicy>(maxCapacity: 1);
        PooledItem item = pool.Rent();

        pool.Return(item);

        await Assert.That(() => pool.Return(item)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ReturningObjectToWrongPoolThrows()
    {
        var firstPool = new ObjectPool<PooledItem, PooledItemPolicy>(maxCapacity: 1);
        var secondPool = new ObjectPool<PooledItem, PooledItemPolicy>(maxCapacity: 1);
        PooledItem item = firstPool.Rent();

        await Assert.That(() => secondPool.Return(item)).Throws<InvalidOperationException>();

        firstPool.Return(item);
    }

    [Test]
    public async Task UntrackedReturnAfterDisposalDisposesBeforeThrowing()
    {
        var pool = new ObjectPool<DisposablePooledItem, DisposablePooledItemPolicy>(
            maxCapacity: 1);
        var item = new DisposablePooledItem();
        pool.Dispose();

        await Assert.That(() => pool.Return(item)).Throws<InvalidOperationException>();
        await Assert.That(item.DisposeCount).IsEqualTo(1);
    }

    [Test]
    [NotInParallel]
    public async Task ThrowingTraceListenerDoesNotEscapeLeakReporter()
    {
        var listener = new ThrowingTraceListener();
        Trace.Listeners.Add(listener);
        Exception? caught = null;

        try
        {
            MethodInfo reportLeak = typeof(ObjectPoolDiagnostics).GetMethod(
                "ReportLeak",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            try
            {
                reportLeak.Invoke(null, [typeof(PooledItem), "rent site"]);
            }
            catch (TargetInvocationException exception)
            {
                caught = exception.InnerException;
            }
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }

        await Assert.That(caught).IsNull();
    }

    [Test]
    public async Task UnreturnedRentalReportsRentSite()
    {
        var reportCompletion = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void HandleLeak(string report)
        {
            if (report.Contains(typeof(LeakedPooledItem).FullName!, StringComparison.Ordinal))
            {
                reportCompletion.TrySetResult(report);
            }
        }

        ObjectPoolDiagnostics.LeakDetected += HandleLeak;

        try
        {
            WeakReference leakedItem = CreateLeakedRental();

            for (int attempt = 0;
                 attempt < 10 && !reportCompletion.Task.IsCompleted;
                 attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                await Task.Yield();
            }

            string report = await reportCompletion.Task.WaitAsync(TestTimeout);

            await Assert.That(leakedItem.IsAlive).IsFalse();
            await Assert.That(report).Contains(typeof(LeakedPooledItem).FullName!);
            await Assert.That(report).Contains(nameof(CreateLeakedRental));
        }
        finally
        {
            ObjectPoolDiagnostics.LeakDetected -= HandleLeak;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateLeakedRental()
    {
        var pool = new ObjectPool<LeakedPooledItem, LeakedPooledItemPolicy>(maxCapacity: 1);
        LeakedPooledItem item = pool.Rent();
        return new WeakReference(item);
    }

    private sealed class PooledItem;

    private readonly struct PooledItemPolicy : IPooledObjectPolicy<PooledItem>
    {
        public PooledItem Create() => new();

        public bool TryReset(PooledItem obj) => true;
    }

    private sealed class DisposablePooledItem : IDisposable
    {
        internal int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }

    private readonly struct DisposablePooledItemPolicy
        : IPooledObjectPolicy<DisposablePooledItem>
    {
        public DisposablePooledItem Create() => new();

        public bool TryReset(DisposablePooledItem obj) => true;
    }

    private sealed class LeakedPooledItem;

    private readonly struct LeakedPooledItemPolicy : IPooledObjectPolicy<LeakedPooledItem>
    {
        public LeakedPooledItem Create() => new();

        public bool TryReset(LeakedPooledItem obj) => true;
    }

    private sealed class ThrowingTraceListener : TraceListener
    {
        public override void Write(string? message)
            => throw new InvalidOperationException("Trace failed.");

        public override void WriteLine(string? message)
            => throw new InvalidOperationException("Trace failed.");
    }
}
#endif
