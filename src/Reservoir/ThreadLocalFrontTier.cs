#nullable enable

using System;
using System.Runtime.CompilerServices;

namespace Reservoir;

internal static class ThreadLocalFrontTier<T, TPolicy>
    where T : class
    where TPolicy : struct, IPooledObjectPolicy<T>
{
    [ThreadStatic]
    private static T? _item;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static T Rent(ObjectPool<T, TPolicy> fallback)
    {
        T? item = _item;
        if (item is null)
        {
            return fallback.RentWithoutLifecycle();
        }

        _item = null;
        return item;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryReturn(T item)
    {
        if (_item is not null)
        {
            return false;
        }

        _item = item;
        return true;
    }
}
