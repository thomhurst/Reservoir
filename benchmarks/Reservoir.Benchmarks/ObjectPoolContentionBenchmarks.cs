using System.Runtime.ExceptionServices;
using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[MemoryDiagnoser]
public class ObjectPoolContentionBenchmarks
{
    private const int OperationsPerInvocation = 327_680;
    private const int PoolCapacity = 32;

    private ObjectPool<Payload, PayloadPolicy>? _pool;
    private WorkerGroup? _workers;

    [Params(1, 4, 8, 16, 32)]
    public int WorkerCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _pool = new ObjectPool<Payload, PayloadPolicy>(maxCapacity: PoolCapacity);
        WarmPool(_pool);

        int operationsPerWorker = OperationsPerInvocation / WorkerCount;
        _workers = new WorkerGroup(WorkerCount, () => RentAndReturn(operationsPerWorker));
    }

    [GlobalCleanup]
    public void Cleanup() => _workers?.Dispose();

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public void RentReturn() => _workers!.Run();

    private static void WarmPool(ObjectPool<Payload, PayloadPolicy> pool)
    {
        var items = new Payload[PoolCapacity];

        for (int i = 0; i < items.Length; i++)
        {
            items[i] = pool.Rent();
        }

        foreach (Payload item in items)
        {
            pool.Return(item);
        }
    }

    private void RentAndReturn(int operationCount)
    {
        ObjectPool<Payload, PayloadPolicy> pool = _pool!;

        for (int i = 0; i < operationCount; i++)
        {
            Payload item = pool.Rent();
            pool.Return(item);
        }
    }

    private sealed class WorkerGroup : IDisposable
    {
        private readonly Barrier _finish;
        private readonly AutoResetEvent[] _starts;
        private readonly Thread[] _threads;
        private ExceptionDispatchInfo? _failure;
        private volatile bool _stopping;

        internal WorkerGroup(int workerCount, Action action)
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

    public sealed class Payload;

    public readonly struct PayloadPolicy : IPooledObjectPolicy<Payload>
    {
        public Payload Create() => new();

        public bool TryReset(Payload obj) => true;
    }
}
