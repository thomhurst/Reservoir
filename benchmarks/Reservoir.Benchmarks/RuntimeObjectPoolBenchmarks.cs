using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
public class RuntimePolicyObjectPoolBenchmarks
{
    private readonly ObjectPool<Payload> _pool = new(new PayloadPolicy(), maxCapacity: 32);

    [GlobalSetup]
    public void WarmPool()
    {
        BenchmarkAsset.Verify();
        Payload payload = _pool.Rent();
        _pool.Return(payload);

        using PooledLease<Payload> lease = _pool.RentScoped();
    }

    [Benchmark(Baseline = true)]
    public Payload RentReturn()
    {
        Payload payload = _pool.Rent();
        _pool.Return(payload);
        return payload;
    }

    [Benchmark]
    public Payload ScopedRentReturn()
    {
        using PooledLease<Payload> lease = _pool.RentScoped();
        return lease.Value;
    }

    [Benchmark]
    public Payload ScopedOutRentReturn()
    {
        using PooledLease<Payload> lease = _pool.RentScoped(out Payload payload);
        return payload;
    }

    public sealed class Payload;

    private sealed class PayloadPolicy : IPooledObjectPolicy<Payload>
    {
        public Payload Create() => new();

        public bool TryReset(Payload obj) => true;
#if RESERVOIR_NETSTANDARD
        public void Destroy(Payload obj)
        {
        }
#endif
    }
}

[MemoryDiagnoser(displayGenColumns: false)]
public class FactoryObjectPoolBenchmarks
{
    private readonly ObjectPool<Payload> _pool = new(() => new Payload(), maxCapacity: 32);

    [GlobalSetup]
    public void WarmPool()
    {
        BenchmarkAsset.Verify();
        Payload payload = _pool.Rent();
        _pool.Return(payload);

        using PooledLease<Payload> lease = _pool.RentScoped();
    }

    [Benchmark(Baseline = true)]
    public Payload RentReturn()
    {
        Payload payload = _pool.Rent();
        _pool.Return(payload);
        return payload;
    }

    [Benchmark]
    public Payload ScopedRentReturn()
    {
        using PooledLease<Payload> lease = _pool.RentScoped();
        return lease.Value;
    }

    [Benchmark]
    public Payload ScopedOutRentReturn()
    {
        using PooledLease<Payload> lease = _pool.RentScoped(out Payload payload);
        return payload;
    }

    public sealed class Payload;
}

[MemoryDiagnoser]
public class RuntimeNonThrowingPolicyBenchmarks
{
    private ObjectPool<Payload> _pool = null!;

    [Params(false, true)]
    public bool StructPolicy { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        BenchmarkAsset.Verify();
        IPooledObjectPolicy<Payload> policy = StructPolicy ? new ValuePolicy() : new ReferencePolicy();
        _pool = new ObjectPool<Payload>(policy, maxCapacity: 32);
        _ = RentReturn();
        _ = ScopedOutRentReturn();
        _ = SharedScopedRentReturn();
    }

    [GlobalCleanup]
    public void Cleanup() => _pool.Dispose();

    [Benchmark(Baseline = true)]
    public Payload RentReturn()
    {
        Payload payload = _pool.Rent();
        payload.Value = 1;
        _pool.Return(payload);
        return payload;
    }

    [Benchmark]
    public Payload ScopedOutRentReturn()
    {
        using PooledLease<Payload> lease = _pool.RentScoped(out Payload payload);
        payload.Value = 1;
        return payload;
    }

    [Benchmark]
    public Payload SharedScopedRentReturn()
    {
        using SharedPooledLease<Payload> lease = _pool.RentScopedShared(out Payload payload);
        payload.Value = 1;
        return payload;
    }

    public sealed class Payload
    {
        public int Value;
    }

    private sealed class ReferencePolicy : IPooledObjectPolicy<Payload>, INonThrowingResetPolicy
    {
        public Payload Create() => new();
        public bool TryReset(Payload payload)
        {
            payload.Value = 0;
            return true;
        }
#if RESERVOIR_NETSTANDARD
        public void Destroy(Payload payload) { }
#endif
    }

    private readonly struct ValuePolicy : IPooledObjectPolicy<Payload>, INonThrowingResetPolicy
    {
        public Payload Create() => new();
        public bool TryReset(Payload payload)
        {
            payload.Value = 0;
            return true;
        }
#if RESERVOIR_NETSTANDARD
        public void Destroy(Payload payload) { }
#endif
    }
}
