namespace Reservoir;

/// <summary>Creates and resets poolable objects through <see cref="IResettable"/>.</summary>
/// <typeparam name="T">The resettable reference type stored by the pool.</typeparam>
public readonly struct ResettablePooledObjectPolicy<T> : IPooledObjectPolicy<T>
    where T : class, IResettable, new()
{
    /// <inheritdoc />
    public T Create() => new();

    /// <inheritdoc />
    public bool TryReset(T obj) => obj.TryReset();
}
