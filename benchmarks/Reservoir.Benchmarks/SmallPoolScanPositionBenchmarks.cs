using System.Reflection;
using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
public class SmallPoolScanPositionBenchmarks
{
    private ObjectPool<Payload, SingletonPolicy>? _pool;
    private FieldInfo? _affinity;
    private object? _previousAffinity;
    private int _ordinal;
    private int _threadId;

    [Params(32, 64)]
    public int Capacity { get; set; }

    [ParamsAllValues]
    public HomePosition Position { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _pool = new ObjectPool<Payload, SingletonPolicy>(default, Capacity, threadLocalFastPath: false);
        _affinity = typeof(ObjectPool<Payload, SingletonPolicy>)
            .GetField("_threadStripe", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("The benchmark requires ordinal-based small-pool affinity.");
        _previousAffinity = _affinity.GetValue(null);
        _threadId = Environment.CurrentManagedThreadId;

        int home = Position switch
        {
            HomePosition.First => 0,
            HomePosition.Middle => Capacity / 2,
            HomePosition.Last => Capacity - 1,
            _ => throw new ArgumentOutOfRangeException(nameof(Position))
        };
        uint ordinal = 0;
        while (ordinal < (uint)Capacity && _pool.GetAffinityIndex(ordinal) != home)
        {
            ordinal++;
        }

        if (ordinal == (uint)Capacity)
        {
            throw new InvalidOperationException("A power-of-two pool did not map an ordinal to the requested home.");
        }

        _ordinal = checked((int)ordinal + 1);
        _affinity.SetValue(null, _ordinal);
        if (!ReferenceEquals(_pool.Rent(), SingletonPolicy.Instance))
        {
            throw new InvalidOperationException("The empty scan did not return its singleton factory value.");
        }

        Console.WriteLine($"Scan home: capacity={Capacity}, position={Position}, home={home}, ordinal={_ordinal}");
    }

    [Benchmark]
    public Payload EmptyRent() => _pool!.Rent();

    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            if (Environment.CurrentManagedThreadId != _threadId || (int)_affinity!.GetValue(null)! != _ordinal)
            {
                throw new InvalidOperationException("The scan benchmark changed its thread or affinity ordinal.");
            }
        }
        finally
        {
            _affinity!.SetValue(null, _previousAffinity);
            _pool!.Dispose();
        }
    }

    public enum HomePosition
    {
        First,
        Middle,
        Last
    }

    public sealed class Payload;

    public readonly struct SingletonPolicy : IPooledObjectPolicy<Payload>
    {
        internal static readonly Payload Instance = new();

        public Payload Create() => Instance;
        public bool TryReset(Payload obj) => true;
        public void Destroy(Payload obj)
        {
        }
    }
}
