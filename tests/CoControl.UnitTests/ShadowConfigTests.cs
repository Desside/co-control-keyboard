using CoControl.Protocol;
using CoControl.State;
using Xunit;

namespace CoControl.UnitTests;

public class ShadowConfigTests : IDisposable
{
    private readonly string _tempPath = Path.Combine(Path.GetTempPath(), $"cocontrol-test-{Guid.NewGuid():N}.bin");

    private ShadowConfig Create() => new(_tempPath);

    public void Dispose()
    {
        if (File.Exists(_tempPath)) File.Delete(_tempPath);
    }

    [Fact]
    public void FreshShadow_IsNotDirty()
    {
        using var shadow = Create();
        Assert.False(shadow.IsDirty);
        Assert.False(shadow.TryBeginWrite(out _, out _));
    }

    [Fact]
    public void SetByte_MarksDirty_OnlyOnChange()
    {
        using var shadow = Create();
        Assert.False(shadow.SetByte(10, 0x00)); // same value
        Assert.False(shadow.IsDirty);

        Assert.True(shadow.SetByte(10, 0x42));
        Assert.True(shadow.IsDirty);
    }

    [Fact]
    public void Mutate_TracksDirty_EvenThroughRawBuffer()
    {
        using var shadow = Create();
        // Regression: mutating via a raw buffer must not bypass dirty tracking.
        bool changed = shadow.Mutate(cfg => cfg[17] = 0x01);
        Assert.True(changed);
        Assert.True(shadow.IsDirty);
    }

    [Fact]
    public void TryBeginWrite_ReturnsSnapshotAndCorrectCrc()
    {
        using var shadow = Create();
        shadow.SetByte(18, 0x03);

        Assert.True(shadow.TryBeginWrite(out var config, out var crc));
        Assert.Equal(ShadowConfig.CONFIG_SIZE, config.Length);
        Assert.Equal(0x03, config[18]);
        Assert.Equal(Crc16.Compute(config), crc);
    }

    [Fact]
    public void CommitWrite_ClearsDirty()
    {
        using var shadow = Create();
        shadow.SetByte(18, 0x03);
        Assert.True(shadow.TryBeginWrite(out _, out _));

        shadow.CommitWrite();
        Assert.False(shadow.IsDirty);
        Assert.False(shadow.HasPendingWrite);
        Assert.False(shadow.TryBeginWrite(out _, out _)); // nothing left to write
    }

    [Fact]
    public void AbortWrite_KeepsDirty_AllowsRetry()
    {
        using var shadow = Create();
        shadow.SetByte(18, 0x03);
        Assert.True(shadow.TryBeginWrite(out _, out _));

        shadow.AbortWrite();
        Assert.True(shadow.IsDirty);
        Assert.True(shadow.TryBeginWrite(out var retry, out _));
        Assert.Equal(0x03, retry[18]);
    }

    [Fact]
    public void MutationDuringFlight_StaysDirtyAfterCommit()
    {
        using var shadow = Create();
        shadow.SetByte(18, 0x03);
        Assert.True(shadow.TryBeginWrite(out _, out _));

        // Shadow moves on while the packet is "in flight".
        shadow.SetByte(18, 0x05);
        shadow.CommitWrite();

        Assert.True(shadow.IsDirty); // 0x05 still needs writing
        Assert.True(shadow.TryBeginWrite(out var next, out _));
        Assert.Equal(0x05, next[18]);
    }

    [Fact]
    public void SyncFromDevice_ClearsDirtyAndSetsBaseline()
    {
        using var shadow = Create();
        shadow.SetByte(18, 0x03);

        var device = new byte[ShadowConfig.CONFIG_SIZE];
        device[18] = 0x0A;
        shadow.SyncFromDevice(device);

        Assert.False(shadow.IsDirty);
        Assert.Equal(0x0A, shadow.Snapshot()[18]);
    }

    [Fact]
    public void Persistence_RoundTripsThroughDisk()
    {
        var device = new byte[ShadowConfig.CONFIG_SIZE];
        device[50] = 0xAB;

        using (var shadow = Create())
            shadow.SyncFromDevice(device);

        using var reloaded = Create();
        Assert.Equal(0xAB, reloaded.Snapshot()[50]);
        Assert.False(reloaded.IsDirty);
    }

    [Fact]
    public void DiffCount_CountsChangedBytes()
    {
        using var shadow = Create();
        Assert.Equal(0, shadow.DiffCount());
        shadow.SetByte(1, 0x11);
        shadow.SetByte(2, 0x22);
        Assert.Equal(2, shadow.DiffCount());
    }
}
