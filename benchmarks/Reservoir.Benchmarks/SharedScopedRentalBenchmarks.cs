using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[MemoryDiagnoser]
public class SharedScopedRentalBenchmarks
{
    private ObjectPool<Payload, Policy> _manual = null!;
    private ObjectPool<Payload, Policy> _scoped = null!;
    private ObjectPool<Payload, Policy> _sharedScoped = null!;
    private ObjectPool<Payload> _runtimeManual = null!;
    private ObjectPool<Payload> _runtimeScoped = null!;
    private ObjectPool<Payload> _runtimeSharedScoped = null!;

    [Params(1, 32, 256)]
    public int Capacity { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _manual = new(default, Capacity);
        _scoped = new(default, Capacity);
        _sharedScoped = new(default, Capacity);
        _runtimeManual = new(new Policy(), Capacity);
        _runtimeScoped = new(new Policy(), Capacity);
        _runtimeSharedScoped = new(new Policy(), Capacity);
        _ = Manual();
        _ = Scoped();
        _ = SharedScoped();
        _ = RuntimeManual();
        _ = RuntimeScoped();
        _ = RuntimeSharedScoped();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _manual.Dispose();
        _scoped.Dispose();
        _sharedScoped.Dispose();
        _runtimeManual.Dispose();
        _runtimeScoped.Dispose();
        _runtimeSharedScoped.Dispose();
    }

    [Benchmark(Baseline = true)]
    public Payload Manual()
    {
        Payload item = _manual.Rent();
        item.Length = 1;
        _manual.Return(item);
        return item;
    }

    [Benchmark]
    public Payload Scoped()
    {
        using var lease = _scoped.RentScoped(out Payload item);
        item.Length = 1;
        return item;
    }

    [Benchmark]
    public Payload SharedScoped()
    {
        using var lease = _sharedScoped.RentScopedShared(out Payload item);
        item.Length = 1;
        return item;
    }

    [Benchmark]
    public Payload SharedScopedValue()
    {
        using var lease = _sharedScoped.RentScopedShared();
        Payload item = lease.Value;
        item.Length = 1;
        return item;
    }

    [Benchmark]
    public Payload RuntimeManual()
    {
        Payload item = _runtimeManual.Rent();
        item.Length = 1;
        _runtimeManual.Return(item);
        return item;
    }

    [Benchmark]
    public Payload RuntimeScoped()
    {
        using var lease = _runtimeScoped.RentScoped(out Payload item);
        item.Length = 1;
        return item;
    }

    [Benchmark]
    public Payload RuntimeSharedScoped()
    {
        using var lease = _runtimeSharedScoped.RentScopedShared(out Payload item);
        item.Length = 1;
        return item;
    }

    public sealed class Payload
    {
        public int Length;
    }

    public readonly struct Policy : IPooledObjectPolicy<Payload>, INonThrowingResetPolicy
    {
        public Payload Create() => new();
        public bool TryReset(Payload item)
        {
            item.Length = 0;
            return true;
        }
    }
}
