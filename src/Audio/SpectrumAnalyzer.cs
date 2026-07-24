using System;

namespace CoControl.Audio;

/// <summary>Provides normalized spectrum bands (0..1) for visualization.</summary>
public interface ISpectrumSource
{
    /// <summary>Fills <paramref name="bands"/> with current levels, 0..1 each.</summary>
    void GetBands(Span<float> bands);
}

/// <summary>
/// Turns a raw audio sample stream into log-spaced, smoothed spectrum bands.
/// Push samples from the capture callback with <see cref="AddSamples"/>;
/// pull bands from the render thread with <see cref="GetBands"/>.
/// Thread-safe for one producer + one consumer.
/// </summary>
public sealed class SpectrumAnalyzer : ISpectrumSource
{
    public const int FftSize = 2048;

    private readonly float[] _ring = new float[FftSize];
    private int _ringPos;
    private readonly object _lock = new();

    private readonly float[] _snapshot = new float[FftSize];
    private readonly float[] _magnitudes = new float[FftSize / 2];
    private float[] _smoothed = Array.Empty<float>();

    private readonly int _sampleRate;
    private readonly float _minFreq, _maxFreq;

    /// <summary>Rise speed (0..1 per update; 1 = instant).</summary>
    public float Attack { get; init; } = 0.9f;

    /// <summary>Fall speed (0..1 per update; smaller = slower falloff).</summary>
    public float Decay { get; init; } = 0.25f;

    /// <summary>Gain applied before clamping to 0..1.</summary>
    public float Gain { get; init; } = 4.0f;

    public SpectrumAnalyzer(int sampleRate = 48000, float minFreq = 40f, float maxFreq = 16000f)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (maxFreq <= minFreq || minFreq <= 0) throw new ArgumentException("Invalid frequency range");
        _sampleRate = sampleRate;
        _minFreq = minFreq;
        _maxFreq = Math.Min(maxFreq, sampleRate / 2f);
    }

    /// <summary>Adds mono samples (producer side, e.g. WASAPI callback).</summary>
    public void AddSamples(ReadOnlySpan<float> samples)
    {
        lock (_lock)
        {
            foreach (float s in samples)
            {
                _ring[_ringPos] = s;
                _ringPos = (_ringPos + 1) % FftSize;
            }
        }
    }

    /// <summary>Computes current band levels (consumer side, render thread).</summary>
    public void GetBands(Span<float> bands)
    {
        int bandCount = bands.Length;
        if (bandCount == 0) return;

        lock (_lock)
        {
            // Unroll ring buffer into chronological order
            int tail = FftSize - _ringPos;
            Array.Copy(_ring, _ringPos, _snapshot, 0, tail);
            Array.Copy(_ring, 0, _snapshot, tail, _ringPos);
        }

        Fft.MagnitudeSpectrum(_snapshot, _magnitudes);

        if (_smoothed.Length != bandCount) _smoothed = new float[bandCount];

        // Log-spaced band edges
        double logMin = Math.Log(_minFreq), logMax = Math.Log(_maxFreq);
        double binHz = (double)_sampleRate / FftSize;

        for (int b = 0; b < bandCount; b++)
        {
            double f0 = Math.Exp(logMin + (logMax - logMin) * b / bandCount);
            double f1 = Math.Exp(logMin + (logMax - logMin) * (b + 1) / bandCount);
            int bin0 = Math.Max(1, (int)(f0 / binHz));
            int bin1 = Math.Min(_magnitudes.Length - 1, Math.Max(bin0, (int)(f1 / binHz)));

            float peak = 0;
            for (int i = bin0; i <= bin1; i++)
                if (_magnitudes[i] > peak) peak = _magnitudes[i];

            float target = Math.Clamp(peak * Gain, 0f, 1f);
            float prev = _smoothed[b];
            _smoothed[b] = target > prev
                ? prev + (target - prev) * Attack
                : prev + (target - prev) * Decay;

            bands[b] = _smoothed[b];
        }
    }
}
