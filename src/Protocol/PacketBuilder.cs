using System;
using System.Buffers.Binary;

namespace CoControl.Protocol;

/// <summary>
/// Command opcodes per Aula F75 HID protocol.
/// </summary>
public enum HidCommand : byte
{
    ConfigWrite     = 0x04, // Flash write (requires CRC)
    ConfigRead      = 0x84, // Read internal state (136-byte response)
    PerKeyRgb       = 0x06, // Planar RGB: R[126] → G[126] → B[126]
    CustomProfile   = 0x0A, // Custom color profile (21 bytes/group)
    DirectMode      = 0x08, // Real-time LED update, bypasses Flash
    ModelQuery      = 0x82, // Hardware identification
    Apply           = 0x02, // Apply config (after write)
    Finish          = 0xF0, // Finish session
    Begin           = 0x18, // Begin session
}

/// <summary>
/// Represents a parsed 520-byte Feature Report.
/// </summary>
public readonly struct HidPacket
{
    public readonly byte ReportId;
    public readonly HidCommand Command;
    public readonly ReadOnlyMemory<byte> Payload;

    public HidPacket(byte reportId, HidCommand command, ReadOnlyMemory<byte> payload)
    {
        ReportId = reportId;
        Command = command;
        Payload = payload;
    }

    public static HidPacket Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length != 520) throw new ArgumentException("HID packet must be 520 bytes");
        return new HidPacket(data[0], (HidCommand)data[1], data[2..].ToArray());
    }
}

/// <summary>
/// Builds 520-byte Feature Reports from structured commands.
/// </summary>
public static class PacketBuilder
{
    public const int PACKET_SIZE = 520;
    public const byte REPORT_ID = 0x06;

    /// <summary>
    /// Creates a Model Query packet (CMD 0x82).
    /// </summary>
    public static byte[] BuildModelQuery()
    {
        var pkt = new byte[PACKET_SIZE];
        pkt[0] = REPORT_ID;
        pkt[1] = (byte)HidCommand.ModelQuery;
        pkt[2] = 0x01; pkt[3] = 0x00; pkt[4] = 0x01; pkt[5] = 0x00; pkt[6] = 0x06;
        return pkt;
    }

    /// <summary>
    /// Creates a Begin Session packet (CMD 0x18).
    /// </summary>
    public static byte[] BuildBeginSession()
    {
        var pkt = new byte[PACKET_SIZE];
        pkt[0] = REPORT_ID;
        pkt[1] = (byte)HidCommand.Begin;
        return pkt;
    }

    /// <summary>
    /// Creates a Config Read packet (CMD 0x84) — requests 136-byte state.
    /// </summary>
    public static byte[] BuildConfigRead()
    {
        var pkt = new byte[PACKET_SIZE];
        pkt[0] = REPORT_ID;
        pkt[1] = (byte)HidCommand.ConfigRead;
        pkt[4] = 0x01; pkt[6] = 0x80; // length = 0x0080 = 128 bytes
        return pkt;
    }

    /// <summary>
    /// Creates a Config Write packet (CMD 0x04) — writes modified 136-byte config.
    /// CRC16 of the payload is embedded at bytes 2–3 (little-endian) so the
    /// firmware can validate integrity before touching Flash.
    /// </summary>
    public static byte[] BuildConfigWrite(ReadOnlySpan<byte> config136, ushort crc)
    {
        if (config136.Length != 136) throw new ArgumentException("Config must be 136 bytes");
        var pkt = new byte[PACKET_SIZE];
        pkt[0] = REPORT_ID;
        pkt[1] = (byte)HidCommand.ConfigWrite;
        BinaryPrimitives.WriteUInt16LittleEndian(pkt.AsSpan(2, 2), crc);
        pkt[4] = 0x01; pkt[6] = 0x80; // length = 128 bytes
        config136.CopyTo(pkt.AsSpan(8, 136));
        return pkt;
    }

    /// <summary>
    /// Creates a Flash page write packet (CMD 0x04) for regions beyond the
    /// 136-byte config (key remap table, macro storage).
    /// Page index goes to byte 5; payload (max 512 bytes) starts at byte 8.
    /// CRC16 of the payload is embedded at bytes 2–3 (little-endian).
    /// NOTE: page numbering per FlashLayout — verify against USB captures
    /// before first hardware write.
    /// </summary>
    public static byte[] BuildFlashPageWrite(byte page, ReadOnlySpan<byte> data, ushort crc)
    {
        if (data.Length == 0 || data.Length > PACKET_SIZE - 8)
            throw new ArgumentException($"Page data must be 1..{PACKET_SIZE - 8} bytes");
        var pkt = new byte[PACKET_SIZE];
        pkt[0] = REPORT_ID;
        pkt[1] = (byte)HidCommand.ConfigWrite;
        BinaryPrimitives.WriteUInt16LittleEndian(pkt.AsSpan(2, 2), crc);
        pkt[4] = 0x01;
        pkt[5] = page;
        BinaryPrimitives.WriteUInt16LittleEndian(pkt.AsSpan(6, 2), (ushort)data.Length);
        data.CopyTo(pkt.AsSpan(8));
        return pkt;
    }

    /// <summary>
    /// Creates an Apply packet (CMD 0x02).
    /// </summary>
    public static byte[] BuildApply()
    {
        var pkt = new byte[PACKET_SIZE];
        pkt[0] = REPORT_ID;
        pkt[1] = (byte)HidCommand.Apply;
        return pkt;
    }

    /// <summary>
    /// Creates a Finish packet (CMD 0xF0).
    /// </summary>
    public static byte[] BuildFinish()
    {
        var pkt = new byte[PACKET_SIZE];
        pkt[0] = REPORT_ID;
        pkt[1] = (byte)HidCommand.Finish;
        return pkt;
    }

    /// <summary>
    /// Creates a Per-Key RGB packet (CMD 0x06) — Planar layout R[126] G[126] B[126].
    /// </summary>
    public static byte[] BuildPerKeyRgb(ReadOnlySpan<byte> red126, ReadOnlySpan<byte> green126, ReadOnlySpan<byte> blue126)
    {
        if (red126.Length != 126 || green126.Length != 126 || blue126.Length != 126)
            throw new ArgumentException("Each color plane must be exactly 126 bytes");

        var pkt = new byte[PACKET_SIZE];
        pkt[0] = REPORT_ID;
        pkt[1] = (byte)HidCommand.PerKeyRgb;
        pkt[4] = 0x01;
        pkt[6] = 0x7A; // LED count = 122
        pkt[7] = 0x01;

        // Planar layout: R[126] at offset 8, G[126] at offset 134, B[126] at offset 260
        red126.CopyTo(pkt.AsSpan(8, 126));
        green126.CopyTo(pkt.AsSpan(134, 126));
        blue126.CopyTo(pkt.AsSpan(260, 126));
        return pkt;
    }

    /// <summary>
    /// Creates a Direct Mode packet (CMD 0x08) — interleaved RGB for real-time animation.
    /// </summary>
    public static byte[] BuildDirectMode(ReadOnlySpan<byte> rgbInterleaved)
    {
        // 122 LEDs * 3 bytes = 366 bytes + header
        if (rgbInterleaved.Length != 366)
            throw new ArgumentException("Direct mode requires 366 bytes (122 LEDs × 3)");

        var pkt = new byte[PACKET_SIZE];
        pkt[0] = REPORT_ID;
        pkt[1] = (byte)HidCommand.DirectMode;
        pkt[4] = 0x01;
        pkt[6] = 0x7A; // 122 LEDs
        pkt[7] = 0x01;
        rgbInterleaved.CopyTo(pkt.AsSpan(8));
        return pkt;
    }

    /// <summary>
    /// Creates a Custom Profile packet (CMD 0x0A).
    /// </summary>
    public static byte[] BuildCustomProfile(ReadOnlySpan<byte> profileData)
    {
        var pkt = new byte[PACKET_SIZE];
        pkt[0] = REPORT_ID;
        pkt[1] = (byte)HidCommand.CustomProfile;
        pkt[6] = 0x00; pkt[7] = 0x02;
        int copyLen = Math.Min(profileData.Length, PACKET_SIZE - 8);
        profileData.Slice(0, copyLen).CopyTo(pkt.AsSpan(8));
        return pkt;
    }
}

/// <summary>
/// Parses device responses.
/// </summary>
public static class PacketParser
{
    /// <summary>
    /// Parses Config Read response (136 bytes expected).
    /// Returns the 136-byte config payload.
    /// </summary>
    public static byte[] ParseConfigRead(ReadOnlySpan<byte> response)
    {
        if (response.Length < 136) throw new ArgumentException("Response too short for config read");
        if (response[0] != 0x06 || response[1] != (byte)HidCommand.ConfigRead)
            throw new ArgumentException("Invalid config read response header");

        // Response structure: header(8) + config(128) = 136 bytes
        var config = new byte[136];
        response.Slice(0, 136).CopyTo(config);
        return config;
    }

    /// <summary>
    /// Parses Model Query response.
    /// </summary>
    public static (byte ModelByte, byte SubModel) ParseModelQuery(ReadOnlySpan<byte> response)
    {
        if (response.Length < 14) throw new ArgumentException("Response too short for model query");
        if (response[0] != 0x06 || response[1] != (byte)HidCommand.ModelQuery)
            throw new ArgumentException("Invalid model query response header");

        // Bytes 8 and 12-13 contain model info per protocol
        return (response[8], response[12]);
    }

    /// <summary>
    /// Validates a generic ACK response (echo of command).
    /// </summary>
    public static bool IsAck(ReadOnlySpan<byte> response, HidCommand expectedCmd)
    {
        return response.Length >= 2 && response[0] == 0x06 && response[1] == (byte)expectedCmd;
    }
}