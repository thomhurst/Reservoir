using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[MemoryDiagnoser]
public class ScopedRentalBenchmarks
{
    private ObjectPool<Payload, Policy> _manual = null!;
    private ObjectPool<Payload, Policy> _scoped = null!;
    private ObjectPool<Payload> _runtimeManual = null!;
    private ObjectPool<Payload> _runtimeScoped = null!;

    [Params(1, 32, 256)]
    public int Capacity { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _manual = new(default, Capacity);
        _scoped = new(default, Capacity);
        _runtimeManual = new(new Policy(), Capacity);
        _runtimeScoped = new(new Policy(), Capacity);
        _ = Manual();
        _ = Scoped();
        _ = RuntimeManual();
        _ = RuntimeScoped();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _manual.Dispose();
        _scoped.Dispose();
        _runtimeManual.Dispose();
        _runtimeScoped.Dispose();
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
    public Payload ScopedValue()
    {
        using var lease = _scoped.RentScoped();
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
