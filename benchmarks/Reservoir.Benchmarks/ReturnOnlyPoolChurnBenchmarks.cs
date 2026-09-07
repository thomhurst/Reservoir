using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

/// <summary>Measures a completion thread's first return to each of many independently owned pools.</summary>
[MemoryDiagnoser]
public class ReturnOnlyPoolChurnBenchmarks
{
    private const int PoolCount = 1024;
    private readonly ObjectPool<Payload, Policy>[] _pools = new ObjectPool<Payload, Policy>[PoolCount];
    private readonly Payload[] _items = new Payload[PoolCount];
    private BenchmarkWorkerGroup? _renter;

    [GlobalSetup]
    public void Setup()
    {
        _renter = new BenchmarkWorkerGroup(1, () =>
        {
            for (int i = 0; i < PoolCount; i++)
            {
                _items[i] = _pools[i].Rent();
            }
        });
    }

    [IterationSetup]
    public void PreparePools()
    {
        for (int i = 0; i < PoolCount; i++)
        {
            _pools[i] = new ObjectPool<Payload, Policy>(default, 1, threadLocalFastPath: true);
        }

        // Only the dedicated renter initializes a slot. The measured thread returns once
        // to each pool; repeating an invocation would instead measure already-created slots.
        _renter!.Run();
    }

    [Benchmark(OperationsPerInvoke = PoolCount)]
    public void FirstReturns()
    {
        for (int i = 0; i < PoolCount; i++)
        {
            _pools[i].Return(_items[i]);
        }
    }

    [IterationCleanup]
    public void DisposePools()
    {
        foreach (ObjectPool<Payload, Policy> pool in _pools)
        {
            pool.Dispose();
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _renter?.Dispose();

    public sealed class Payload;

    public readonly struct Policy : IPooledObjectPolicy<Payload>, INonThrowingResetPolicy
    {
        public Payload Create() => new();
        public bool TryReset(Payload obj) => true;
        public void Destroy(Payload obj) { }
    }
}
