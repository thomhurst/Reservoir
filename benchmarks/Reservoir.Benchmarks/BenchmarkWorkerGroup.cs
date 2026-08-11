using System.Runtime.ExceptionServices;

namespace Reservoir.Benchmarks;

internal sealed class BenchmarkWorkerGroup : IDisposable
{
    private readonly Barrier _finish;
    private readonly AutoResetEvent[] _starts;
    private readonly Thread[] _threads;
    private ExceptionDispatchInfo? _failure;
    private int _isDisposed;
    private volatile bool _stopping;

    internal BenchmarkWorkerGroup(int workerCount, Action action)
    {
        _finish = new Barrier(workerCount + 1);
        _starts = new AutoResetEvent[workerCount];
        _threads = new Thread[workerCount];

        for (int i = 0; i < workerCount; i++)
        {
            AutoResetEvent start = _starts[i] = new AutoResetEvent(false);
            _threads[i] = new Thread(() => Work(start, action))
            {
                IsBackground = true,
            };
            _threads[i].Start();
        }
    }

    internal void Run()
    {
        foreach (AutoResetEvent start in _starts)
        {
            start.Set();
        }

        _finish.SignalAndWait();
        _failure?.Throw();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _stopping = true;

        foreach (AutoResetEvent start in _starts)
        {
            start.Set();
        }

        foreach (Thread thread in _threads)
        {
            thread.Join();
        }

        foreach (AutoResetEvent start in _starts)
        {
            start.Dispose();
        }

        _finish.Dispose();
    }

    private void Work(AutoResetEvent start, Action action)
    {
        while (true)
        {
            start.WaitOne();
            if (_stopping)
            {
                return;
            }

            try
            {
                action();
            }
            catch (Exception exception)
            {
                Interlocked.CompareExchange(
                    ref _failure,
                    ExceptionDispatchInfo.Capture(exception),
                    null);
            }
            finally
            {
                _finish.SignalAndWait();
            }
        }
    }
}
