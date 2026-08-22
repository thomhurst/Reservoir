using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[GenericTypeArguments(typeof(PermanentTrivialPolicy))]
[GenericTypeArguments(typeof(PermanentResetPolicy))]
[MemoryDiagnoser(displayGenColumns: false)]
public class PermanentObjectPoolBenchmarks<TPolicy>
    where TPolicy : struct, IPooledObjectPolicy<PermanentPoolPayload>
{
    private ObjectPool<PermanentPoolPayload, TPolicy>? _lifecyclePool;
    private PermanentObjectPool<PermanentPoolPayload, TPolicy>? _permanentPool;

    [Params(1, 32, 64, 65, 128, 256)]
    public int Capacity { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _lifecyclePool = new ObjectPool<PermanentPoolPayload, TPolicy>(Capacity);
        _permanentPool = new PermanentObjectPool<PermanentPoolPayload, TPolicy>(Capacity);
        _lifecyclePool.Return(_lifecyclePool.Rent());
        _permanentPool.Return(_permanentPool.Rent());
    }

    [Benchmark(Baseline = true)]
    public PermanentPoolPayload Lifecycle()
    {
        PermanentPoolPayload item = _lifecyclePool!.Rent();
        _lifecyclePool.Return(item);
        return item;
    }

    [Benchmark]
    public PermanentPoolPayload Permanent()
    {
        PermanentPoolPayload item = _permanentPool!.Rent();
        _permanentPool.Return(item);
        return item;
    }
}

[MemoryDiagnoser(displayGenColumns: false)]
public class PermanentObjectPoolPopulationBenchmarks
{
    private ObjectPool<PermanentPoolPayload, PermanentTrivialPolicy>? _lifecyclePool;
    private PermanentObjectPool<PermanentPoolPayload, PermanentTrivialPolicy>? _permanentPool;
    private PermanentPoolPayload[]? _lifecycleItems;
    private PermanentPoolPayload[]? _permanentItems;

    [Params(1, 32, 64, 65, 128, 256)]
    public int Capacity { get; set; }

    [Params(PoolPopulation.Half, PoolPopulation.Full)]
    public PoolPopulation Population { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        int retained = Population == PoolPopulation.Full
            ? Capacity
            : Math.Max(1, Capacity / 2);
        _lifecyclePool = new ObjectPool<PermanentPoolPayload, PermanentTrivialPolicy>(Capacity);
        _permanentPool = new PermanentObjectPool<PermanentPoolPayload, PermanentTrivialPolicy>(
            Capacity);
        _lifecycleItems = new PermanentPoolPayload[retained];
        _permanentItems = new PermanentPoolPayload[retained];

        for (int i = 0; i < retained; i++)
        {
            _lifecycleItems[i] = _lifecyclePool.Rent();
            _permanentItems[i] = _permanentPool.Rent();
        }

        for (int i = 0; i < retained; i++)
        {
            _lifecyclePool.Return(_lifecycleItems[i]);
            _permanentPool.Return(_permanentItems[i]);
        }
    }

    [Benchmark(Baseline = true)]
    public void LifecycleDrainAndRefill()
    {
        for (int i = 0; i < _lifecycleItems!.Length; i++)
        {
            _lifecycleItems[i] = _lifecyclePool!.Rent();
        }

        for (int i = 0; i < _lifecycleItems.Length; i++)
        {
            _lifecyclePool!.Return(_lifecycleItems[i]);
        }
    }

    [Benchmark]
    public void PermanentDrainAndRefill()
    {
        for (int i = 0; i < _permanentItems!.Length; i++)
        {
            _permanentItems[i] = _permanentPool!.Rent();
        }

        for (int i = 0; i < _permanentItems.Length; i++)
        {
            _permanentPool!.Return(_permanentItems[i]);
        }
    }
}

[MemoryDiagnoser(displayGenColumns: false)]
public class PermanentObjectPoolMissBenchmarks
{
    private ObjectPool<PermanentPoolPayload, PermanentRejectingPolicy>? _lifecyclePool;
    private PermanentObjectPool<PermanentPoolPayload, PermanentRejectingPolicy>? _permanentPool;

    [Params(1, 32, 64, 65, 128, 256)]
    public int Capacity { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _lifecyclePool = new ObjectPool<PermanentPoolPayload, PermanentRejectingPolicy>(Capacity);
        _permanentPool = new PermanentObjectPool<PermanentPoolPayload, PermanentRejectingPolicy>(
            Capacity);
    }

    [Benchmark(Baseline = true)]
    public PermanentPoolPayload LifecycleMissAndReject()
    {
        PermanentPoolPayload item = _lifecyclePool!.Rent();
        _lifecyclePool.Return(item);
        return item;
    }

    [Benchmark]
    public PermanentPoolPayload PermanentMissAndReject()
    {
        PermanentPoolPayload item = _permanentPool!.Rent();
        _permanentPool.Return(item);
        return item;
    }
}

[MemoryDiagnoser(displayGenColumns: false)]
public class PermanentObjectPoolContentionBenchmarks
{
    private const int OperationsPerInvocation = 327_680;

    private BenchmarkWorkerGroup? _workers;

    [Params(1, 32, 64, 65, 128, 256)]
    public int Capacity { get; set; }

    [ParamsSource(nameof(WorkerCounts))]
    public int WorkerCount { get; set; }

    public IEnumerable<int> WorkerCounts => new[] { 1, Math.Min(16, Environment.ProcessorCount) }
        .Distinct();

    [GlobalSetup(Target = nameof(Lifecycle))]
    public void SetupLifecycle()
    {
        var pool = new ObjectPool<PermanentPoolPayload, PermanentTrivialPolicy>(Capacity);
        Warm(pool);
        _workers = new BenchmarkWorkerGroup(
            WorkerCount,
            workerIndex => Run(pool, GetOperationCount(workerIndex)));
    }

    [GlobalSetup(Target = nameof(Permanent))]
    public void SetupPermanent()
    {
        var pool = new PermanentObjectPool<PermanentPoolPayload, PermanentTrivialPolicy>(Capacity);
        Warm(pool);
        _workers = new BenchmarkWorkerGroup(
            WorkerCount,
            workerIndex => Run(pool, GetOperationCount(workerIndex)));
    }

    [GlobalCleanup]
    public void Cleanup() => _workers?.Dispose();

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation, Baseline = true)]
    public void Lifecycle() => _workers!.Run();

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public void Permanent() => _workers!.Run();

    private static void Warm(ObjectPool<PermanentPoolPayload, PermanentTrivialPolicy> pool)
    {
        var items = new PermanentPoolPayload[pool.MaximumRetained];
        for (int i = 0; i < items.Length; i++)
        {
            items[i] = pool.Rent();
        }

        foreach (PermanentPoolPayload item in items)
        {
            pool.Return(item);
        }
    }

    private static void Warm(
        PermanentObjectPool<PermanentPoolPayload, PermanentTrivialPolicy> pool)
    {
        var items = new PermanentPoolPayload[pool.MaximumRetained];
        for (int i = 0; i < items.Length; i++)
        {
            items[i] = pool.Rent();
        }

        foreach (PermanentPoolPayload item in items)
        {
            pool.Return(item);
        }
    }

    private static void Run(
        ObjectPool<PermanentPoolPayload, PermanentTrivialPolicy> pool,
        int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
        {
            PermanentPoolPayload item = pool.Rent();
            pool.Return(item);
        }
    }

    private static void Run(
        PermanentObjectPool<PermanentPoolPayload, PermanentTrivialPolicy> pool,
        int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
        {
            PermanentPoolPayload item = pool.Rent();
            pool.Return(item);
        }
    }

    private int GetOperationCount(int workerIndex)
    {
        int baseCount = OperationsPerInvocation / WorkerCount;
        return baseCount + (workerIndex < OperationsPerInvocation % WorkerCount ? 1 : 0);
    }
}

public enum PoolPopulation
{
    Half,
    Full,
}

public sealed class PermanentPoolPayload
{
    internal int Value;
}

public readonly struct PermanentTrivialPolicy : IPooledObjectPolicy<PermanentPoolPayload>
{
    public PermanentPoolPayload Create() => new();

    public bool TryReset(PermanentPoolPayload obj) => true;
}

public readonly struct PermanentResetPolicy : IPooledObjectPolicy<PermanentPoolPayload>
{
    public PermanentPoolPayload Create() => new();

    public bool TryReset(PermanentPoolPayload obj)
    {
        obj.Value = 0;
        return true;
    }
}

public readonly struct PermanentRejectingPolicy : IPooledObjectPolicy<PermanentPoolPayload>
{
    private static readonly PermanentPoolPayload Singleton = new();

    public PermanentPoolPayload Create() => Singleton;

    public bool TryReset(PermanentPoolPayload obj) => false;
}
