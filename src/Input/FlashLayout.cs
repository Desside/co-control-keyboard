namespace CoControl.Input;

/// <summary>
/// Flash page layout for regions beyond the 136-byte config.
///
/// !! ASSUMPTION — VERIFY BEFORE FIRST HARDWARE WRITE !!
/// The exact page numbering of the SinoWealth MCU is not covered by the
/// public spec. These values are placeholders structured so that only this
/// file needs to change after sniffing the vendor software's USB traffic
/// (Wireshark + USBPcap on a remap/macro save action).
/// All writes go through the CRC-guarded Begin → Data → Apply → Finish
/// sequence, and the firmware rejects packets with a bad CRC, but a write
/// to a wrong page could still corrupt unrelated settings.
/// </summary>
public static class FlashLayout
{
    /// <summary>Page holding the 88-key remap table.</summary>
    public const byte RemapPage = 0x02;

    /// <summary>First page of macro storage; one macro slot per page.</summary>
    public const byte MacroPageBase = 0x04;

    /// <summary>Number of macro slots supported.</summary>
    public const int MacroSlotCount = 8;

    /// <summary>Bytes reserved per macro slot (fits one flash page write).</summary>
    public const int MacroSlotSize = 512;

    /// <summary>Bytes per key in the remap table.</summary>
    public const int RemapEntrySize = 4;

    /// <summary>Number of remappable keys (K1..K88 per KB.ini).</summary>
    public const int KeyCount = 88;

    public static byte MacroPage(int slot)
    {
        if (slot < 0 || slot >= MacroSlotCount)
            throw new System.ArgumentOutOfRangeException(nameof(slot));
        return (byte)(MacroPageBase + slot);
    }
}
