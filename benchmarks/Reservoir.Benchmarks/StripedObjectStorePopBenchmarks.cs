using System.Runtime.CompilerServices;
using System.Threading;
using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[BenchmarkCategory("Storage")]
[MemoryDiagnoser(displayGenColumns: false)]
public class StripedObjectStorePopBenchmarks
{
    private const int Capacity = 4_096;

    private StampedNodeStore? _atomicStore;
    private StampedNodeStore? _plainStore;

    [GlobalSetup]
    public void Setup()
    {
        _atomicStore = new StampedNodeStore(Capacity);
        _plainStore = new StampedNodeStore(Capacity);
        var atomicPayload = new Payload();
        var plainPayload = new Payload();

        _atomicStore.TryPush(atomicPayload);
        _plainStore.TryPush(plainPayload);
    }

    [Benchmark(Baseline = true)]
    public Payload AtomicExchange()
    {
        if (!_atomicStore!.TryPopAtomic(out Payload? item)
            || item is null
            || !_atomicStore.TryPush(item))
        {
            throw new InvalidOperationException("Atomic store lost its warm item.");
        }

        return item;
    }

    [Benchmark]
    public Payload PlainReadClear()
    {
        if (!_plainStore!.TryPopPlain(out Payload? item)
            || item is null
            || !_plainStore.TryPush(item))
        {
            throw new InvalidOperationException("Plain store lost its warm item.");
        }

        return item;
    }

    public sealed class Payload;

    private sealed class StampedNodeStore
    {
        private const int EmptyIndex = -1;

        private readonly Node[] _nodes;
        private long _availableHead;
        private long _freeHead;

        internal StampedNodeStore(int capacity)
        {
            _nodes = new Node[capacity];

            for (int i = 0; i < capacity; i++)
            {
                _nodes[i].Next = i + 1 < capacity ? i + 1 : EmptyIndex;
            }

            _availableHead = PackHead(0, EmptyIndex);
            _freeHead = PackHead(0, 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryPopAtomic(out Payload? item)
        {
            if (!TryTakeNode(ref _availableHead, out int nodeIndex))
            {
                item = null;
                return false;
            }

            item = Interlocked.Exchange(ref _nodes[nodeIndex].Item, null);
            PublishNode(ref _freeHead, nodeIndex);
            return item is not null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryPopPlain(out Payload? item)
        {
            if (!TryTakeNode(ref _availableHead, out int nodeIndex))
            {
                item = null;
                return false;
            }

            item = _nodes[nodeIndex].Item;
            _nodes[nodeIndex].Item = null;
            PublishNode(ref _freeHead, nodeIndex);
            return item is not null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryPush(Payload item)
        {
            if (!TryTakeNode(ref _freeHead, out int nodeIndex))
            {
                return false;
            }

            Volatile.Write(ref _nodes[nodeIndex].Item, item);
            PublishNode(ref _availableHead, nodeIndex);
            return true;
        }

        private bool TryTakeNode(ref long head, out int nodeIndex)
        {
            while (true)
            {
                long observedHead = Volatile.Read(ref head);
                nodeIndex = GetIndex(observedHead);
                if (nodeIndex == EmptyIndex)
                {
                    return false;
                }

                int nextIndex = Volatile.Read(ref _nodes[nodeIndex].Next);
                long updatedHead = NextHead(observedHead, nextIndex);
                if (Interlocked.CompareExchange(ref head, updatedHead, observedHead) == observedHead)
                {
                    return true;
                }
            }
        }

        private void PublishNode(ref long head, int nodeIndex)
        {
            while (true)
            {
                long observedHead = Volatile.Read(ref head);
                Volatile.Write(ref _nodes[nodeIndex].Next, GetIndex(observedHead));
                long updatedHead = NextHead(observedHead, nodeIndex);
                if (Interlocked.CompareExchange(ref head, updatedHead, observedHead) == observedHead)
                {
                    return;
                }
            }
        }

        private static long PackHead(int version, int index)
            => ((long)version << 32) | (uint)index;

        private static long NextHead(long observedHead, int index)
            => PackHead(unchecked((int)(observedHead >> 32) + 1), index);

        private static int GetIndex(long head) => (int)head;

        private struct Node
        {
            internal Payload? Item;
            internal int Next;
        }
    }
}
