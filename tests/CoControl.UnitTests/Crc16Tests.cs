using System.Text;
using CoControl.Protocol;
using Xunit;

namespace CoControl.UnitTests;

public class Crc16Tests
{
    [Fact]
    public void Compute_StandardModbusVector_Matches()
    {
        // Canonical CRC16/Modbus check value for "123456789" is 0x4B37.
        byte[] data = Encoding.ASCII.GetBytes("123456789");
        Assert.Equal(0x4B37, Crc16.Compute(data));
    }

    [Fact]
    public void Compute_EmptyInput_ReturnsInit()
    {
        Assert.Equal(0xFFFF, Crc16.Compute(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Compute_SingleByteChange_ChangesCrc()
    {
        var a = new byte[136];
        var b = new byte[136];
        b[77] = 0x01;
        Assert.NotEqual(Crc16.Compute(a), Crc16.Compute(b));
    }

    [Fact]
    public void Verify_MatchingCrc_ReturnsTrue()
    {
        byte[] data = { 0x06, 0x04, 0xAA, 0x55 };
        Assert.True(Crc16.Verify(data, Crc16.Compute(data)));
        Assert.False(Crc16.Verify(data, (ushort)(Crc16.Compute(data) ^ 1)));
    }
}
