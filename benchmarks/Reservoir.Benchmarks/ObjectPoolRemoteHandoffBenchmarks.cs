using System.Reflection;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

/// <summary>
/// Measures bounded request/completion handoffs with repeatable home or distant return affinity.
/// Unlike naturally assigned thread ordinals, these placements remain comparable between builds.
/// </summary>
[BenchmarkCategory("Storage", "Contention")]
[MemoryDiagnoser]
public class ObjectPoolRemoteHandoffBenchmarks
{
    private const int OperationsPerInvocation = 65_536;
    private BenchmarkWorkerGroup? _workers;
    private ObjectPool<Payload, PayloadPolicy>? _pool;
    private long _workerAllocatedBytes;
    private long _maximumWorkerAllocatedBytes;

    [Params(65, 4096)]
    public int Capacity { get; set; }

    [ParamsSource(nameof(PairCounts))]
    public int PairCount { get; set; }

    public IEnumerable<int> PairCounts => new[] { 1, 4 }
        .Where(pairs => pairs <= Math.Max(1, Environment.ProcessorCount / 2));

    [Params(ReturnAffinity.Home, ReturnAffinity.Distant)]
    public ReturnAffinity ReturnLocation { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var pool = new ObjectPool<Payload, PayloadPolicy>(Capacity);
        _pool = pool;
        object store = pool.GetType().GetField("_largeStore", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(pool)!;
        var stripes = (Array)store.GetType().GetField("_stripes", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(store)!;
        int stripeCount = stripes.Length;
        int returnDistance = ReturnLocation == ReturnAffinity.Home ? 0 : Math.Max(1, stripeCount - PairCount);
        Console.WriteLine($"// ProcessorCount={Environment.ProcessorCount}, StripeCount={stripeCount}, ReturnDistance={returnDistance}");

        var retained = new Payload[PairCount * 8];
        for (int i = 0; i < retained.Length; i++)
        {
            retained[i] = pool.Rent();
        }

        foreach (Payload item in retained)
        {
            pool.Return(item);
        }

        var channels = new HandoffCell[PairCount];
        for (int i = 0; i < channels.Length; i++)
        {
            channels[i] = new HandoffCell();
        }

        FieldInfo ordinal = store.GetType()
            .GetField("_threadStripe", BindingFlags.NonPublic | BindingFlags.Static)!;
        var initialized = new bool[PairCount * 2];
        _workers = new BenchmarkWorkerGroup(PairCount * 2, workerIndex =>
        {
            int pair = workerIndex % PairCount;
            bool renter = workerIndex < PairCount;
            if (!initialized[workerIndex])
            {
                // Set affinity once on each dedicated worker, outside the measured calls.
                ordinal.SetValue(null, pair + 1 + (renter ? 0 : returnDistance));
                initialized[workerIndex] = true;
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            HandoffCell channel = channels[pair];
            int operationCount = OperationsPerInvocation / PairCount;
            if (renter)
            {
                for (int i = 0; i < operationCount; i++)
                {
                    Payload item = pool.Rent();
                    while (Volatile.Read(ref channel.Item) is not null)
                    {
                        Thread.SpinWait(1);
                    }

                    Volatile.Write(ref channel.Item, item);
                }
            }
            else
            {
                for (int i = 0; i < operationCount; i++)
                {
                    Payload? item;
                    while ((item = Volatile.Read(ref channel.Item)) is null)
                    {
                        Thread.SpinWait(1);
                    }

                    Volatile.Write(ref channel.Item, null);
                    pool.Return(item);
                }
            }

            Interlocked.Add(ref _workerAllocatedBytes, GC.GetAllocatedBytesForCurrentThread() - before);
        });

        for (int i = 0; i < 4; i++)
        {
            _workers.Run();
        }
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public void Handoff()
    {
        _workerAllocatedBytes = 0;
        _workers!.Run();
        _maximumWorkerAllocatedBytes = Math.Max(_maximumWorkerAllocatedBytes, _workerAllocatedBytes);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Console.WriteLine($"// Worker allocated bytes per invocation: final={_workerAllocatedBytes}, max={_maximumWorkerAllocatedBytes}");
        _workers?.Dispose();
        _pool?.Dispose();
    }

    public enum ReturnAffinity
    {
        Home,
        Distant,
    }

    public sealed class Payload;

    public readonly struct PayloadPolicy : IPooledObjectPolicy<Payload>
    {
        public Payload Create() => new();
        public bool TryReset(Payload obj) => true;
    }

    [StructLayout(LayoutKind.Explicit)]
    private sealed class HandoffCell
    {
        [FieldOffset(64)]
        internal Payload? Item;

#pragma warning disable CS0169 // Keep neighbouring channels off the hot item's cache line.
        [FieldOffset(136)]
        private readonly long _trailingPad;
#pragma warning restore CS0169
    }
}
