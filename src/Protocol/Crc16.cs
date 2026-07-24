using System;

namespace CoControl.Protocol;

/// <summary>
/// CRC16 (Modbus variant: poly 0xA001 reflected, init 0xFFFF).
/// Used to guard every Flash write (CMD 0x04) against corruption.
/// </summary>
public static class Crc16
{
    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 0x0001) != 0)
                    crc = (ushort)((crc >> 1) ^ 0xA001);
                else
                    crc >>= 1;
            }
        }
        return crc;
    }

    /// <summary>Verifies that <paramref name="expected"/> matches the CRC of <paramref name="data"/>.</summary>
    public static bool Verify(ReadOnlySpan<byte> data, ushort expected) => Compute(data) == expected;
}
