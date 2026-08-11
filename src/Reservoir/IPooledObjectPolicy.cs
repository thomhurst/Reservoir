namespace Reservoir;

/// <summary>
/// Creates pooled objects and prepares returned objects for reuse.
/// </summary>
/// <typeparam name="T">The reference type stored by the pool.</typeparam>
public interface IPooledObjectPolicy<T>
    where T : class
{
    /// <summary>Creates an object when the pool is empty.</summary>
    T Create();

    /// <summary>
    /// Resets an object before it is retained. Returns <see langword="false"/> to discard it.
    /// </summary>
    bool TryReset(T obj);
}
