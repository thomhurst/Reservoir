using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

/// <summary>
/// Workers do same-thread rent/return on the strict shared tier with first touches sequenced so
/// worker 0 takes thread ordinal 0 and therefore slot 0, the slot next to the array header whose
/// length every other worker's bounds check reads.
/// </summary>
[BenchmarkCategory("Storage")]
[MemoryDiagnoser(displayGenColumns: false)]
public class SmallPoolHeaderLineBenchmarks
{
    private const int OperationsPerInvocation = 327_680;

    private BenchmarkWorkerGroup? _workers;

    [Params(2, 4)]
    public int WorkerCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var pool = new ObjectPool<Payload, PayloadPolicy>(maxCapacity: 32);
        int operationsPerWorker = OperationsPerInvocation / WorkerCount;
        int started = 0;
        int workerCount = WorkerCount;
        _workers = new BenchmarkWorkerGroup(
            WorkerCount,
            workerIndex =>
            {
                if (Volatile.Read(ref started) < workerCount)
                {
                    while (Volatile.Read(ref started) != workerIndex)
                    {
                        Thread.SpinWait(1);
                    }

                    pool.Return(pool.Rent());
                    Interlocked.Increment(ref started);
                    while (Volatile.Read(ref started) != workerCount)
                    {
                        Thread.SpinWait(1);
                    }
                }

                for (int i = 0; i < operationsPerWorker; i++)
                {
                    pool.Return(pool.Rent());
                }
            });
    }

    [GlobalCleanup]
    public void Cleanup() => _workers?.Dispose();

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public void StrictRentReturn() => _workers!.Run();

    public sealed class Payload;

    public readonly struct PayloadPolicy : IPooledObjectPolicy<Payload>
    {
        public Payload Create() => new();

        public bool TryReset(Payload obj) => true;
    }
}
