using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[BenchmarkCategory("Storage", "Contention")]
[MemoryDiagnoser(displayGenColumns: false)]
public class StripedObjectStoreMixedContentionBenchmarks
{
    private const int OperationsPerInvocation = 262_144;
    private const int Capacity = 256;

    private BenchmarkWorkerGroup? _workers;

    [Params(1, 4)]
    public int WorkerCount { get; set; }

    [Params(1, 4)]
    public int StripeCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var store = new StripedObjectStore<Payload>(Capacity, StripeCount);
        for (int i = 0; i < Capacity; i++)
        {
            if (!store.TryPush(new Payload()))
            {
                throw new InvalidOperationException("Failed to populate striped store.");
            }
        }

        // A full store lets concurrent renters use overflow nodes while another renter owns
        // the direct slot. One worker is the uncontended control; four share one or four stripes.
        int operationsPerWorker = OperationsPerInvocation / WorkerCount;
        _workers = new BenchmarkWorkerGroup(WorkerCount, () => Run(store, operationsPerWorker));
    }

    [GlobalCleanup]
    public void Cleanup() => _workers?.Dispose();

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public void RentReturn() => _workers!.Run();

    private static void Run(StripedObjectStore<Payload> store, int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
        {
            if (!store.TryPop(out Payload? item) || !store.TryPush(item!))
            {
                throw new InvalidOperationException("Striped store lost capacity.");
            }
        }
    }

    public sealed class Payload;
}
