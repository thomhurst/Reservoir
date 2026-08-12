using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
public class ObjectPoolCapacityScalingBenchmarks
{
    private ObjectPool<Payload, PayloadPolicy>? _pool;
    private ObjectPool<Payload, SingletonPolicy>? _emptyPool;

    [Params(32, 64, 65, 256, 4096, 65536)]
    public int Capacity { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _pool = new ObjectPool<Payload, PayloadPolicy>(Capacity);
        _emptyPool = new ObjectPool<Payload, SingletonPolicy>(Capacity);
        Payload payload = _pool.Rent();
        _pool.Return(payload);
    }

    [Benchmark(Baseline = true)]
    public Payload RentReturn()
    {
        Payload payload = _pool!.Rent();
        _pool.Return(payload);
        return payload;
    }

    [Benchmark]
    public Payload EmptyRent() => _emptyPool!.Rent();

    public sealed class Payload;

    public readonly struct PayloadPolicy : IPooledObjectPolicy<Payload>
    {
        public Payload Create() => new();

        public bool TryReset(Payload obj) => true;
    }

    public readonly struct SingletonPolicy : IPooledObjectPolicy<Payload>
    {
        private static readonly Payload Singleton = new();

        public Payload Create() => Singleton;

        public bool TryReset(Payload obj) => true;
    }
}

[MemoryDiagnoser(displayGenColumns: false)]
public class ObjectPoolBurstBenchmarks
{
    private ObjectPool<Payload, PayloadPolicy>? _pool;
    private Payload[]? _items;

    [Params(32, 64, 65, 256, 4096, 65536)]
    public int Capacity { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _pool = new ObjectPool<Payload, PayloadPolicy>(Capacity);
        _items = new Payload[Capacity];

        for (int i = 0; i < Capacity; i++)
        {
            _items[i] = _pool.Rent();
        }

        for (int i = 0; i < Capacity; i++)
        {
            _pool.Return(_items[i]);
        }
    }

    [Benchmark]
    public void DrainAndRefill()
    {
        // The complete burst is the operation under measurement.
        for (int i = 0; i < Capacity; i++)
        {
            _items![i] = _pool!.Rent();
        }

        for (int i = 0; i < Capacity; i++)
        {
            _pool!.Return(_items![i]);
        }
    }

    public sealed class Payload;

    public readonly struct PayloadPolicy : IPooledObjectPolicy<Payload>
    {
        public Payload Create() => new();

        public bool TryReset(Payload obj) => true;
    }
}

[BenchmarkCategory("Storage", "Contention")]
[MemoryDiagnoser(displayGenColumns: false)]
[MinColumn]
[MaxColumn]
[MedianColumn]
public class StripedObjectStoreContentionBenchmarks
{
    private const int OperationsPerInvocation = 327_680;

    private BenchmarkWorkerGroup? _workers;

    [Params(65, 4_096, 65_536)]
    public int Capacity { get; set; }

    [ParamsSource(nameof(WorkerCounts))]
    public int WorkerCount { get; set; }

    [ParamsAllValues]
    public LargeStorePopulation Population { get; set; }

    public IEnumerable<int> WorkerCounts => new[]
    {
        1,
        8,
        16,
        17,
        Environment.ProcessorCount,
        24,
        32,
    }.Distinct();

    [GlobalSetup]
    public void Setup()
    {
        var store = new StripedObjectStore<Payload>(Capacity);
        int population = Population switch
        {
            LargeStorePopulation.OneRetainedItem => 1,
            LargeStorePopulation.OneItemPerWorker => Math.Min(WorkerCount, Capacity),
            LargeStorePopulation.FullCapacity => Capacity,
            _ => throw new ArgumentOutOfRangeException(nameof(Population)),
        };

        for (int i = 0; i < population; i++)
        {
            if (!store.TryPush(new Payload()))
            {
                throw new InvalidOperationException("Failed to populate striped store.");
            }
        }

        _workers = new BenchmarkWorkerGroup(
            WorkerCount,
            workerIndex => Run(store, GetOperationCount(workerIndex)));
    }

    [GlobalCleanup]
    public void Cleanup() => _workers?.Dispose();

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public void RentReturn() => _workers!.Run();

    private static void Run(
        StripedObjectStore<Payload> store,
        int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
        {
            Payload? item;
            while (!store.TryPop(out item))
            {
                Thread.SpinWait(1);
            }

            if (!store.TryPush(item!))
            {
                throw new InvalidOperationException("Striped store rejected a rented item.");
            }
        }
    }

    private int GetOperationCount(int workerIndex)
    {
        int baseCount = OperationsPerInvocation / WorkerCount;
        return baseCount + (workerIndex < OperationsPerInvocation % WorkerCount ? 1 : 0);
    }

    public sealed class Payload;
}

public enum LargeStorePopulation
{
    OneRetainedItem,
    OneItemPerWorker,
    FullCapacity,
}
