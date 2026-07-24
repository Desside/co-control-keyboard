using System;
using NAudio.Wave;

namespace CoControl.Audio;

/// <summary>
/// Captures the system audio mix via WASAPI loopback (NAudio) and feeds a
/// <see cref="SpectrumAnalyzer"/>. Whatever plays through the default output
/// device becomes the visualizer input.
/// </summary>
public sealed class WasapiLoopbackSource : IDisposable
{
    private readonly WasapiLoopbackCapture _capture;
    private bool _disposed;

    public SpectrumAnalyzer Analyzer { get; }

    public WasapiLoopbackSource()
    {
        _capture = new WasapiLoopbackCapture();
        var fmt = _capture.WaveFormat; // typically IEEE float, 48000 Hz, 2 ch
        Analyzer = new SpectrumAnalyzer(fmt.SampleRate);

        int channels = fmt.Channels;
        _capture.DataAvailable += (_, e) =>
        {
            // Mix to mono floats. Loopback delivers 32-bit IEEE float on Vista+.
            int frames = e.BytesRecorded / 4 / channels;
            if (frames <= 0) return;

            Span<float> mono = frames <= 4096 ? stackalloc float[frames] : new float[frames];
            var src = e.Buffer.AsSpan(0, e.BytesRecorded);
            for (int f = 0; f < frames; f++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++)
                    sum += BitConverter.ToSingle(src.Slice((f * channels + c) * 4, 4));
                mono[f] = sum / channels;
            }
            Analyzer.AddSamples(mono);
        };
    }

    public void Start() => _capture.StartRecording();
    public void Stop() => _capture.StopRecording();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _capture.StopRecording(); } catch { /* not recording */ }
        _capture.Dispose();
    }
}
