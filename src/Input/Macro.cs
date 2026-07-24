using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading.Tasks;
using CoControl.Service;

namespace CoControl.Input;

public enum MacroEventType : byte
{
    KeyDown = 0x01,
    KeyUp = 0x02,
    Delay = 0x03,
}

/// <summary>
/// One macro event: 4 bytes on device — [type][key][delay LE16].
/// KeyDown/KeyUp use Key; Delay uses DelayMs.
/// </summary>
public readonly record struct MacroEvent(MacroEventType Type, KeyCode Key = KeyCode.None, ushort DelayMs = 0)
{
    public const int Size = 4;

    public static MacroEvent Down(KeyCode key) => new(MacroEventType.KeyDown, key);
    public static MacroEvent Up(KeyCode key) => new(MacroEventType.KeyUp, key);
    public static MacroEvent Wait(ushort ms) => new(MacroEventType.Delay, KeyCode.None, ms);

    public void WriteTo(Span<byte> dest)
    {
        if (dest.Length < Size) throw new ArgumentException("Need 4 bytes");
        dest[0] = (byte)Type;
        dest[1] = (byte)Key;
        BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(2, 2), DelayMs);
    }

    public static MacroEvent ReadFrom(ReadOnlySpan<byte> src)
    {
        if (src.Length < Size) throw new ArgumentException("Need 4 bytes");
        return new((MacroEventType)src[0], (KeyCode)src[1],
            BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(2, 2)));
    }
}

/// <summary>
/// A macro: ordered list of key events with delays.
/// On-device layout (one flash page / slot, max 512 bytes):
///   [0]    repeat count (0 = while held)
///   [1]    reserved
///   [2..3] event count LE16
///   [4..]  events, 4 bytes each (max 127 events per slot)
/// </summary>
public sealed class Macro
{
    public const int HeaderSize = 4;
    public const int MaxEvents = (FlashLayout.MacroSlotSize - HeaderSize) / MacroEvent.Size; // 127

    private readonly List<MacroEvent> _events = new();

    /// <summary>0 = repeat while key held; otherwise fixed repeat count.</summary>
    public byte RepeatCount { get; set; } = 1;

    public IReadOnlyList<MacroEvent> Events => _events;

    public Macro Add(MacroEvent e)
    {
        if (_events.Count >= MaxEvents)
            throw new InvalidOperationException($"Macro slot holds at most {MaxEvents} events");
        _events.Add(e);
        return this;
    }

    /// <summary>Convenience: press+release with an optional inter-event delay.</summary>
    public Macro Tap(KeyCode key, ushort delayMs = 0)
    {
        Add(MacroEvent.Down(key));
        if (delayMs > 0) Add(MacroEvent.Wait(delayMs));
        Add(MacroEvent.Up(key));
        return this;
    }

    /// <summary>Convenience: types a string of A–Z / 0–9 / space characters.</summary>
    public Macro Type(string text, ushort perKeyDelayMs = 10)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (char c in text)
        {
            KeyCode key = char.ToUpperInvariant(c) switch
            {
                >= 'A' and <= 'Z' => (KeyCode)((byte)KeyCode.A + (char.ToUpperInvariant(c) - 'A')),
                '0' => KeyCode.D0,
                >= '1' and <= '9' => (KeyCode)((byte)KeyCode.D1 + (c - '1')),
                ' ' => KeyCode.Space,
                _ => throw new ArgumentException($"Unsupported character '{c}' — add events manually"),
            };
            Tap(key, perKeyDelayMs);
        }
        return this;
    }

    public byte[] Serialize()
    {
        var buf = new byte[HeaderSize + _events.Count * MacroEvent.Size];
        buf[0] = RepeatCount;
        buf[1] = 0x00;
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(2, 2), (ushort)_events.Count);
        for (int i = 0; i < _events.Count; i++)
            _events[i].WriteTo(buf.AsSpan(HeaderSize + i * MacroEvent.Size));
        return buf;
    }

    public static Macro Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize) throw new ArgumentException("Macro data too short");
        int count = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(2, 2));
        if (data.Length < HeaderSize + count * MacroEvent.Size)
            throw new ArgumentException("Macro data truncated");

        var macro = new Macro { RepeatCount = data[0] };
        for (int i = 0; i < count; i++)
            macro._events.Add(MacroEvent.ReadFrom(data.Slice(HeaderSize + i * MacroEvent.Size)));
        return macro;
    }

    /// <summary>
    /// Writes the macro to a device slot (CRC-guarded flash page write).
    /// </summary>
    public Task ApplyAsync(ProtocolEngine engine, int slot)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.WriteFlashPageAsync(FlashLayout.MacroPage(slot), Serialize());
    }
}
