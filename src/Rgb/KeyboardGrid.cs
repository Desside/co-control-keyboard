using System;

namespace CoControl.Rgb;

/// <summary>
/// Logical key grid of the F75: rows of key indices (0..87, KB.ini order).
/// Key indices follow the same row-major order as PlanarRgbConverter.KeyToLed.
/// </summary>
public static class KeyboardGrid
{
    /// <summary>Rows top → bottom; values are key indices (0..87).</summary>
    public static readonly int[][] Rows =
    {
        CreateRange(0, 13),   // F-row: ESC, F1..F12
        CreateRange(13, 17),  // number row + PrtSc/ScrLk/Pause
        CreateRange(30, 17),  // Q-row + Del/Ins/Home/PgUp
        CreateRange(47, 15),  // A-row + End/PgDn
        CreateRange(62, 13),  // Z-row + Up
        CreateRange(75, 13),  // bottom row + arrows
    };

    public const int RowCount = 6;

    /// <summary>
    /// Key index at a relative horizontal position (0..1) within a row.
    /// </summary>
    public static int KeyAt(int row, float relativeCol)
    {
        if (row is < 0 or >= RowCount) throw new ArgumentOutOfRangeException(nameof(row));
        relativeCol = Math.Clamp(relativeCol, 0f, 1f);
        var keys = Rows[row];
        return keys[(int)MathF.Round(relativeCol * (keys.Length - 1))];
    }

    private static int[] CreateRange(int start, int count)
    {
        var r = new int[count];
        for (int i = 0; i < count; i++) r[i] = start + i;
        return r;
    }
}
