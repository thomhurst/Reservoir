using System.Text;
using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
public class CollectionPoolAllocationBenchmarks
{
    private readonly ObjectPool<Payload, PayloadPolicy> _objectPool = new();
    private readonly ListPool<int> _listPool = new();
    private readonly DictionaryPool<int, int> _dictionaryPool = new();
    private readonly HashSetPool<int> _hashSetPool = new();
    private readonly QueuePool<int> _queuePool = new();
    private readonly StackPool<int> _stackPool = new();
    private readonly StringBuilderPool _stringBuilderPool = new();

    [GlobalSetup]
    public void WarmPools()
    {
        Payload payload = _objectPool.Rent();
        _objectPool.Return(payload);

        List<int> list = _listPool.Rent();
        _listPool.Return(list);

        Dictionary<int, int> dictionary = _dictionaryPool.Rent();
        _dictionaryPool.Return(dictionary);

        HashSet<int> hashSet = _hashSetPool.Rent();
        _hashSetPool.Return(hashSet);

        Queue<int> queue = _queuePool.Rent();
        _queuePool.Return(queue);

        Stack<int> stack = _stackPool.Rent();
        _stackPool.Return(stack);

        StringBuilder builder = _stringBuilderPool.Rent();
        _stringBuilderPool.Return(builder);
    }

    [Benchmark(Baseline = true)]
    public int ObjectPool()
    {
        Payload payload = _objectPool.Rent();
        int result = payload.Value;
        _objectPool.Return(payload);
        return result;
    }

    [Benchmark]
    public int ListPool()
    {
        List<int> list = _listPool.Rent();
        int result = list.Count;
        _listPool.Return(list);
        return result;
    }

    [Benchmark]
    public int DictionaryPool()
    {
        Dictionary<int, int> dictionary = _dictionaryPool.Rent();
        int result = dictionary.Count;
        _dictionaryPool.Return(dictionary);
        return result;
    }

    [Benchmark]
    public int HashSetPool()
    {
        HashSet<int> hashSet = _hashSetPool.Rent();
        int result = hashSet.Count;
        _hashSetPool.Return(hashSet);
        return result;
    }

    [Benchmark]
    public int QueuePool()
    {
        Queue<int> queue = _queuePool.Rent();
        int result = queue.Count;
        _queuePool.Return(queue);
        return result;
    }

    [Benchmark]
    public int StackPool()
    {
        Stack<int> stack = _stackPool.Rent();
        int result = stack.Count;
        _stackPool.Return(stack);
        return result;
    }

    [Benchmark]
    public int StringBuilderPool()
    {
        StringBuilder builder = _stringBuilderPool.Rent();
        int result = builder.Length;
        _stringBuilderPool.Return(builder);
        return result;
    }

    public sealed class Payload
    {
        public int Value { get; set; }
    }

    public readonly struct PayloadPolicy : IPooledObjectPolicy<Payload>
    {
        public Payload Create() => new();

        public bool TryReset(Payload obj)
        {
            obj.Value = 0;
            return true;
        }
    }
}
