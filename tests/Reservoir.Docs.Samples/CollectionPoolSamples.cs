namespace Reservoir.Docs.Samples.CollectionPools;

using System.Text;
using Reservoir;

internal static class CollectionPoolExamples
{
    internal static void List()
    {
        var listPool = new ListPool<int>(
            maxRetainedCapacity: 256,
            maxCapacity: 32);

        List<int> list = listPool.Rent();
        try
        {
            list.Add(42);
        }
        finally
        {
            listPool.Return(list);
        }
    }

    internal static void Dictionary()
    {
        var pool = new DictionaryPool<string, int>(
            comparer: StringComparer.OrdinalIgnoreCase,
            maxRetainedCapacity: 512,
            maxCapacity: 16);
    }

    internal static string BuildString(int requestId)
    {
        StringBuilder builder = StringBuilderPool.Shared.Rent();
        try
        {
            builder.Append("request-").Append(requestId);
            return builder.ToString();
        }
        finally
        {
            StringBuilderPool.Shared.Return(builder);
        }
    }

    internal static void Scoped()
    {
        using ListPool<int>.Lease lease = ListPool<int>.Shared.RentScoped(out List<int> list);
        list.Add(42);
    }

    internal static void ThreadLocal()
    {
        List<int> list = ListPool<int>.ThreadLocalShared.Rent();
        try
        {
            list.Add(42);
        }
        finally
        {
            ListPool<int>.ThreadLocalShared.Return(list);
        }
    }
}
