using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
public class ObjectPoolCapacityScalingBenchmarks
{
    private ObjectPool<Payload, PayloadPolicy>? _pool;
    private ObjectPool<Payload, SingletonPolicy>? _emptyPool;

    [Params(32, 256, 4096, 65536)]
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

    [Params(32, 256, 4096, 65536)]
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

[MemoryDiagnoser(displayGenColumns: false)]
public class ObjectPoolLargeContentionBenchmarks
{
    private const int OperationsPerInvocation = 327_680;
    private const int PoolCapacity = 65_536;

    private BenchmarkWorkerGroup? _workers;

    [Params(1, 4, 8, 16, 32)]
    public int WorkerCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var pool = new ObjectPool<Payload, PayloadPolicy>(PoolCapacity);
        var items = new Payload[PoolCapacity];

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
            () => Run(pool, operationsPerWorker));
    }

    [GlobalCleanup]
    public void Cleanup() => _workers?.Dispose();

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public void RentReturn() => _workers!.Run();

    private static void Run(
        ObjectPool<Payload, PayloadPolicy> pool,
        int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
        {
            Payload item = pool.Rent();
            pool.Return(item);
        }
    }

    public sealed class Payload;

    public readonly struct PayloadPolicy : IPooledObjectPolicy<Payload>
    {
        public Payload Create() => new();

        public bool TryReset(Payload obj) => true;
    }
}
