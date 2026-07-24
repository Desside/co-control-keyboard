using System;
using System.Diagnostics;
using CoControl.Rgb;

namespace CoControl.Sensors;

/// <summary>
/// Animation that colors keys by a live sensor value mapped through a gradient.
/// The sensor is polled at most once per <see cref="PollInterval"/> (rendering
/// runs at 30–60 Hz; hardware polling must not).
/// If <see cref="TargetKeys"/> is null, all 88 keys show the color; otherwise
/// only the listed key indices light up and the rest stay dark.
/// </summary>
public sealed class SensorAnimation : IAnimation
{
    private readonly ISensorProvider _provider;
    private readonly SensorChannel _channel;
    private readonly ColorGradient _gradient;
    private readonly float _min, _max;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastPoll;
    private bool _polledOnce;
    private float? _lastValue;

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Key indices (0..87) to light; null = all keys.</summary>
    public int[]? TargetKeys { get; init; }

    /// <param name="min">Sensor value mapped to gradient position 0.</param>
    /// <param name="max">Sensor value mapped to gradient position 1.</param>
    public SensorAnimation(ISensorProvider provider, SensorChannel channel, ColorGradient gradient, float min, float max)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _gradient = gradient ?? throw new ArgumentNullException(nameof(gradient));
        if (max <= min) throw new ArgumentException("max must be greater than min");
        _channel = channel;
        _min = min;
        _max = max;
    }

    /// <summary>Last polled sensor value (for dashboards/logging).</summary>
    public float? LastValue => _lastValue;

    public void Render(float timeSec, Span<byte> rgb88x3)
    {
        var now = _clock.Elapsed;
        if (!_polledOnce || now - _lastPoll >= PollInterval)
        {
            _lastValue = _provider.Read(_channel);
            _lastPoll = now;
            _polledOnce = true;
        }

        rgb88x3.Clear();
        if (_lastValue is not { } value) return; // sensor unavailable → dark

        float t = (value - _min) / (_max - _min);
        var (r, g, b) = _gradient.Sample(t);

        if (TargetKeys == null)
        {
            for (int i = 0; i < 88; i++)
            {
                rgb88x3[i * 3] = r; rgb88x3[i * 3 + 1] = g; rgb88x3[i * 3 + 2] = b;
            }
        }
        else
        {
            foreach (int key in TargetKeys)
            {
                if (key is < 0 or >= 88) continue;
                rgb88x3[key * 3] = r; rgb88x3[key * 3 + 1] = g; rgb88x3[key * 3 + 2] = b;
            }
        }
    }
}
