using CoControl.Audio;
using CoControl.Rgb;
using Xunit;

namespace CoControl.UnitTests;

public class FftTests
{
    [Fact]
    public void MagnitudeSpectrum_PureSine_PeaksAtCorrectBin()
    {
        const int n = 2048;
        const int sampleRate = 48000;
        const float freq = 1000f;

        var samples = new float[n];
        for (int i = 0; i < n; i++)
            samples[i] = MathF.Sin(2 * MathF.PI * freq * i / sampleRate);

        var mags = new float[n / 2];
        Fft.MagnitudeSpectrum(samples, mags);

        int peakBin = 0;
        for (int i = 1; i < mags.Length; i++)
            if (mags[i] > mags[peakBin]) peakBin = i;

        int expectedBin = (int)(freq / ((double)sampleRate / n)); // ≈ 42
        Assert.InRange(peakBin, expectedBin - 1, expectedBin + 1);
        Assert.True(mags[peakBin] > 0.3f, $"Peak magnitude too low: {mags[peakBin]}");
    }

    [Fact]
    public void MagnitudeSpectrum_Silence_IsZero()
    {
        var mags = new float[1024];
        Fft.MagnitudeSpectrum(new float[2048], mags);
        Assert.All(mags, m => Assert.True(m < 1e-6f));
    }

    [Fact]
    public void MagnitudeSpectrum_RejectsNonPowerOfTwo()
    {
        Assert.Throws<ArgumentException>(() => Fft.MagnitudeSpectrum(new float[1000], new float[500]));
    }

    [Fact]
    public void Transform_RoundTrip_PreservesSignal()
    {
        // FFT then inverse (via conjugate trick) should return the original.
        const int n = 256;
        var re = new double[n];
        var im = new double[n];
        var original = new double[n];
        var rnd = new Random(42);
        for (int i = 0; i < n; i++) original[i] = re[i] = rnd.NextDouble() * 2 - 1;

        Fft.Transform(re, im);
        // inverse: conjugate → forward → conjugate → /n
        for (int i = 0; i < n; i++) im[i] = -im[i];
        Fft.Transform(re, im);
        for (int i = 0; i < n; i++)
            Assert.Equal(original[i], re[i] / n, 6);
    }
}

public class SpectrumAnalyzerTests
{
    [Fact]
    public void GetBands_SineTone_LightsExpectedBand()
    {
        var analyzer = new SpectrumAnalyzer(48000) { Attack = 1f, Decay = 1f, Gain = 4f };

        const float freq = 1000f;
        var samples = new float[SpectrumAnalyzer.FftSize];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = MathF.Sin(2 * MathF.PI * freq * i / 48000f);
        analyzer.AddSamples(samples);

        var bands = new float[16];
        analyzer.GetBands(bands);

        // 1 kHz between 40 Hz and 16 kHz on a log scale → band ≈ 8..9
        int peak = 0;
        for (int i = 1; i < bands.Length; i++)
            if (bands[i] > bands[peak]) peak = i;

        Assert.InRange(peak, 7, 10);
        Assert.True(bands[peak] > 0.5f, $"Band level too low: {bands[peak]}");
    }

    [Fact]
    public void GetBands_Silence_AllZero()
    {
        var analyzer = new SpectrumAnalyzer(48000);
        var bands = new float[16];
        analyzer.GetBands(bands);
        Assert.All(bands, b => Assert.True(b < 0.01f));
    }

    [Fact]
    public void GetBands_Decay_FallsGraduallyAfterSignalStops()
    {
        var analyzer = new SpectrumAnalyzer(48000) { Attack = 1f, Decay = 0.5f };

        var tone = new float[SpectrumAnalyzer.FftSize];
        for (int i = 0; i < tone.Length; i++)
            tone[i] = MathF.Sin(2 * MathF.PI * 1000f * i / 48000f);
        analyzer.AddSamples(tone);

        var bands = new float[16];
        analyzer.GetBands(bands);
        float peakDuring = bands.Max();

        analyzer.AddSamples(new float[SpectrumAnalyzer.FftSize]); // silence
        analyzer.GetBands(bands);
        float peakAfter1 = bands.Max();

        Assert.True(peakAfter1 < peakDuring, "Level should fall after silence");
        Assert.True(peakAfter1 > 0.1f * peakDuring, "Decay should be gradual, not instant");
    }
}

public class AudioSpectrumAnimationTests
{
    private sealed class FakeSpectrum : ISpectrumSource
    {
        public float[] Levels = new float[16];
        public void GetBands(Span<float> bands) => Levels.AsSpan(0, bands.Length).CopyTo(bands);
    }

    [Fact]
    public void Render_FullBand_LightsWholeColumn()
    {
        var src = new FakeSpectrum();
        src.Levels[0] = 1f; // leftmost column, full height
        var anim = new AudioSpectrumAnimation(src);
        var buf = new byte[88 * 3];

        anim.Render(0f, buf);

        // All 6 rows should have their leftmost key lit.
        for (int row = 0; row < KeyboardGrid.RowCount; row++)
        {
            int key = KeyboardGrid.KeyAt(row, 0f);
            bool lit = buf[key * 3] > 0 || buf[key * 3 + 1] > 0 || buf[key * 3 + 2] > 0;
            Assert.True(lit, $"Row {row} (key {key}) should be lit");
        }
    }

    [Fact]
    public void Render_HalfBand_LightsBottomHalfOnly()
    {
        var src = new FakeSpectrum();
        src.Levels[8] = 0.5f;
        var anim = new AudioSpectrumAnimation(src);
        var buf = new byte[88 * 3];

        anim.Render(0f, buf);

        float relCol = 8 / 15f;
        int bottomKey = KeyboardGrid.KeyAt(5, relCol);
        int topKey = KeyboardGrid.KeyAt(0, relCol);
        Assert.True(buf[bottomKey * 3] + buf[bottomKey * 3 + 1] + buf[bottomKey * 3 + 2] > 0, "Bottom row lit");
        Assert.True(buf[topKey * 3] + buf[topKey * 3 + 1] + buf[topKey * 3 + 2] == 0, "Top row dark");
    }

    [Fact]
    public void Render_Silence_AllDark()
    {
        var anim = new AudioSpectrumAnimation(new FakeSpectrum());
        var buf = new byte[88 * 3];
        buf[10] = 0xFF; // stale
        anim.Render(0f, buf);
        Assert.All(buf, v => Assert.Equal(0, v));
    }
}

public class KeyboardGridTests
{
    [Fact]
    public void Rows_CoverAll88KeysExactlyOnce()
    {
        var seen = new HashSet<int>();
        foreach (var row in KeyboardGrid.Rows)
            foreach (int key in row)
                Assert.True(seen.Add(key), $"Key {key} appears twice");
        Assert.Equal(88, seen.Count);
        Assert.Equal(87, seen.Max());
        Assert.Equal(0, seen.Min());
    }

    [Fact]
    public void KeyAt_EdgesMapToRowEnds()
    {
        Assert.Equal(0, KeyboardGrid.KeyAt(0, 0f));    // ESC
        Assert.Equal(12, KeyboardGrid.KeyAt(0, 1f));   // F12
        Assert.Equal(75, KeyboardGrid.KeyAt(5, 0f));   // bottom-left
        Assert.Equal(87, KeyboardGrid.KeyAt(5, 1f));   // bottom-right
    }
}
