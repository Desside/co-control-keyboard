using System.Buffers.Binary;
using CoControl.Protocol;
using Xunit;

namespace CoControl.UnitTests;

public class PacketBuilderTests
{
    [Fact]
    public void AllPackets_Are520Bytes_WithReportId06()
    {
        var packets = new[]
        {
            PacketBuilder.BuildModelQuery(),
            PacketBuilder.BuildBeginSession(),
            PacketBuilder.BuildConfigRead(),
            PacketBuilder.BuildApply(),
            PacketBuilder.BuildFinish(),
            PacketBuilder.BuildConfigWrite(new byte[136], 0x1234),
            PacketBuilder.BuildFlashPageWrite(0x02, new byte[352], 0x5678),
            PacketBuilder.BuildPerKeyRgb(new byte[126], new byte[126], new byte[126]),
            PacketBuilder.BuildDirectMode(new byte[366]),
        };

        foreach (var pkt in packets)
        {
            Assert.Equal(PacketBuilder.PACKET_SIZE, pkt.Length);
            Assert.Equal(PacketBuilder.REPORT_ID, pkt[0]);
        }
    }

    [Fact]
    public void ConfigWrite_EmbedsCrcAtBytes2And3()
    {
        var config = new byte[136];
        config[18] = 0x03;
        ushort crc = Crc16.Compute(config);

        var pkt = PacketBuilder.BuildConfigWrite(config, crc);

        Assert.Equal((byte)HidCommand.ConfigWrite, pkt[1]);
        Assert.Equal(crc, BinaryPrimitives.ReadUInt16LittleEndian(pkt.AsSpan(2, 2)));
        Assert.Equal(0x03, pkt[8 + 18]); // payload at offset 8
    }

    [Fact]
    public void ConfigWrite_RejectsWrongSize()
    {
        Assert.Throws<ArgumentException>(() => PacketBuilder.BuildConfigWrite(new byte[135], 0));
        Assert.Throws<ArgumentException>(() => PacketBuilder.BuildConfigWrite(new byte[137], 0));
    }

    [Fact]
    public void FlashPageWrite_SetsPageAndLength()
    {
        var data = new byte[352];
        data[0] = 0xAA;
        var pkt = PacketBuilder.BuildFlashPageWrite(0x02, data, 0xBEEF);

        Assert.Equal((byte)HidCommand.ConfigWrite, pkt[1]);
        Assert.Equal(0xBEEF, BinaryPrimitives.ReadUInt16LittleEndian(pkt.AsSpan(2, 2)));
        Assert.Equal(0x02, pkt[5]);
        Assert.Equal(352, BinaryPrimitives.ReadUInt16LittleEndian(pkt.AsSpan(6, 2)));
        Assert.Equal(0xAA, pkt[8]);
    }

    [Fact]
    public void FlashPageWrite_RejectsOversizedPayload()
    {
        Assert.Throws<ArgumentException>(() => PacketBuilder.BuildFlashPageWrite(0, new byte[513], 0));
        Assert.Throws<ArgumentException>(() => PacketBuilder.BuildFlashPageWrite(0, Array.Empty<byte>(), 0));
    }

    [Fact]
    public void PerKeyRgb_PlacesPlanesAtCorrectOffsets()
    {
        var r = new byte[126]; var g = new byte[126]; var b = new byte[126];
        r[0] = 0x11; g[0] = 0x22; b[0] = 0x33;

        var pkt = PacketBuilder.BuildPerKeyRgb(r, g, b);

        Assert.Equal(0x11, pkt[8]);       // R plane at 8
        Assert.Equal(0x22, pkt[134]);     // G plane at 134
        Assert.Equal(0x33, pkt[260]);     // B plane at 260
    }

    [Fact]
    public void ParseConfigRead_RejectsBadHeader()
    {
        var resp = new byte[520];
        resp[0] = 0x06; resp[1] = 0x00; // wrong command echo
        Assert.Throws<ArgumentException>(() => PacketParser.ParseConfigRead(resp));
    }

    [Fact]
    public void IsAck_MatchesCommandEcho()
    {
        var resp = new byte[520];
        resp[0] = 0x06; resp[1] = (byte)HidCommand.ConfigRead;
        Assert.True(PacketParser.IsAck(resp, HidCommand.ConfigRead));
        Assert.False(PacketParser.IsAck(resp, HidCommand.ModelQuery));
    }
}
