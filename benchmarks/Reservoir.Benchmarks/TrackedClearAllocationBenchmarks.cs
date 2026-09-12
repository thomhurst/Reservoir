using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[MemoryDiagnoser]
public class TrackedClearAllocationBenchmarks
{
    private ObjectPool<Payload, Policy>? _pool;
    private BenchmarkWorkerGroup? _workers;

    [Params(1, 4, 8, 9, 16)]
    public int WorkerCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _pool = new ObjectPool<Payload, Policy>(default, 1);
        _workers = new BenchmarkWorkerGroup(WorkerCount, () =>
        {
            using var lease = _pool.RentScoped();
        });
        // Keep the registered threads alive, and warm capture bookkeeping outside measurement.
        _workers.Run();
        _pool.Clear();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _workers?.Dispose();
        _pool?.Dispose();
    }

    [Benchmark]
    public void ClearEmpty() => _pool!.Clear();

    [Benchmark]
    public void RefillAndClear()
    {
        // Includes worker dispatch, replacement payloads, and cleanup of one item per worker.
        _workers!.Run();
        _pool!.Clear();
    }

    public sealed class Payload;

    public readonly struct Policy : IPooledObjectDestroyPolicy<Payload>, INonThrowingResetPolicy
    {
        public Payload Create() => new();
        public bool TryReset(Payload item) => true;
        public void Destroy(Payload item) { }
    }
}
