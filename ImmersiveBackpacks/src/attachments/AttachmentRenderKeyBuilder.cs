#nullable enable
using Vintagestory.API.Common;

namespace ImmersiveModularBackpacks.Attachments;

/// <summary>Builds a stable 64-bit key from the state that affects attachment rendering.</summary>
public struct AttachmentRenderKeyBuilder
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    private ulong hash;
    private bool initialized;

    public void Add(bool value)
    {
        AddRaw(1);
        AddRaw(value ? (byte)1 : (byte)0);
    }

    public void Add(int value)
    {
        AddRaw(2);
        AddRaw((byte)value);
        AddRaw((byte)(value >> 8));
        AddRaw((byte)(value >> 16));
        AddRaw((byte)(value >> 24));
    }

    public void Add(ulong value)
    {
        AddRaw(3);
        for (int shift = 0; shift < 64; shift += 8)
            AddRaw((byte)(value >> shift));
    }

    public void Add(string value)
    {
        AddRaw(4);
        if (value == null)
        {
            AddRaw(0);
            return;
        }

        AddRaw(1);
        AddLength(value.Length);
        foreach (char c in value)
        {
            AddRaw((byte)c);
            AddRaw((byte)(c >> 8));
        }
    }

    /// <summary>Adds the stack's persisted state, including class, id, quantity, and attribute tree.</summary>
    public void Add(ItemStack stack)
    {
        AddRaw(5);
        if (stack == null)
        {
            AddRaw(0);
            return;
        }

        AddRaw(1);
        byte[] bytes = stack.ToBytes();
        AddLength(bytes.Length);
        foreach (byte value in bytes) AddRaw(value);
    }

    public readonly ulong Build()
    {
        ulong value = initialized ? hash : OffsetBasis;
        value ^= value >> 33;
        value *= 0xff51afd7ed558ccdUL;
        value ^= value >> 33;
        value *= 0xc4ceb9fe1a85ec53UL;
        return value ^ (value >> 33);
    }

    private void AddLength(int value)
    {
        AddRaw((byte)value);
        AddRaw((byte)(value >> 8));
        AddRaw((byte)(value >> 16));
        AddRaw((byte)(value >> 24));
    }

    private void AddRaw(byte value)
    {
        if (!initialized)
        {
            hash = OffsetBasis;
            initialized = true;
        }

        hash ^= value;
        hash *= Prime;
    }
}
