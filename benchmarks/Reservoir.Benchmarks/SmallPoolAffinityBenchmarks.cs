using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[BenchmarkCategory("Storage")]
[MemoryDiagnoser(displayGenColumns: false)]
public class SmallPoolAffinityBenchmarks
{
    private const int OperationsPerInvocation = 65_536;
    private const uint StripeHashMultiplier = 2_654_435_769u;

    [ThreadStatic]
    private static uint _threadOrdinal;

    private BenchmarkWorkerGroup? _workers;

    [Params(1, 8, 31, 32, 40, 63, 64)]
    public int Capacity { get; set; }

    [Params(Workload.SameThread, Workload.CrossThreadHandoff)]
    public Workload Scenario { get; set; }

    [GlobalSetup(Target = nameof(ModuloThreadOrdinal))]
    public void SetupModuloThreadOrdinal() => Setup(RunModuloThreadOrdinal);

    [GlobalSetup(Target = nameof(MultiplyHighThreadOrdinal))]
    public void SetupMultiplyHighThreadOrdinal() => Setup(RunMultiplyHighThreadOrdinal);

    [GlobalSetup(Target = nameof(ModuloProcessorAffinity))]
    public void SetupModuloProcessorAffinity() => Setup(RunModuloProcessorAffinity);

    [GlobalSetup(Target = nameof(MultiplyHighProcessorAffinity))]
    public void SetupMultiplyHighProcessorAffinity() => Setup(RunMultiplyHighProcessorAffinity);

    [GlobalCleanup]
    public void Cleanup() => _workers?.Dispose();

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation, Baseline = true)]
    public void ModuloThreadOrdinal() => _workers!.Run();

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public void MultiplyHighThreadOrdinal() => _workers!.Run();

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public void ModuloProcessorAffinity() => _workers!.Run();

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public void MultiplyHighProcessorAffinity() => _workers!.Run();

    private void Setup(Action<SmallStore, int> run)
    {
        var store = new SmallStore(Capacity);
        int workerCount = Scenario == Workload.SameThread ? 1 : 8;
        int operationsPerWorker = OperationsPerInvocation / workerCount;
        _workers = new BenchmarkWorkerGroup(
            workerCount,
            workerIndex =>
            {
                _threadOrdinal = (uint)workerIndex;
                run(store, operationsPerWorker);
            });
    }

    private static void RunModuloThreadOrdinal(SmallStore store, int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
        {
            int item = store.Rent(MapModulo(_threadOrdinal, store.Capacity, store.Mask));
            store.Return(item, MapModulo(_threadOrdinal, store.Capacity, store.Mask));
        }
    }

    private static void RunMultiplyHighThreadOrdinal(SmallStore store, int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
        {
            int item = store.Rent(MapMultiplyHigh(_threadOrdinal, store.Capacity, store.Mask));
            store.Return(item, MapMultiplyHigh(_threadOrdinal, store.Capacity, store.Mask));
        }
    }

    private static void RunModuloProcessorAffinity(SmallStore store, int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
        {
            int item = store.Rent(MapModulo(
                (uint)Thread.GetCurrentProcessorId(),
                store.Capacity,
                store.Mask));
            store.Return(item, MapModulo(
                (uint)Thread.GetCurrentProcessorId(),
                store.Capacity,
                store.Mask));
        }
    }

    private static void RunMultiplyHighProcessorAffinity(SmallStore store, int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
        {
            int item = store.Rent(MapMultiplyHigh(
                (uint)Thread.GetCurrentProcessorId(),
                store.Capacity,
                store.Mask));
            store.Return(item, MapMultiplyHigh(
                (uint)Thread.GetCurrentProcessorId(),
                store.Capacity,
                store.Mask));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int MapModulo(uint affinity, int capacity, int mask)
    {
        uint mixed = affinity * StripeHashMultiplier;
        return mask >= 0 ? (int)(mixed & (uint)mask) : (int)(mixed % (uint)capacity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int MapMultiplyHigh(uint affinity, int capacity, int mask)
    {
        uint mixed = affinity * StripeHashMultiplier;
        return mask >= 0
            ? (int)(mixed & (uint)mask)
            : (int)(((ulong)mixed * (uint)capacity) >> 32);
    }

    public enum Workload
    {
        SameThread,
        CrossThreadHandoff,
    }

    private sealed class SmallStore
    {
        private const int CacheLineSlotStride = 16;
        private readonly int[] _items;

        internal SmallStore(int capacity)
        {
            Capacity = capacity;
            Mask = (capacity & (capacity - 1)) == 0 ? capacity - 1 : -1;
            _items = new int[capacity * CacheLineSlotStride];

            for (int i = 0; i < capacity; i++)
            {
                GetSlot(i) = i + 1;
            }
        }

        internal int Capacity { get; }

        internal int Mask { get; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int Rent(int startIndex)
        {
            int item = Interlocked.Exchange(ref GetSlot(startIndex), 0);
            if (item != 0)
            {
                return item;
            }

            for (int offset = 1; offset < Capacity; offset++)
            {
                int index = startIndex + offset;
                if (index >= Capacity)
                {
                    index -= Capacity;
                }

                item = Interlocked.Exchange(ref GetSlot(index), 0);
                if (item != 0)
                {
                    return item;
                }
            }

            return 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Return(int item, int startIndex)
        {
            if (Interlocked.CompareExchange(ref GetSlot(startIndex), item, 0) == 0)
            {
                return;
            }

            for (int offset = 1; offset < Capacity; offset++)
            {
                int index = startIndex + offset;
                if (index >= Capacity)
                {
                    index -= Capacity;
                }

                if (Interlocked.CompareExchange(ref GetSlot(index), item, 0) == 0)
                {
                    return;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref int GetSlot(int index) => ref _items[index * CacheLineSlotStride];
    }
}
