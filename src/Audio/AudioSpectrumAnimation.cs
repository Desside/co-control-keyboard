using System;
using CoControl.Rgb;

namespace CoControl.Audio;

/// <summary>
/// Audio visualizer: 16 spectrum columns rendered as vertical bars across the
/// keyboard. Low frequencies on the left, highs on the right; bars grow from
/// the bottom row upward, colored green → yellow → red by height.
/// </summary>
public sealed class AudioSpectrumAnimation : IAnimation
{
    public const int Bands = 16;

    private readonly ISpectrumSource _source;
    private readonly ColorGradient _gradient;
    private readonly float[] _bands = new float[Bands];

    public AudioSpectrumAnimation(ISpectrumSource source, ColorGradient? gradient = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _gradient = gradient ?? ColorGradient.Temperature();
    }

    public void Render(float timeSec, Span<byte> rgb88x3)
    {
        _source.GetBands(_bands);
        rgb88x3.Clear();

        for (int band = 0; band < Bands; band++)
        {
            float level = _bands[band];
            float relCol = band / (float)(Bands - 1);

            // Bar height in rows: bottom row (index 5) upward.
            int litRows = (int)MathF.Ceiling(level * KeyboardGrid.RowCount);

            for (int i = 0; i < litRows; i++)
            {
                int row = KeyboardGrid.RowCount - 1 - i; // 5 = bottom
                int key = KeyboardGrid.KeyAt(row, relCol);
                if ((uint)key >= 88) continue;

                // Color by height fraction of this segment
                var (r, g, b) = _gradient.Sample((i + 1) / (float)KeyboardGrid.RowCount);
                rgb88x3[key * 3] = r;
                rgb88x3[key * 3 + 1] = g;
                rgb88x3[key * 3 + 2] = b;
            }
        }
    }
}
