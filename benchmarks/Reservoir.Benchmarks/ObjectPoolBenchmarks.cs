using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
public class ObjectPoolBenchmarks
{
    // The baseline pools use the default shared tier; the thread-local pool opts into the fast
    // path explicitly.
    private readonly ObjectPool<Payload, PayloadPolicy> _pool
        = new(maxCapacity: 32);
    private readonly ObjectPool<Payload, NonThrowingPayloadPolicy> _nonThrowingPool
        = new(maxCapacity: 32);
    private readonly ObjectPool<Payload, PayloadPolicy> _threadLocalPool
        = new(default, maxCapacity: 32, threadLocalFastPath: true);

    [GlobalSetup]
    public void WarmPool()
    {
        Payload payload = _pool.Rent();
        _pool.Return(payload);

        _nonThrowingPool.Return(_nonThrowingPool.Rent());
        _threadLocalPool.Return(_threadLocalPool.Rent());

        using PooledLease<Payload, PayloadPolicy> lease = _pool.RentScoped();
    }

    [Benchmark(Baseline = true)]
    public Payload RentReturn()
    {
        Payload payload = _pool.Rent();
        _pool.Return(payload);
        return payload;
    }

    [Benchmark]
    public Payload RentReturnNonThrowingPolicy()
    {
        Payload payload = _nonThrowingPool.Rent();
        _nonThrowingPool.Return(payload);
        return payload;
    }

    [Benchmark]
    public Payload RentReturnThreadLocalFastPath()
    {
        Payload payload = _threadLocalPool.Rent();
        _threadLocalPool.Return(payload);
        return payload;
    }

    [Benchmark]
    public Payload ScopedRentReturn()
    {
        using PooledLease<Payload, PayloadPolicy> lease = _pool.RentScoped();
        return lease.Value;
    }

    [Benchmark]
    public Payload ScopedOutRentReturn()
    {
        using PooledLease<Payload, PayloadPolicy> lease = _pool.RentScoped(out Payload payload);
        return payload;
    }

    public sealed class Payload;

    public readonly struct PayloadPolicy : IPooledObjectPolicy<Payload>
    {
        public Payload Create() => new();

        public bool TryReset(Payload obj) => true;
    }

    public readonly struct NonThrowingPayloadPolicy
        : IPooledObjectPolicy<Payload>, INonThrowingResetPolicy
    {
        public Payload Create() => new();

        public bool TryReset(Payload obj) => true;
    }
}
