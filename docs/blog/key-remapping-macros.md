# Beyond the Key: Building a Robust Key Remapping and Macro System

## Opening

**The la**g in standard keyboard software often comes from a layer of "virtual drivers" that intercept every keystroke. But for hardware-level customization, the goal is different: you want the *keyboard itself* to change what it sends over the wire. 

**The insight:** To implement custom remapping and macros, you aren't changing the software; you are editing the keyboard's internal Flash memory. You must transform a high-level intent ("Press Shift+C") into a binary blob that the keyboard's MCU understands, all while staying within strict page-size limits.

**What you'll learn:** How to design a serialization system for key remaps, implement an event-based macro engine, and manage Flash page layouts to ensure hardware stability.

---

## Body

### 1. The Remap Table: From Intent to Byte

The keyboard doesn't see "Keys"; it sees indices. A remap table is essentially an array where the index is the physical key and the value is the desired output.

We use a `RemapEntry` record to encapsulate the type of action. A single entry is exactly 4 bytes: `[Type][Param1][Param2][Param3]`.

```csharp
public readonly record struct RemapEntry(RemapType Type, byte Param1 = 0, byte Param la2 = 0, byte Param3 = 0)
{
    public static RemapEntry Combo(Modifiers mods, KeyCode key) 
        => new(RemapType.Combo, (byte)mods, (byte)key);
    
    public void WriteTo(Span<byte> dest)
    {
        dest[0] = (byte)Type; dest[1] = Param1; dest[2] = Param la2; dest[3] = Param3;
    }
}
```

By using a fixed 4-byte size, we can treat the entire 88-key table as a single contiguous block of 352 bytes, making it trivial to calculate CRC and write to a single Flash page.

---

### 2. Macro Architecture: Event-Based Sequencing

Macros are more complex than simple remaps because they involve *time*. A macro isn't just a key; it's a sequence of `Down` $\to$ `Wait` $\to$ `Up` events.

We implement macros as a list of `MacroEvent` objects. To keep the memory footprint small, each event is packed into 4 bytes.

**Macro Event Structure:** `[Type][KeyCode][DelayLow][DelayHigh]`

```csharp
public enum MacroEventType : byte { KeyDown = 0x01, KeyUp = 0x02, Delay = 0x03 }

public readonly record struct MacroEvent(MacroEventType Type, KeyCode Key, ushort DelayMs)
{
    public void WriteTo(Span<byte> dest)
    {
        dest[0] = (byte)Type;
        dest[1] = (byte)Key;
        BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(2, 2), DelayMs);
    }
}
```

This allows the keyboard's MCU to simply iterate through the list and execute the events without needing a complex parser.

---

### 3. Flash Layout: The Danger of "Blind Writes"

The biggest risk in hardware customization is writing to the wrong memory address. The Aula F75 uses a SinoWealth MCU with a specific page layout.

We define a `FlashLayout` class that acts as the "Source of Truth" for memory addresses. Instead of hardcoding magic numbers throughout the app, we use these constants to calculate offsets.

```csharp
public static class FlashLayout
{
    public const byte RemapPage = 0x02;
    public const byte MacroPageBase = 0x04;
    public const int MacroSlotSize = 512; // Fits exactly one flash page write
}
```

**Crucial Rule:** Every write must follow the `Begin → Data → Apply → Finish` sequence. Skipping a step or writing past the 512-byte boundary can corrupt the device's bootloader or factory settings.

---

### 4. Serialization: Turning Objects into Binary

To move a `Macro` object from C# to the keyboard, we must serialize it into a raw byte array. We use a header-based approach:
`[RepeatCount][Reserved][EventCount LE16][Events...]`

```csharp
public byte[] Serialize()
{
    var buf = new byte[HeaderSize + _events.Count * MacroEvent.Size];
    buf[0] = RepeatCount;
    BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(2, 2), (ushort)_events.Count);
    for (int i = 0; i < _events.Count; i++)
        _events[i].WriteTo(buf.AsSpan(HeaderSize + i * MacroEvent.Size));
    return buf;
}
```

This ensures that the firmware knows exactly how many events to read and whether to loop the macro while the key is held.

---

## Closing

**Key Takeaway:** Hardware remapping is an exercise in memory constraints. By using fixed-size records and a strict page-based layout, we can provide a high-level "Macro Editor" experience while maintaining the binary precision required by the MCU.

**Action for you:** When designing for embedded hardware, always start with the memory map. Define your page sizes and offsets first; your C# classes should be mirrors of the hardware's memory, not abstractions that hide it.

---

## Honest Trade-offs

| Decision | Benefit | Cost |
|----------|---------|------|
| **Fixed 4-byte Entry** | Simple indexing; fast serialization | Wastes bytes for simple keys (only need 1 byte) |
| **Page-based Writes** | Matches MCU Flash architecture | Cannot write a single byte; must write the whole page |
| **SinoWealth Assumptions**| Faster development; no need for full SDK | Risk of corruption if the firmware version changes the layout |
| **In-memory Table** | Instant UI updates | Requires a full sync from device on startup |
