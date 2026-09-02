using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

/// <summary>
/// Returns a foreign object into a pool that is already full, so every return displaces the home
/// slot, scans the whole store for an empty slot, and discards. The returned object is created
/// per operation because the discard consumes it; that allocation is the same on every side.
/// </summary>
[BenchmarkCategory("Storage")]
[MemoryDiagnoser(displayGenColumns: false)]
public class FullPoolReturnBenchmarks
{
    private const int OperationsPerInvocation = 65_536;

    private BenchmarkWorkerGroup? _workers;

    [Params(32, 4096)]
    public int Capacity { get; set; }

    [Params(1, 4)]
    public int WorkerCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var pool = new ObjectPool<Payload, PayloadPolicy>(maxCapacity: Capacity);
        var items = new Payload[Capacity];
        for (int i = 0; i < items.Length; i++)
        {
            items[i] = pool.Rent();
        }

        foreach (Payload item in items)
        {
            pool.Return(item);
        }

        int operationsPerWorker = OperationsPerInvocation / WorkerCount;
        _workers = new BenchmarkWorkerGroup(
            WorkerCount,
            _ =>
            {
                for (int i = 0; i < operationsPerWorker; i++)
                {
                    pool.Return(new Payload());
                }
            });
    }

    [GlobalCleanup]
    public void Cleanup() => _workers?.Dispose();

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public void ReturnForeignIntoFullPool() => _workers!.Run();

    public sealed class Payload;

    public readonly struct PayloadPolicy : IPooledObjectPolicy<Payload>
    {
        public Payload Create() => new();

        public bool TryReset(Payload obj) => true;

        // Every operation discards one object, so the explicit no-op keeps the constrained call
        // devirtualized instead of boxing the policy.
        public void Destroy(Payload obj)
        {
        }
    }
}
