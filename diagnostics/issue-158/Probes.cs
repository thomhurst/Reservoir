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

    [GlobalCleanup]
    public void Cleanup() => _pool.Dispose();

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
