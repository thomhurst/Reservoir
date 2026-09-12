using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
public class TrackedReturnBenchmarks
{
    private readonly ObjectPool<Payload, Policy> _manual = new(default, 32, threadLocalFastPath: true);
    private readonly ObjectPool<Payload, Policy> _default = new(default, 32);

    [GlobalSetup]
    public void Setup()
    {
        BenchmarkAsset.Verify();
        _manual.Return(_manual.Rent());
        _default.Return(_default.Rent());
        using var lease = _default.RentScoped();
    }

    [Benchmark]
    public Payload ManualThreadLocal()
    {
        Payload item = _manual.Rent();
        _manual.Return(item);
        return item;
    }

    [Benchmark]
    public Payload Scoped()
    {
        using var lease = _default.RentScoped();
        return lease.Value;
    }

    [Benchmark]
    public Payload SharedControl()
    {
        Payload item = _default.Rent();
        _default.Return(item);
        return item;
    }

    public sealed class Payload;

    public readonly struct Policy : IPooledObjectDestroyPolicy<Payload>, INonThrowingResetPolicy
    {
        public Payload Create() => new();
        public bool TryReset(Payload item) => true;
        public void Destroy(Payload item) { }
    }
}
