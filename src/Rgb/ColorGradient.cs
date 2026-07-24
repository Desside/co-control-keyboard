using System;
using System.Collections.Generic;
using System.Linq;

namespace CoControl.Rgb;

/// <summary>
/// Multi-stop color gradient: maps t ∈ [0,1] to an interpolated RGB color.
/// Used to visualize sensor values (e.g. green → yellow → red for temperature).
/// </summary>
public sealed class ColorGradient
{
    private readonly record struct Stop(float Position, byte R, byte G, byte B);
    private readonly List<Stop> _stops = new();

    public ColorGradient AddStop(float position, byte r, byte g, byte b)
    {
        if (position is < 0f or > 1f) throw new ArgumentOutOfRangeException(nameof(position), "Position must be 0..1");
        _stops.Add(new Stop(position, r, g, b));
        _stops.Sort((a, b2) => a.Position.CompareTo(b2.Position));
        return this;
    }

    /// <summary>Samples the gradient; t is clamped to [0,1].</summary>
    public (byte R, byte G, byte B) Sample(float t)
    {
        if (_stops.Count == 0) throw new InvalidOperationException("Gradient has no stops");
        t = Math.Clamp(t, 0f, 1f);

        var first = _stops[0];
        if (t <= first.Position) return (first.R, first.G, first.B);
        var last = _stops[^1];
        if (t >= last.Position) return (last.R, last.G, last.B);

        for (int i = 1; i < _stops.Count; i++)
        {
            if (t > _stops[i].Position) continue;
            var lo = _stops[i - 1];
            var hi = _stops[i];
            float span = hi.Position - lo.Position;
            float f = span <= float.Epsilon ? 0f : (t - lo.Position) / span;
            return (
                (byte)(lo.R + (hi.R - lo.R) * f),
                (byte)(lo.G + (hi.G - lo.G) * f),
                (byte)(lo.B + (hi.B - lo.B) * f));
        }
        return (last.R, last.G, last.B); // unreachable
    }

    /// <summary>Green → yellow → red, the classic temperature gradient.</summary>
    public static ColorGradient Temperature() => new ColorGradient()
        .AddStop(0.0f, 0x00, 0xFF, 0x00)
        .AddStop(0.5f, 0xFF, 0xFF, 0x00)
        .AddStop(1.0f, 0xFF, 0x00, 0x00);
}
