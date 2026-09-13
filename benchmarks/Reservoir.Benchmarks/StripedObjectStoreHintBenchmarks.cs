using System.Reflection;
using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

// Fixed geometry exposes the stale-hint scan independently of runner processor count.
[MemoryDiagnoser(displayGenColumns: false)]
public class StripedObjectStoreHintBenchmarks
{
    private static readonly FieldInfo ThreadStripe = typeof(StripedObjectStore<Payload>)
        .GetField("_threadStripe", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly FieldInfo LastRentStripe = typeof(StripedObjectStore<Payload>)
        .GetField("_lastRentStripe", BindingFlags.NonPublic | BindingFlags.Static)!;

    private StripedObjectStore<Payload> _empty = null!;
    private StripedObjectStore<Payload> _populated = null!;
    private readonly Payload _item = new();
    private int _previousThreadStripe;
    private int _previousHint;
    private int _ownerThread;
    private int _expectedHint;

    [Params(2, 20)]
    public int StripeCount { get; set; }

    [Params(HintState.None, HintState.Remote, HintState.OutOfRange)]
    public HintState Hint { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _ownerThread = Environment.CurrentManagedThreadId;
        _previousThreadStripe = (int)ThreadStripe.GetValue(null)!;
        _previousHint = (int)LastRentStripe.GetValue(null)!;
        _expectedHint = Hint switch
        {
            HintState.None => 0,
            HintState.Remote => StripeCount,
            HintState.OutOfRange => StripeCount + 1,
            _ => throw new InvalidOperationException(),
        };
        ThreadStripe.SetValue(null, 1);
        LastRentStripe.SetValue(null, _expectedHint);
        _empty = new StripedObjectStore<Payload>(StripeCount * 16, StripeCount);
        _populated = new StripedObjectStore<Payload>(StripeCount * 16, StripeCount);
        if (StripedObjectStore<Payload>.GetStripeCount(StripeCount * 16, StripeCount) != StripeCount
            || _empty.GetAffinityIndex(0) != 0
            || !_populated.TryPush(_item))
        {
            throw new InvalidOperationException("Hint benchmark geometry or initial publication failed.");
        }
    }

    [Benchmark]
    public bool EmptyPop() => _empty.TryPop(out _);

    [Benchmark]
    public Payload HomePopPush()
    {
        if (!_populated.TryPop(out Payload? item) || !_populated.TryPush(item!))
        {
            throw new InvalidOperationException("Hint benchmark lost its retained item.");
        }

        return item!;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread
            || (int)ThreadStripe.GetValue(null)! != 1
            || (int)LastRentStripe.GetValue(null)! != _expectedHint
            || _empty.TryPop(out _)
            || !_populated.TryPop(out Payload? item)
            || !ReferenceEquals(item, _item)
            || _populated.TryPop(out _))
        {
            throw new InvalidOperationException("Hint benchmark changed its geometry, hint, or ownership.");
        }

        ThreadStripe.SetValue(null, _previousThreadStripe);
        LastRentStripe.SetValue(null, _previousHint);
    }

    public enum HintState
    {
        None,
        Remote,
        OutOfRange,
    }

    public sealed class Payload;
}
