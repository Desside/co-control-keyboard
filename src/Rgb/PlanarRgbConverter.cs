using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace CoControl.Rgb;

/// <summary>
/// Converts between standard per-key RGB[88][3] and device Planar R[126]G[126]B[126].
/// Handles the 88-key → 126-LED index mapping (per KB.ini LED map).
/// </summary>
public static class PlanarRgbConverter
{
    /// <summary>
    /// Maps logical key index (0-87, per KB.ini K1-K88) → LED index (0-125).
    /// K88 is ISO extra key between LSHIFT and Z.
    /// </summary>
    private static readonly byte[] KeyToLed = new byte[88]
    {
        // F-row
        0,  12, 18, 24, 30, 36, 42, 48, 54, 60, 66, 72, 78,  // ESC, F1-F12
        // Number row
        1,  7,  13, 19, 25, 31, 37, 43, 49, 55, 61, 67, 73, 79,  // `, 1-9, 0, -, =
        84, 90, 96,  // PrtSc, ScrLk, Pause
        // Q-row
        2,  8,  14, 20, 26, 32, 38, 44, 50, 56, 62, 68, 74,  // Tab, Q-P, [, ], Enter
        86, 85, 91, 97,  // Del, Ins, Home, PgUp
        // A-row
        3,  9,  15, 21, 27, 33, 39, 45, 51, 57, 63, 69, 75,  // Caps, A-L, ;, ', \
        92, 98,  // End, PgDn
        // Z-row
        4,  10, 16, 22, 28, 34, 40, 46, 52, 58, 64, 76, 82,  // LShift, Z-M, ,, ., /, RShift, Up
        // Bottom row
        94, 83, 5,  11, 17, 35, 53, 59, 65,  // LCtrl, LWin, LAlt, Space, RAlt, Fn, App
        89, 95, 101,  // Left, Down, Right
        76   // ISO key (between LShift and Z)
    };

    /// <summary>
    /// Converts per-key RGB[88][3] (interleaved) to Planar R[126] G[126] B[126].
    /// </summary>
    public static void ToPlanar(ReadOnlySpan<byte> rgb88x3, Span<byte> r126, Span<byte> g126, Span<byte> b126)
    {
        if (rgb88x3.Length != 88 * 3) throw new ArgumentException("rgb88x3 must be 264 bytes");
        if (r126.Length != 126 || g126.Length != 126 || b126.Length != 126)
            throw new ArgumentException("Planar buffers must be 126 bytes each");

        r126.Clear(); g126.Clear(); b126.Clear();

        for (int key = 0; key < 88; key++)
        {
            int led = KeyToLed[key];
            if (led >= 126) continue;

            int src = key * 3;
            r126[led] = rgb88x3[src];
            g126[led] = rgb88x3[src + 1];
            b126[led] = rgb88x3[src + 2];
        }
    }

    /// <summary>
    /// Converts Planar R[126] G[126] B[126] back to per-key RGB[88][3] (interleaved).
    /// </summary>
    public static void FromPlanar(ReadOnlySpan<byte> r126, ReadOnlySpan<byte> g126, ReadOnlySpan<byte> b126, Span<byte> rgb88x3)
    {
        if (r126.Length != 126 || g126.Length != 126 || b126.Length != 126)
            throw new ArgumentException("Planar buffers must be 126 bytes");
        if (rgb88x3.Length != 88 * 3) throw new ArgumentException("rgb88x3 must be 264 bytes");

        for (int key = 0; key < 88; key++)
        {
            int led = KeyToLed[key];
            if (led >= 126) continue;

            int dst = key * 3;
            rgb88x3[dst] = r126[led];
            rgb88x3[dst + 1] = g126[led];
            rgb88x3[dst + 2] = b126[led];
        }
    }

    /// <summary>
    /// Converts per-key RGB[88][3] to Direct Mode interleaved RGB[122][3] (366 bytes).
    /// Direct Mode uses 122 LEDs (some keys share LEDs).
    /// </summary>
    public static void ToDirectMode(ReadOnlySpan<byte> rgb88x3, Span<byte> rgb366)
    {
        if (rgb88x3.Length != 88 * 3) throw new ArgumentException("rgb88x3 must be 264 bytes");
        if (rgb366.Length != 122 * 3) throw new ArgumentException("rgb366 must be 366 bytes");

        rgb366.Clear();

        // Map keys to direct mode LED indices (simplified: use first 122 keys)
        for (int i = 0; i < 122 && i < 88; i++)
        {
            int led = KeyToLed[i];
            if (led < 122)
            {
                int src = i * 3;
                int dst = led * 3;
                rgb366[dst] = rgb88x3[src];
                rgb366[dst + 1] = rgb88x3[src + 1];
                rgb366[dst + 2] = rgb88x3[src + 2];
            }
        }
    }

    /// <summary>
    /// Creates a single-color planar buffer for all keys.
    /// </summary>
    public static void FillSolid(byte r, byte g, byte b, Span<byte> r126, Span<byte> g126, Span<byte> b126)
    {
        r126.Fill(r);
        g126.Fill(g);
        b126.Fill(b);
    }

    /// <summary>
    /// Creates a gradient planar buffer (e.g., for sensor visualization).
    /// </summary>
    public static void FillGradient(Func<int, (byte r, byte g, byte b)> colorFunc, Span<byte> r126, Span<byte> g126, Span<byte> b126)
    {
        for (int i = 0; i < 126; i++)
        {
            var (r, g, b) = colorFunc(i);
            r126[i] = r;
            g126[i] = g;
            b126[i] = b;
        }
    }
}

/// <summary>
/// Frame buffer for animation: holds current and next frame for diffing.
/// </summary>
public sealed class FrameBuffer
{
    public byte[] CurrentRgb = new byte[88 * 3];
    public byte[] NextRgb = new byte[88 * 3];
    public byte[] PlanarR = new byte[126];
    public byte[] PlanarG = new byte[126];
    public byte[] PlanarB = new byte[126];
    public byte[] DirectMode = new byte[122 * 3];

    public void Swap()
    {
        (CurrentRgb, NextRgb) = (NextRgb, CurrentRgb);
    }

    public void ClearNext()
    {
        Array.Clear(NextRgb, 0, NextRgb.Length);
    }
}