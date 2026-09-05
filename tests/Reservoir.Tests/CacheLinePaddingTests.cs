using System.Reflection;
using System.Runtime.CompilerServices;

namespace Reservoir.Tests;

public class CacheLinePaddingTests
{
    private const int CacheLineSize = 64;

    [Test]
    [Arguments(typeof(TrackedInstanceThreadLocalFrontTier<object>), "PaddedSlot")]
    [Arguments(typeof(InstanceThreadLocalFrontTier<object>), "PaddedSlot")]
    [Arguments(typeof(StripedObjectStore<object>), "PaddedStripe")]
    public async Task PaddedNestedTypesOccupyAtLeastTwoCacheLinesBeyondTheirFields(
        Type declaringType,
        string nestedTypeName)
    {
        Type paddedType = GetPaddedNestedType(declaringType, nestedTypeName);
        Func<object> create = paddedType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null,
            [typeof(int)],
            null) is { } capacityConstructor
            ? () => capacityConstructor.Invoke([1])
            : () => Activator.CreateInstance(paddedType, nonPublic: true)!;

        await AssertPaddedAllocation(paddedType, create);
    }

    [Test]
    public async Task PaddedLeaseStateOccupiesAtLeastTwoCacheLinesBeyondItsFields()
        => await AssertPaddedAllocation(
            typeof(PaddedScopedPoolLeaseState),
            static () => new PaddedScopedPoolLeaseState());

    [Test]
    public async Task HotSlotFieldsStartOneCacheLinePastTheObjectHeader()
    {
        Type paddedType = GetPaddedNestedType(
            typeof(TrackedInstanceThreadLocalFrontTier<object>),
            "PaddedSlot");
        var slot = (TrackedInstanceThreadLocalFrontTier<object>.Slot)Activator.CreateInstance(
            paddedType,
            nonPublic: true)!;

        // The first field of any object sits right after its method-table pointer, so its
        // distance from the Item field gives Item's offset from the object start.
        long itemOffset = (long)Unsafe.ByteOffset(
            ref Unsafe.As<RawData>(slot).Data,
            ref Unsafe.As<object?, byte>(ref slot.Item))
            + IntPtr.Size;

        await Assert.That(itemOffset).IsGreaterThanOrEqualTo(CacheLineSize);
    }

    // A nested type of a constructed generic type comes back as its open definition.
    private static Type GetPaddedNestedType(Type declaringType, string nestedTypeName)
        => declaringType
            .GetGenericTypeDefinition()
            .GetNestedType(nestedTypeName, BindingFlags.NonPublic)!
            .MakeGenericType(declaringType.GetGenericArguments());

    private static async Task AssertPaddedAllocation(Type paddedType, Func<object> create)
    {
        int fieldBytes = 0;
        for (Type? type = paddedType; type is not null; type = type.BaseType)
        {
            foreach (FieldInfo field in type.GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                | BindingFlags.DeclaredOnly))
            {
                if (field.FieldType == typeof(CacheLinePad)
                    || field.Name == "_leadingPad"
                    || field.Name == "_trailingPad")
                {
                    continue;
                }

                fieldBytes += field.FieldType.IsValueType
                    ? Math.Max(IntPtr.Size, System.Runtime.InteropServices.Marshal.SizeOf(
                        field.FieldType == typeof(bool) ? typeof(byte) : field.FieldType))
                    : IntPtr.Size;
            }
        }

        const int Samples = 256;
        var keep = new object[Samples];
        _ = create();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Samples; i++)
        {
            keep[i] = create();
        }

        long allocatedPerObject = (GC.GetAllocatedBytesForCurrentThread() - before) / Samples;
        GC.KeepAlive(keep);

        // Header, leading pad, the hot fields, then a trailing pad.
        int minimumSize = IntPtr.Size + CacheLineSize + fieldBytes + CacheLineSize;
        await Assert.That(allocatedPerObject).IsGreaterThanOrEqualTo(minimumSize);
    }

    private sealed class RawData
    {
#pragma warning disable CS0649 // Only ever read through Unsafe.As.
        public byte Data;
#pragma warning restore CS0649
    }
}
