using System.Text;
using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
public class OversizedCollectionReturnBenchmarks
{
    private const int ElementsPerBatch = 262_144;
    private const int MaximumRetainedCapacity = 2_048;
    private readonly object _element = new();
    private readonly ListPool<object> _listPool = new(MaximumRetainedCapacity, 1);
    private readonly DictionaryPool<int, object> _dictionaryPool = new(MaximumRetainedCapacity, 1);
    private readonly HashSetPool<object> _hashSetPool = new(MaximumRetainedCapacity, 1);
    private readonly QueuePool<object> _queuePool = new(MaximumRetainedCapacity, 1);
    private readonly StackPool<object> _stackPool = new(MaximumRetainedCapacity, 1);
    private readonly StringBuilderPool _stringBuilderPool = new(MaximumRetainedCapacity, 1);
    private List<object>[] _lists = null!;
    private Dictionary<int, object>[] _dictionaries = null!;
    private HashSet<object>[] _hashSets = null!;
    private Queue<object>[] _queues = null!;
    private Stack<object>[] _stacks = null!;
    private StringBuilder[] _stringBuilders = null!;

    [Params(1_024, 65_536)]
    public int ElementCount { get; set; }

    // Preparing a fresh batch for each iteration keeps setup outside the measurement and makes
    // BenchmarkDotNet run the stateful benchmark once per iteration with an unroll factor of one.
    [IterationSetup(Target = nameof(ReturnList))]
    public void SetupList()
    {
        _lists = new List<object>[BatchSize];

        for (int i = 0; i < _lists.Length; i++)
        {
            _lists[i] = new List<object>(Enumerable.Repeat(_element, ElementCount));
        }
    }

    [IterationSetup(Target = nameof(ReturnDictionary))]
    public void SetupDictionary()
    {
        _dictionaries = new Dictionary<int, object>[BatchSize];

        for (int i = 0; i < _dictionaries.Length; i++)
        {
            var dictionary = new Dictionary<int, object>(ElementCount);

            for (int j = 0; j < ElementCount; j++)
            {
                dictionary.Add(j, _element);
            }

            _dictionaries[i] = dictionary;
        }
    }

    [IterationSetup(Target = nameof(ReturnHashSet))]
    public void SetupHashSet()
    {
        _hashSets = new HashSet<object>[BatchSize];

        for (int i = 0; i < _hashSets.Length; i++)
        {
            var hashSet = new HashSet<object>(ElementCount);

            for (int j = 0; j < ElementCount; j++)
            {
                hashSet.Add(new object());
            }

            _hashSets[i] = hashSet;
        }
    }

    [IterationSetup(Target = nameof(ReturnQueue))]
    public void SetupQueue()
    {
        _queues = new Queue<object>[BatchSize];

        for (int i = 0; i < _queues.Length; i++)
        {
            _queues[i] = new Queue<object>(Enumerable.Repeat(_element, ElementCount));
        }
    }

    [IterationSetup(Target = nameof(ReturnStack))]
    public void SetupStack()
    {
        _stacks = new Stack<object>[BatchSize];

        for (int i = 0; i < _stacks.Length; i++)
        {
            _stacks[i] = new Stack<object>(Enumerable.Repeat(_element, ElementCount));
        }
    }

    [IterationSetup(Target = nameof(ReturnStringBuilder))]
    public void SetupStringBuilder()
    {
        _stringBuilders = new StringBuilder[BatchSize];

        for (int i = 0; i < _stringBuilders.Length; i++)
        {
            _stringBuilders[i] = new StringBuilder(capacity: ElementCount)
                .Append('x', ElementCount);
        }
    }

    [Benchmark]
    public int ReturnList()
    {
        int remainingCount = 0;

        for (int i = 0; i < _lists.Length; i++)
        {
            _listPool.Return(_lists[i]);
            remainingCount += _lists[i].Count;
        }

        return remainingCount;
    }

    [Benchmark]
    public int ReturnDictionary()
    {
        int remainingCount = 0;

        for (int i = 0; i < _dictionaries.Length; i++)
        {
            _dictionaryPool.Return(_dictionaries[i]);
            remainingCount += _dictionaries[i].Count;
        }

        return remainingCount;
    }

    [Benchmark]
    public int ReturnHashSet()
    {
        int remainingCount = 0;

        for (int i = 0; i < _hashSets.Length; i++)
        {
            _hashSetPool.Return(_hashSets[i]);
            remainingCount += _hashSets[i].Count;
        }

        return remainingCount;
    }

    [Benchmark]
    public int ReturnQueue()
    {
        int remainingCount = 0;

        for (int i = 0; i < _queues.Length; i++)
        {
            _queuePool.Return(_queues[i]);
            remainingCount += _queues[i].Count;
        }

        return remainingCount;
    }

    [Benchmark]
    public int ReturnStack()
    {
        int remainingCount = 0;

        for (int i = 0; i < _stacks.Length; i++)
        {
            _stackPool.Return(_stacks[i]);
            remainingCount += _stacks[i].Count;
        }

        return remainingCount;
    }

    [Benchmark]
    public int ReturnStringBuilder()
    {
        int remainingCount = 0;

        for (int i = 0; i < _stringBuilders.Length; i++)
        {
            _stringBuilderPool.Return(_stringBuilders[i]);
            remainingCount += _stringBuilders[i].Length;
        }

        return remainingCount;
    }

    private int BatchSize => ElementsPerBatch / ElementCount;
}
