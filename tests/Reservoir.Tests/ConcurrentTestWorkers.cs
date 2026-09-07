using System.Runtime.ExceptionServices;

namespace Reservoir.Tests;

// Every worker must observe cancellation in loops and synchronization waits.
internal static class ConcurrentTestWorkers
{
    internal static async Task RunAsync(
        IEnumerable<Action<CancellationToken>> workers,
        Action<CancellationToken>? background = null,
        TimeSpan? timeout = null)
    {
        TimeSpan deadline = timeout ?? TimeSpan.FromSeconds(30);
        using var stop = new CancellationTokenSource(deadline);
        ExceptionDispatchInfo? failure = null;
        var tasks = new List<Task>();

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
                    Interlocked.CompareExchange(
                        ref failure, ExceptionDispatchInfo.Capture(exception), null);
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
            await Task.WhenAll(foreground).WaitAsync(deadline);
            if (stop.IsCancellationRequested && failure is null)
            {
                throw new TimeoutException("Concurrency workers exceeded the test deadline.");
            }
        }
        catch (Exception exception)
        {
            Interlocked.CompareExchange(ref failure, ExceptionDispatchInfo.Capture(exception), null);
        }
        finally
        {
            stop.Cancel();
            try
            {
                await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception exception)
            {
                if (failure is null)
                {
                    failure = ExceptionDispatchInfo.Capture(exception);
                }
                else
                {
                    failure.SourceException.Data["WorkerCleanupFailure"] = exception;
                }
            }
        }

        failure?.Throw();
    }
}
