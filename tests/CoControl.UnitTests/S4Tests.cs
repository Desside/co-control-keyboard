using CoControl.HAL;
using CoControl.Protocol;
using CoControl.Rgb;
using CoControl.Sensors;
using CoControl.Service;
using CoControl.State;
using CoControl.Tft;
using Xunit;

namespace CoControl.UnitTests;

public class ColorGradientTests
{
    [Fact]
    public void Sample_AtStops_ReturnsStopColors()
    {
        var g = ColorGradient.Temperature();
        Assert.Equal(((byte)0x00, (byte)0xFF, (byte)0x00), g.Sample(0f));
        Assert.Equal(((byte)0xFF, (byte)0xFF, (byte)0x00), g.Sample(0.5f));
        Assert.Equal(((byte)0xFF, (byte)0x00, (byte)0x00), g.Sample(1f));
    }

    [Fact]
    public void Sample_Midway_Interpolates()
    {
        var g = new ColorGradient().AddStop(0f, 0, 0, 0).AddStop(1f, 200, 100, 50);
        Assert.Equal(((byte)100, (byte)50, (byte)25), g.Sample(0.5f));
    }

    [Fact]
    public void Sample_ClampsOutOfRange()
    {
        var g = ColorGradient.Temperature();
        Assert.Equal(g.Sample(0f), g.Sample(-5f));
        Assert.Equal(g.Sample(1f), g.Sample(2f));
    }

    [Fact]
    public void Sample_EmptyGradient_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new ColorGradient().Sample(0.5f));
    }
}

public class AnimationTests
{
    [Fact]
    public void Pulse_AtQuarterPeriod_IsFullBrightness()
    {
        var anim = new PulseAnimation(200, 100, 50, periodSec: 4f);
        var buf = new byte[88 * 3];
        anim.Render(1f, buf); // sin(π/2) = 1 → full brightness
        Assert.Equal(200, buf[0]);
        Assert.Equal(100, buf[1]);
        Assert.Equal(50, buf[2]);
    }

    [Fact]
    public void Solid_FillsAllKeys()
    {
        var anim = new SolidColorAnimation(1, 2, 3);
        var buf = new byte[88 * 3];
        anim.Render(0f, buf);
        for (int i = 0; i < 88; i++)
        {
            Assert.Equal(1, buf[i * 3]);
            Assert.Equal(2, buf[i * 3 + 1]);
            Assert.Equal(3, buf[i * 3 + 2]);
        }
    }

    [Theory]
    [InlineData(0f, 255, 0, 0)]      // hue 0 = red
    [InlineData(1f / 3f, 0, 255, 0)] // hue 1/3 = green
    [InlineData(2f / 3f, 0, 0, 255)] // hue 2/3 = blue
    public void HsvToRgb_PrimaryHues(float hue, byte r, byte g, byte b)
    {
        Assert.Equal((r, g, b), RainbowWaveAnimation.HsvToRgb(hue, 1f, 1f));
    }

    [Fact]
    public void RainbowWave_ProducesDifferentColorsAcrossKeys()
    {
        var anim = new RainbowWaveAnimation(hueSpanPerKey: 0.25f);
        var buf = new byte[88 * 3];
        anim.Render(0f, buf);
        var key0 = (buf[0], buf[1], buf[2]);
        var key1 = (buf[3], buf[4], buf[5]);
        Assert.NotEqual(key0, key1);
    }
}

public class AnimationEngineTests : IDisposable
{
    private readonly string _tempPath = Path.Combine(Path.GetTempPath(), $"cocontrol-anim-{Guid.NewGuid():N}.bin");
    private readonly FakeHidDevice _device = new();
    private readonly ShadowConfig _shadow;

    public AnimationEngineTests() => _shadow = new ShadowConfig(_tempPath);

    public void Dispose()
    {
        _shadow.Dispose();
        if (File.Exists(_tempPath)) File.Delete(_tempPath);
    }

    [Fact]
    public async Task RenderFrame_SendsDirectModePacket()
    {
        using var engine = new ProtocolEngine(_device, _shadow);
        await using var anim = new AnimationEngine(engine);

        anim.Play(new SolidColorAnimation(0xFF, 0x00, 0x00));
        // Play starts the loop; also render one deterministic frame directly.
        await anim.RenderFrameAsync(0f);

        Assert.Contains((byte)HidCommand.DirectMode, _device.SentCommands());
    }

    [Fact]
    public async Task Loop_SendsMultipleFrames()
    {
        using var engine = new ProtocolEngine(_device, _shadow);
        await using var anim = new AnimationEngine(engine, fps: 60);

        anim.Play(new PulseAnimation(0xFF, 0xFF, 0xFF));
        await Task.Delay(200);

        Assert.True(_device.Sent.Count >= 3, $"Expected ≥3 frames, got {_device.Sent.Count}");
    }

    [Fact]
    public async Task Paused_SendsKeepalives_NotFrames()
    {
        using var engine = new ProtocolEngine(_device, _shadow);
        await using var anim = new AnimationEngine(engine, fps: 60);

        anim.Play(new SolidColorAnimation(1, 2, 3));
        await Task.Delay(100);
        anim.Pause();
        await Task.Delay(100);
        int atPause = _device.Sent.Count;
        await Task.Delay(200);

        // Paused: far fewer packets than 60 fps would produce (keepalive ≈1/s).
        int during = _device.Sent.Count - atPause;
        Assert.True(during <= 2, $"Expected ≤2 keepalive packets while paused, got {during}");
    }
}

public class SensorAnimationTests
{
    private sealed class FakeSensor : ISensorProvider
    {
        public float? Value;
        public int ReadCount;
        public float? Read(SensorChannel channel) { ReadCount++; return Value; }
        public void Dispose() { }
    }

    [Fact]
    public void Render_MapsValueThroughGradient()
    {
        var sensor = new FakeSensor { Value = 90f }; // max → red
        var anim = new SensorAnimation(sensor, SensorChannel.CpuTemperature, ColorGradient.Temperature(), 30f, 90f);
        var buf = new byte[88 * 3];

        anim.Render(0f, buf);

        Assert.Equal(0xFF, buf[0]); // R
        Assert.Equal(0x00, buf[1]); // G
        Assert.Equal(90f, anim.LastValue);
    }

    [Fact]
    public void Render_TargetKeys_LightsOnlyThose()
    {
        var sensor = new FakeSensor { Value = 30f }; // min → green
        var anim = new SensorAnimation(sensor, SensorChannel.CpuTemperature, ColorGradient.Temperature(), 30f, 90f)
        {
            TargetKeys = new[] { 5 },
        };
        var buf = new byte[88 * 3];

        anim.Render(0f, buf);

        Assert.Equal(0xFF, buf[5 * 3 + 1]); // key 5 green
        Assert.Equal(0, buf[0]);            // key 0 dark
        Assert.Equal(0, buf[6 * 3 + 1]);    // key 6 dark
    }

    [Fact]
    public void Render_ThrottlesSensorPolling()
    {
        var sensor = new FakeSensor { Value = 50f };
        var anim = new SensorAnimation(sensor, SensorChannel.CpuTemperature, ColorGradient.Temperature(), 30f, 90f)
        {
            PollInterval = TimeSpan.FromHours(1),
        };
        var buf = new byte[88 * 3];

        for (int i = 0; i < 100; i++) anim.Render(i / 60f, buf);

        Assert.Equal(1, sensor.ReadCount); // 100 frames, 1 poll
    }

    [Fact]
    public void Render_SensorUnavailable_GoesDark()
    {
        var sensor = new FakeSensor { Value = null };
        var anim = new SensorAnimation(sensor, SensorChannel.GpuTemperature, ColorGradient.Temperature(), 30f, 90f);
        var buf = new byte[88 * 3];
        buf[0] = 0xFF; // stale data must be cleared

        anim.Render(0f, buf);

        Assert.All(buf, v => Assert.Equal(0, v));
    }
}

public class TftUploaderTests
{
    private sealed class FakePipe : IRawPipe
    {
        public readonly List<int> ChunkSizes = new();
        public void Write(ReadOnlySpan<byte> data) => ChunkSizes.Add(data.Length);
    }

    [Fact]
    public async Task Upload_SplitsIntoChunks()
    {
        var pipe = new FakePipe();
        var uploader = new TftUploader(pipe, chunkDelay: TimeSpan.Zero);

        await uploader.UploadAsync(new byte[10000]);

        Assert.Equal(new[] { 4096, 4096, 1808 }, pipe.ChunkSizes);
    }

    [Fact]
    public async Task Upload_ExactMultiple_NoEmptyTailChunk()
    {
        var pipe = new FakePipe();
        var uploader = new TftUploader(pipe, chunkDelay: TimeSpan.Zero);

        await uploader.UploadAsync(new byte[8192]);

        Assert.Equal(new[] { 4096, 4096 }, pipe.ChunkSizes);
    }

    private sealed class SyncProgress : IProgress<double>
    {
        public readonly List<double> Reports = new();
        public void Report(double value) => Reports.Add(value);
    }

    [Fact]
    public async Task Upload_ReportsProgress()
    {
        var pipe = new FakePipe();
        var uploader = new TftUploader(pipe, chunkDelay: TimeSpan.Zero);
        var progress = new SyncProgress(); // synchronous: Progress<T> would post async

        await uploader.UploadAsync(new byte[8192], progress);

        Assert.Equal(new[] { 0.5, 1.0 }, progress.Reports);
    }

    [Fact]
    public async Task Upload_EmptyData_Throws()
    {
        var uploader = new TftUploader(new FakePipe(), chunkDelay: TimeSpan.Zero);
        await Assert.ThrowsAsync<ArgumentException>(() => uploader.UploadAsync(ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public void ChunkCount_Rounds()
    {
        Assert.Equal(1, TftUploader.ChunkCount(1));
        Assert.Equal(1, TftUploader.ChunkCount(4096));
        Assert.Equal(2, TftUploader.ChunkCount(4097));
    }

    [Theory]
    [InlineData(0xFF, 0x00, 0x00, 0xF800)] // red
    [InlineData(0x00, 0xFF, 0x00, 0x07E0)] // green
    [InlineData(0x00, 0x00, 0xFF, 0x001F)] // blue
    [InlineData(0xFF, 0xFF, 0xFF, 0xFFFF)] // white
    [InlineData(0x00, 0x00, 0x00, 0x0000)] // black
    public void Rgb888ToRgb565_KnownColors(byte r, byte g, byte b, ushort expected)
    {
        var result = TftUploader.Rgb888ToRgb565(new[] { r, g, b });
        ushort actual = (ushort)(result[0] | (result[1] << 8)); // LE
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Rgb888ToRgb565_RejectsBadLength()
    {
        Assert.Throws<ArgumentException>(() => TftUploader.Rgb888ToRgb565(new byte[4]));
    }
}
