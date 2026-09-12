using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

/// <summary>
/// Measures fixed batches of scoped rentals overlapping a bounded number of clears.
/// Clear includes snapshot allocations and destruction, so this is a lifecycle workload.
/// </summary>
[MemoryDiagnoser]
public class ScopedClearContentionBenchmarks
{
    private const int OperationsPerInvocation = 262_144;
    private ObjectPool<Payload, Policy> _pool = null!;
    private BenchmarkWorkerGroup _workers = null!;
    private Barrier _start = null!;

    [Params(1, 4)]
    public int WorkerCount { get; set; }

    [Params(0, 8, 64)]
    public int ClearCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _pool = new ObjectPool<Payload, Policy>(maxCapacity: 32);
        _start = new Barrier(WorkerCount + 1);
        int rentalsPerWorker = OperationsPerInvocation / WorkerCount;
        _workers = new BenchmarkWorkerGroup(WorkerCount + 1, worker =>
        {
            _start.SignalAndWait();
            if (worker == WorkerCount)
            {
                for (int i = 0; i < ClearCount; i++)
                {
                    _pool.Clear();
                }
            }
            else
            {
                RentScoped(rentalsPerWorker);
            }
        });
        _workers.Run();
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public void ScopedRentAndClear() => _workers.Run();

    [GlobalCleanup]
    public void Cleanup()
    {
        _workers.Dispose();
        _start.Dispose();
        _pool.Dispose();
    }

    private void RentScoped(int count)
    {
        for (int i = 0; i < count; i++)
        {
            using var lease = _pool.RentScoped(out Payload item);
            if (Volatile.Read(ref item.Destroyed) != 0)
            {
                throw new InvalidOperationException("An active lease exposes a destroyed item.");
            }
        }
    }

    public sealed class Payload
    {
        public int Destroyed;
    }

    public readonly struct Policy : IPooledObjectPolicy<Payload>, INonThrowingResetPolicy
    {
        public Payload Create() => new();
        public bool TryReset(Payload item) => true;
        public void Destroy(Payload item) => Volatile.Write(ref item.Destroyed, 1);
    }
}
