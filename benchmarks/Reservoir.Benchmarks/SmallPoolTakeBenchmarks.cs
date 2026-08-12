using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[BenchmarkCategory("Storage")]
[MemoryDiagnoser(displayGenColumns: false)]
public class SmallPoolTakeBenchmarks
{
    private const int OperationsPerInvocation = 65_536;

    private BenchmarkWorkerGroup? _workers;

    [Params(1, 8, 31, 32, 40, 63, 64)]
    public int Capacity { get; set; }

    [Params(Population.Empty, Population.OneItem, Population.HalfFull)]
    public Population InitialPopulation { get; set; }

    [Params(1, 8)]
    public int WorkerCount { get; set; }

    [GlobalSetup(Target = nameof(Exchange))]
    public void SetupExchange() => Setup(RunExchange);

    [GlobalSetup(Target = nameof(ReadBeforeCas))]
    public void SetupReadBeforeCas() => Setup(RunReadBeforeCas);

    [GlobalCleanup]
    public void Cleanup() => _workers?.Dispose();

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation, Baseline = true)]
    public void Exchange() => _workers!.Run();

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public void ReadBeforeCas() => _workers!.Run();

    private void Setup(Action<SmallStore, int, int> run)
    {
        var store = new SmallStore(Capacity);
        store.Populate(InitialPopulation);
        int operationsPerWorker = OperationsPerInvocation / WorkerCount;
        _workers = new BenchmarkWorkerGroup(
            WorkerCount,
            workerIndex => run(
                store,
                store.GetStartIndex(workerIndex),
                operationsPerWorker));
    }

    private static void RunExchange(SmallStore store, int startIndex, int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
        {
            store.TakeExchangeAndRestore(startIndex);
        }
    }

    private static void RunReadBeforeCas(
        SmallStore store,
        int startIndex,
        int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
        {
            store.TakeReadBeforeCasAndRestore(startIndex);
        }
    }

    public enum Population
    {
        Empty,
        OneItem,
        HalfFull,
    }

    private sealed class SmallStore
    {
        private const int CacheLineSlotStride = 8;
        private const uint StripeHashMultiplier = 2_654_435_769u;

        private readonly Payload?[] _items;

        internal SmallStore(int capacity)
        {
            Capacity = capacity;
            _items = new Payload[capacity * CacheLineSlotStride];
        }

        private int Capacity { get; }

        internal void Populate(Population population)
        {
            int itemCount = population switch
            {
                Population.Empty => 0,
                Population.OneItem => 1,
                Population.HalfFull => Math.Max(1, Capacity / 2),
                _ => throw new ArgumentOutOfRangeException(nameof(population)),
            };

            for (int i = 0; i < itemCount; i++)
            {
                GetSlot(i) = new Payload();
            }
        }

        internal int GetStartIndex(int workerIndex)
            => (int)(unchecked((uint)workerIndex) * StripeHashMultiplier % (uint)Capacity);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Payload? TakeExchangeAndRestore(int startIndex)
        {
            int index = startIndex;
            Payload? item = Interlocked.Exchange(ref GetSlot(index), null);

            if (item is null)
            {
                item = TakeSlow(startIndex, out index);
            }

            if (item is not null)
            {
                Volatile.Write(ref GetSlot(index), item);
            }

            return item;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Payload? TakeReadBeforeCasAndRestore(int startIndex)
        {
            int index = startIndex;
            ref Payload? slot = ref GetSlot(index);
            Payload? observed = Volatile.Read(ref slot);
            Payload? item = observed is not null
                && ReferenceEquals(
                    Interlocked.CompareExchange(ref slot, null, observed),
                    observed)
                ? observed
                : null;

            if (item is null)
            {
                item = TakeSlow(startIndex, out index);
            }

            if (item is not null)
            {
                Volatile.Write(ref GetSlot(index), item);
            }

            return item;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private Payload? TakeSlow(int startIndex, out int itemIndex)
        {
            for (int offset = 1; offset < Capacity; offset++)
            {
                int index = startIndex + offset;
                if (index >= Capacity)
                {
                    index -= Capacity;
                }

                ref Payload? slot = ref GetSlot(index);
                Payload? item = Volatile.Read(ref slot);
                if (item is not null
                    && ReferenceEquals(
                        Interlocked.CompareExchange(ref slot, null, item),
                        item))
                {
                    itemIndex = index;
                    return item;
                }
            }

            itemIndex = -1;
            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref Payload? GetSlot(int index)
            => ref _items[index * CacheLineSlotStride];
    }

    private sealed class Payload;
}
