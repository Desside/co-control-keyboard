using System;

namespace CoControl.Rgb;

/// <summary>Static single color on all keys.</summary>
public sealed class SolidColorAnimation : IAnimation
{
    private readonly byte _r, _g, _b;

    public SolidColorAnimation(byte r, byte g, byte b) => (_r, _g, _b) = (r, g, b);

    public void Render(float timeSec, Span<byte> rgb88x3)
    {
        for (int i = 0; i < 88; i++)
        {
            rgb88x3[i * 3] = _r;
            rgb88x3[i * 3 + 1] = _g;
            rgb88x3[i * 3 + 2] = _b;
        }
    }
}

/// <summary>Sine pulse of one color across all keys.</summary>
public sealed class PulseAnimation : IAnimation
{
    private readonly byte _r, _g, _b;
    private readonly float _periodSec;

    public PulseAnimation(byte r, byte g, byte b, float periodSec = 2f)
    {
        if (periodSec <= 0) throw new ArgumentOutOfRangeException(nameof(periodSec));
        (_r, _g, _b, _periodSec) = (r, g, b, periodSec);
    }

    public void Render(float timeSec, Span<byte> rgb88x3)
    {
        float phase = (float)(Math.Sin(timeSec * 2 * Math.PI / _periodSec) * 0.5 + 0.5);
        byte r = (byte)(_r * phase), g = (byte)(_g * phase), b = (byte)(_b * phase);
        for (int i = 0; i < 88; i++)
        {
            rgb88x3[i * 3] = r;
            rgb88x3[i * 3 + 1] = g;
            rgb88x3[i * 3 + 2] = b;
        }
    }
}

/// <summary>Rainbow hue wave travelling across key indices.</summary>
public sealed class RainbowWaveAnimation : IAnimation
{
    private readonly float _speedHzPerSec;
    private readonly float _hueSpanPerKey;

    /// <param name="speed">Hue rotations per second (default 0.2).</param>
    /// <param name="hueSpanPerKey">Hue offset between adjacent keys, in rotations (default 1/88).</param>
    public RainbowWaveAnimation(float speed = 0.2f, float hueSpanPerKey = 1f / 88f)
    {
        _speedHzPerSec = speed;
        _hueSpanPerKey = hueSpanPerKey;
    }

    public void Render(float timeSec, Span<byte> rgb88x3)
    {
        for (int i = 0; i < 88; i++)
        {
            float hue = (timeSec * _speedHzPerSec + i * _hueSpanPerKey) % 1f;
            if (hue < 0) hue += 1f;
            var (r, g, b) = HsvToRgb(hue, 1f, 1f);
            rgb88x3[i * 3] = r;
            rgb88x3[i * 3 + 1] = g;
            rgb88x3[i * 3 + 2] = b;
        }
    }

    /// <summary>HSV → RGB; h, s, v in [0,1].</summary>
    public static (byte R, byte G, byte B) HsvToRgb(float h, float s, float v)
    {
        float c = v * s;
        float hp = h * 6f;
        float x = c * (1 - Math.Abs(hp % 2 - 1));
        (float r, float g, float b) = ((int)hp % 6) switch
        {
            0 => (c, x, 0f),
            1 => (x, c, 0f),
            2 => (0f, c, x),
            3 => (0f, x, c),
            4 => (x, 0f, c),
            _ => (c, 0f, x),
        };
        float m = v - c;
        return ((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
