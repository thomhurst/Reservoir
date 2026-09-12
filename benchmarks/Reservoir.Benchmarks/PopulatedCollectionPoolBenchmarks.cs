using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

/// <summary>Measures reuse after writing an item, including reset of allocated backing storage.</summary>
[MemoryDiagnoser]
public class PopulatedCollectionPoolBenchmarks
{
    private readonly DictionaryPool<int, int> _dictionaries = new();
    private readonly HashSetPool<int> _sets = new();
    private readonly QueuePool<int> _queues = new();
    private readonly StackPool<int> _stacks = new();
    private int _value = 42;

    [GlobalSetup]
    public void Warm()
    {
        _ = Dictionary();
        _ = HashSet();
        _ = Queue();
        _ = Stack();
        _ = DictionaryScoped();
        _ = HashSetScoped();
        _ = QueueScoped();
        _ = StackScoped();
    }

    [Benchmark]
    public int Dictionary()
    {
        Dictionary<int, int> item = _dictionaries.Rent();
        item.Add(_value, _value);
        int result = item.Count;
        _dictionaries.Return(item);
        return result;
    }

    [Benchmark]
    public int HashSet()
    {
        HashSet<int> item = _sets.Rent();
        item.Add(_value);
        int result = item.Count;
        _sets.Return(item);
        return result;
    }

    [Benchmark]
    public int Queue()
    {
        Queue<int> item = _queues.Rent();
        item.Enqueue(_value);
        int result = item.Count;
        _queues.Return(item);
        return result;
    }

    [Benchmark]
    public int Stack()
    {
        Stack<int> item = _stacks.Rent();
        item.Push(_value);
        int result = item.Count;
        _stacks.Return(item);
        return result;
    }

    [Benchmark]
    public int DictionaryScoped()
    {
        using DictionaryPool<int, int>.Lease lease = _dictionaries.RentScoped(out Dictionary<int, int> item);
        item.Add(_value, _value);
        return item.Count;
    }

    [Benchmark]
    public int HashSetScoped()
    {
        using HashSetPool<int>.Lease lease = _sets.RentScoped(out HashSet<int> item);
        item.Add(_value);
        return item.Count;
    }

    [Benchmark]
    public int QueueScoped()
    {
        using QueuePool<int>.Lease lease = _queues.RentScoped(out Queue<int> item);
        item.Enqueue(_value);
        return item.Count;
    }

    [Benchmark]
    public int StackScoped()
    {
        using StackPool<int>.Lease lease = _stacks.RentScoped(out Stack<int> item);
        item.Push(_value);
        return item.Count;
    }
}
