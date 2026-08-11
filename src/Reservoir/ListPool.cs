namespace Reservoir;

/// <summary>Provides pools of reusable <see cref="List{T}"/> instances.</summary>
/// <typeparam name="T">The element type.</typeparam>
public sealed class ListPool<T>
{
    private readonly ObjectPool<List<T>, Policy> _pool;

    /// <summary>Gets the default largest list capacity retained by a pool.</summary>
    public const int DefaultMaximumRetainedCapacity = 1024;

    /// <summary>Gets the shared pool.</summary>
    public static ListPool<T> Shared { get; } = new();

    /// <summary>Initializes a pool with default limits.</summary>
    public ListPool()
        : this(DefaultMaximumRetainedCapacity, ObjectPool<List<T>, Policy>.DefaultMaximumRetained)
    {
    }

    /// <summary>Initializes a pool with a custom maximum retained list capacity.</summary>
    public ListPool(int maxRetainedCapacity)
        : this(maxRetainedCapacity, ObjectPool<List<T>, Policy>.DefaultMaximumRetained)
    {
    }

    /// <summary>Initializes a pool with custom item and list capacity limits.</summary>
    public ListPool(int maxRetainedCapacity, int maxCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetainedCapacity);
        MaximumRetainedCapacity = maxRetainedCapacity;
        _pool = new ObjectPool<List<T>, Policy>(new Policy(maxRetainedCapacity), maxCapacity);
    }

    /// <summary>Gets the maximum number of lists retained by this pool.</summary>
    public int MaximumRetained => _pool.MaximumRetained;

    /// <summary>Gets the largest list capacity retained by this pool.</summary>
    public int MaximumRetainedCapacity { get; }

    /// <summary>Rents an empty list.</summary>
    public List<T> Rent() => _pool.Rent();

    /// <summary>Clears and returns a list, discarding it when its capacity is too large.</summary>
    public void Return(List<T> list) => _pool.Return(list);

    private readonly struct Policy(int maxRetainedCapacity) : IPooledObjectPolicy<List<T>>
    {
        public List<T> Create() => [];

        public bool TryReset(List<T> obj)
        {
            obj.Clear();
            return obj.Capacity <= maxRetainedCapacity;
        }
    }
}
