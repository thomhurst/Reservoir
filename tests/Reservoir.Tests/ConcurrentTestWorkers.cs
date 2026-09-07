using System.Runtime.ExceptionServices;

namespace Reservoir.Tests;

// Every worker must observe cancellation in loops and synchronization waits.
internal static class ConcurrentTestWorkers
{
    internal static async Task RunAsync(
        IEnumerable<Action<CancellationToken>> workers,
        Action<CancellationToken>? background = null,
        TimeSpan? timeout = null,
        TimeSpan? cleanupTimeout = null)
    {
        TimeSpan deadline = timeout ?? TimeSpan.FromSeconds(30);
        using var stop = new CancellationTokenSource(deadline);
        ExceptionDispatchInfo? failure = null;
        Exception? cleanupFailure = null;
        var tasks = new List<Task>();

        void RecordFailure(Exception exception) =>
            Interlocked.CompareExchange(ref failure, ExceptionDispatchInfo.Capture(exception), null);

        Task Start(Action<CancellationToken> worker)
        {
            Task task = Task.Factory.StartNew(() =>
            {
                try
                {
                    worker(stop.Token);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                }
                catch (Exception exception)
                {
                    RecordFailure(exception);
                    stop.Cancel();
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            tasks.Add(task);
            return task;
        }

        try
        {
            if (background is not null)
            {
                _ = Start(background);
            }

            Task[] foreground = workers.Select(Start).ToArray();
            // The source cancels cooperative loops and barriers, including during startup.
            // WaitAsync also bounds observation of a worker that never checks cancellation.
            await Task.WhenAll(foreground).WaitAsync(deadline);
            if (stop.IsCancellationRequested && failure is null)
            {
                throw new TimeoutException("Concurrency workers exceeded the test deadline.");
            }
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
        }
        finally
        {
            stop.Cancel();
            try
            {
                await Task.WhenAll(tasks).WaitAsync(cleanupTimeout ?? TimeSpan.FromSeconds(10));
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
            }
        }

        if (cleanupFailure is not null)
        {
            ExceptionDispatchInfo? primary = Volatile.Read(ref failure);
            if (primary is not null)
            {
                throw new AggregateException(
                    "Concurrency workers failed and did not complete cleanup.",
                    primary.SourceException, cleanupFailure);
            }

            ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        }

        failure?.Throw();
    }
}
