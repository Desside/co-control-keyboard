using System.Collections.Concurrent;
using CoControl.HAL;
using CoControl.Protocol;
using CoControl.Service;
using CoControl.State;
using Xunit;

namespace CoControl.UnitTests;

/// <summary>Fake HID device: records every packet, replays scripted responses.</summary>
internal sealed class FakeHidDevice : IHidDevice
{
    public readonly ConcurrentQueue<byte[]> Sent = new();
    public readonly ConcurrentQueue<byte[]> Responses = new();
    public volatile bool ThrowOnSet;

    public void SetFeature(byte[] report)
    {
        if (ThrowOnSet) throw new IOException("simulated USB failure");
        Sent.Enqueue((byte[])report.Clone());
    }

    public byte[] GetFeature(byte reportId = 0x06)
    {
        if (Responses.TryDequeue(out var resp)) return resp;
        var empty = new byte[520];
        empty[0] = reportId;
        return empty;
    }

    public List<byte> SentCommands()
    {
        var cmds = new List<byte>();
        foreach (var pkt in Sent) cmds.Add(pkt[1]);
        return cmds;
    }
}

public class ProtocolEngineTests : IDisposable
{
    private readonly string _tempPath = Path.Combine(Path.GetTempPath(), $"cocontrol-engine-{Guid.NewGuid():N}.bin");
    private readonly FakeHidDevice _device = new();
    private readonly ShadowConfig _shadow;

    public ProtocolEngineTests() => _shadow = new ShadowConfig(_tempPath);

    public void Dispose()
    {
        _shadow.Dispose();
        if (File.Exists(_tempPath)) File.Delete(_tempPath);
    }

    [Fact]
    public async Task UpdateConfig_SendsFullFlashSequence()
    {
        using var engine = new ProtocolEngine(_device, _shadow);

        bool wrote = await engine.UpdateConfigAsync(cfg => cfg[18] = 0x03);

        Assert.True(wrote);
        Assert.Equal(
            new byte[] { (byte)HidCommand.Begin, (byte)HidCommand.ConfigWrite, (byte)HidCommand.Apply, (byte)HidCommand.Finish },
            _device.SentCommands());
        Assert.False(_shadow.IsDirty); // committed after device accepted
    }

    [Fact]
    public async Task UpdateConfig_NoChanges_SendsNothing()
    {
        using var engine = new ProtocolEngine(_device, _shadow);

        bool wrote = await engine.UpdateConfigAsync(_ => { });

        Assert.False(wrote);
        Assert.Empty(_device.Sent);
    }

    [Fact]
    public async Task UpdateConfig_UsbFailure_KeepsShadowDirtyForRetry()
    {
        using var engine = new ProtocolEngine(_device, _shadow);
        _device.ThrowOnSet = true;

        await Assert.ThrowsAsync<IOException>(() => engine.UpdateConfigAsync(cfg => cfg[18] = 0x07));

        Assert.True(_shadow.IsDirty);          // not committed
        Assert.False(_shadow.HasPendingWrite); // aborted, retry possible

        _device.ThrowOnSet = false;
        Assert.True(await engine.UpdateConfigAsync(_ => { })); // retry flushes pending diff
        Assert.False(_shadow.IsDirty);
    }

    [Fact]
    public async Task UpdateConfig_EmbedsValidCrc()
    {
        using var engine = new ProtocolEngine(_device, _shadow);
        await engine.UpdateConfigAsync(cfg => cfg[18] = 0x03);

        var dataPkt = _device.Sent.Single(p => p[1] == (byte)HidCommand.ConfigWrite);
        byte[] payload = dataPkt[8..144]; // Span locals are not allowed in async methods
        ushort crcInPacket = (ushort)(dataPkt[2] | (dataPkt[3] << 8));

        Assert.True(Crc16.Verify(payload, crcInPacket));
    }

    [Fact]
    public async Task WriteFlashPage_SendsGuardedSequence()
    {
        using var engine = new ProtocolEngine(_device, _shadow);
        await engine.WriteFlashPageAsync(0x02, new byte[352]);

        Assert.Equal(
            new byte[] { (byte)HidCommand.Begin, (byte)HidCommand.ConfigWrite, (byte)HidCommand.Apply, (byte)HidCommand.Finish },
            _device.SentCommands());
    }

    [Fact]
    public async Task SendWithResponse_ReadsAckWithReportId06()
    {
        using var engine = new ProtocolEngine(_device, _shadow);
        var ack = new byte[520];
        ack[0] = 0x06; ack[1] = (byte)HidCommand.ModelQuery; ack[8] = 0x42;
        _device.Responses.Enqueue(ack);

        var resp = await engine.SendWithResponseAsync(PacketBuilder.BuildModelQuery(), HidCommand.ModelQuery);

        Assert.Equal(0x42, resp[8]);
    }

    [Fact]
    public async Task SendWithResponse_WrongEcho_Throws()
    {
        using var engine = new ProtocolEngine(_device, _shadow);
        var wrong = new byte[520];
        wrong[0] = 0x06; wrong[1] = 0x00;
        _device.Responses.Enqueue(wrong);

        await Assert.ThrowsAsync<IOException>(() =>
            engine.SendWithResponseAsync(PacketBuilder.BuildModelQuery(), HidCommand.ModelQuery));
    }

    [Fact]
    public async Task Commands_AreSerialized_InOrder()
    {
        using var engine = new ProtocolEngine(_device, _shadow);
        var rgb = new byte[366];

        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
            tasks.Add(engine.SetDirectModeAsync(rgb));
        await Task.WhenAll(tasks);

        Assert.Equal(10, _device.Sent.Count);
        Assert.All(_device.SentCommands(), c => Assert.Equal((byte)HidCommand.DirectMode, c));
    }
}
