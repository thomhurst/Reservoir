using System.Reflection;
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using Kevlar;
using Reservoir;

[MemoryDiagnoser]
public class PoolContextProbe
{
    private static readonly Shield Empty = Shield.Empty;
    private readonly ObjectPool<Item, Policy> _pool = new(maxCapacity: 128);
    private readonly ObjectPool<Item, Policy> _tlsPool = new(default, 128, threadLocalFastPath: true);

    [GlobalSetup]
    public void Setup()
    {
        Assembly reservoir = typeof(ObjectPool<>).Assembly;
        string expected = Environment.GetEnvironmentVariable("EXPECTED_RESERVOIR_VERSION")
            ?? throw new InvalidOperationException("Expected version was not passed to the benchmark process.");
        if (reservoir.GetName().Version != new Version(expected + ".0"))
        {
            throw new InvalidOperationException($"Expected Reservoir {expected}; loaded {reservoir.FullName}.");
        }

        PrintAssembly(reservoir);
        PrintAssembly(typeof(Shield).Assembly);
        _ = ManualTls();
        _ = NestedManualTls();
        _ = Scoped();
        _ = NestedScoped();
        // Validate results and warm both retained objects before measurement.
        if (SingleRentReturn() != 42 || NestedRentReturn() != 84
            || SingleContext().GetAwaiter().GetResult() != 42
            || NestedContext().GetAwaiter().GetResult() != 42)
        {
            throw new InvalidOperationException("The diagnostic workload returned an unexpected result.");
        }
    }

    [Benchmark]
    public int SingleRentReturn()
    {
        Item item = _pool.Rent();
        int result = item.Value;
        _pool.Return(item);
        return result;
    }

    [Benchmark]
    public int NestedRentReturn()
    {
        Item outer = _pool.Rent();
        Item inner = _pool.Rent();
        int result = outer.Value + inner.Value;
        _pool.Return(inner);
        _pool.Return(outer);
        return result;
    }

    [Benchmark]
    public ValueTask<int> SingleContext() =>
        Empty.ExecuteWithContextAsync(static _ => new ValueTask<int>(42));

    [Benchmark]
    public ValueTask<int> NestedContext() =>
        Empty.ExecuteWithContextAsync(
            static parent => Empty.ExecuteWithContextAsync(parent, static _ => new ValueTask<int>(42)));

    [Benchmark]
    public int ManualTls()
    {
        Item item = _tlsPool.Rent();
        int result = item.Value;
        _tlsPool.Return(item);
        return result;
    }

    [Benchmark]
    public int NestedManualTls()
    {
        Item outer = _tlsPool.Rent();
        Item inner = _tlsPool.Rent();
        int result = outer.Value + inner.Value;
        _tlsPool.Return(inner);
        _tlsPool.Return(outer);
        return result;
    }

    [Benchmark]
    public int Scoped()
    {
        using var lease = _pool.RentScoped();
        return lease.Value.Value;
    }

    [Benchmark]
    public int NestedScoped()
    {
        using var outer = _pool.RentScoped();
        using var inner = _pool.RentScoped();
        return outer.Value.Value + inner.Value.Value;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _pool.Dispose();
        _tlsPool.Dispose();
    }

    private static void PrintAssembly(Assembly assembly)
    {
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location)));
        Console.WriteLine($"Loaded {assembly.FullName}; SHA256={hash}; path={assembly.Location}");
    }

    public sealed class Item
    {
        public int Value = 42;
    }

    public readonly struct Policy : IPooledObjectPolicy<Item>
    {
        public Item Create() => new();
        public bool TryReset(Item item) => true;
        public void Destroy(Item item) { }
    }
}
