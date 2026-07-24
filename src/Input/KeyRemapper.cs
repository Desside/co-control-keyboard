using System;
using System.Threading.Tasks;
using CoControl.Service;

namespace CoControl.Input;

/// <summary>
/// A single remap entry: 4 bytes on device — [type][param1][param2][param3].
/// </summary>
public readonly record struct RemapEntry(RemapType Type, byte Param1 = 0, byte Param2 = 0, byte Param3 = 0)
{
    public static RemapEntry Default => new(RemapType.Default);
    public static RemapEntry Key(KeyCode key) => new(RemapType.Key, (byte)key);
    public static RemapEntry Combo(Modifiers mods, KeyCode key) => new(RemapType.Combo, (byte)mods, (byte)key);
    public static RemapEntry Media(ushort consumerUsage) =>
        new(RemapType.Media, (byte)(consumerUsage & 0xFF), (byte)(consumerUsage >> 8));
    public static RemapEntry Macro(int slot)
    {
        if (slot < 0 || slot >= FlashLayout.MacroSlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
        return new(RemapType.Macro, (byte)slot);
    }
    public static RemapEntry Disabled => new(RemapType.Disabled);

    public void WriteTo(Span<byte> dest)
    {
        if (dest.Length < FlashLayout.RemapEntrySize) throw new ArgumentException("Need 4 bytes");
        dest[0] = (byte)Type; dest[1] = Param1; dest[2] = Param2; dest[3] = Param3;
    }

    public static RemapEntry ReadFrom(ReadOnlySpan<byte> src)
    {
        if (src.Length < FlashLayout.RemapEntrySize) throw new ArgumentException("Need 4 bytes");
        return new((RemapType)src[0], src[1], src[2], src[3]);
    }
}

/// <summary>
/// Key remap table for the 88 physical keys (K1..K88 per KB.ini).
/// Serializes to a single flash page: 88 × 4 = 352 bytes.
/// </summary>
public sealed class KeyRemapper
{
    private readonly RemapEntry[] _table = new RemapEntry[FlashLayout.KeyCount];

    public const int TableSize = FlashLayout.KeyCount * FlashLayout.RemapEntrySize; // 352

    public RemapEntry this[int keyIndex]
    {
        get
        {
            ValidateIndex(keyIndex);
            return _table[keyIndex];
        }
        set
        {
            ValidateIndex(keyIndex);
            _table[keyIndex] = value;
        }
    }

    /// <summary>Resets every key to firmware default.</summary>
    public void Reset() => Array.Fill(_table, RemapEntry.Default);

    /// <summary>Serializes the table to its on-device byte layout.</summary>
    public byte[] Serialize()
    {
        var buf = new byte[TableSize];
        for (int i = 0; i < _table.Length; i++)
            _table[i].WriteTo(buf.AsSpan(i * FlashLayout.RemapEntrySize));
        return buf;
    }

    /// <summary>Restores a table from its on-device byte layout.</summary>
    public static KeyRemapper Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length < TableSize) throw new ArgumentException($"Need {TableSize} bytes");
        var remapper = new KeyRemapper();
        for (int i = 0; i < FlashLayout.KeyCount; i++)
            remapper._table[i] = RemapEntry.ReadFrom(data.Slice(i * FlashLayout.RemapEntrySize));
        return remapper;
    }

    /// <summary>
    /// Writes the remap table to the device (CRC-guarded flash page write).
    /// </summary>
    public Task ApplyAsync(ProtocolEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.WriteFlashPageAsync(FlashLayout.RemapPage, Serialize());
    }

    private static void ValidateIndex(int keyIndex)
    {
        if (keyIndex < 0 || keyIndex >= FlashLayout.KeyCount)
            throw new ArgumentOutOfRangeException(nameof(keyIndex), $"Key index must be 0..{FlashLayout.KeyCount - 1}");
    }
}
