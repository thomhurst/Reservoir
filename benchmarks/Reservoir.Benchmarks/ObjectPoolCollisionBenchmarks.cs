using System.Reflection;
using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

/// <summary>Compares spread affinity with workers deliberately competing for one home slot.</summary>
[MemoryDiagnoser]
public class ObjectPoolCollisionBenchmarks
{
    private const int OperationsPerInvocation = 65_536;
    private BenchmarkWorkerGroup? _workers;
    private ObjectPool<Payload, Policy>? _pool;

    [Params(8, 32)]
    public int Capacity { get; set; }

    [Params(4, 8)]
    public int WorkerCount { get; set; }

    [Params(false, true)]
    public bool CollidingAffinity { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var pool = new ObjectPool<Payload, Policy>(maxCapacity: Capacity);
        _pool = pool;
        var items = new Payload[Capacity];
        for (int i = 0; i < items.Length; i++)
        {
            items[i] = pool.Rent();
        }

        foreach (Payload item in items)
        {
            pool.Return(item);
        }

        FieldInfo affinity = typeof(ObjectPool<Payload, Policy>)
            .GetField("_threadStripe", BindingFlags.NonPublic | BindingFlags.Static)!;
        var initialized = new bool[WorkerCount];
        int operationsPerWorker = OperationsPerInvocation / WorkerCount;
        _workers = new BenchmarkWorkerGroup(WorkerCount, worker =>
        {
            if (!initialized[worker])
            {
                affinity.SetValue(null, CollidingAffinity ? 1 : worker + 1);
                initialized[worker] = true;
            }

            for (int i = 0; i < operationsPerWorker; i++)
            {
                Payload item = pool.Rent();
                pool.Return(item);
            }
        });

        _workers.Run();
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public void RentReturn() => _workers!.Run();

    [GlobalCleanup]
    public void Cleanup()
    {
        _workers?.Dispose();
        _pool?.Dispose();
    }

    public sealed class Payload;

    public readonly struct Policy : IPooledObjectPolicy<Payload>, INonThrowingResetPolicy
    {
        public Payload Create() => new();
        public bool TryReset(Payload item) => true;
        public void Destroy(Payload item)
        {
        }
    }
}
