using System;

namespace CoControl.Audio;

/// <summary>
/// In-place iterative radix-2 FFT with Hann windowing.
/// Small, allocation-free hot path — good enough for 2048-point audio frames.
/// </summary>
public static class Fft
{
    /// <summary>
    /// Computes magnitude spectrum of <paramref name="samples"/> (length must be a power of 2).
    /// Output: <c>samples.Length / 2</c> magnitudes (DC .. Nyquist-1).
    /// </summary>
    public static void MagnitudeSpectrum(ReadOnlySpan<float> samples, Span<float> magnitudes)
    {
        int n = samples.Length;
        if (n == 0 || (n & (n - 1)) != 0) throw new ArgumentException("Sample count must be a power of 2");
        if (magnitudes.Length < n / 2) throw new ArgumentException($"Need {n / 2} magnitude slots");

        // Copy with Hann window
        Span<double> re = n <= 4096 ? stackalloc double[n] : new double[n];
        Span<double> im = n <= 4096 ? stackalloc double[n] : new double[n];
        for (int i = 0; i < n; i++)
        {
            double w = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (n - 1)));
            re[i] = samples[i] * w;
            im[i] = 0;
        }

        Transform(re, im);

        double scale = 2.0 / n;
        for (int i = 0; i < n / 2; i++)
            magnitudes[i] = (float)(Math.Sqrt(re[i] * re[i] + im[i] * im[i]) * scale);
    }

    /// <summary>In-place complex FFT (Cooley–Tukey, bit-reversal + butterflies).</summary>
    public static void Transform(Span<double> re, Span<double> im)
    {
        int n = re.Length;
        if (n != im.Length || (n & (n - 1)) != 0) throw new ArgumentException("Length must be a power of 2");

        // Bit-reversal permutation
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        // Butterflies
        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = -2 * Math.PI / len;
            double wRe = Math.Cos(angle), wIm = Math.Sin(angle);
            for (int i = 0; i < n; i += len)
            {
                double curRe = 1, curIm = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = i + k + len / 2;
                    double tRe = re[b] * curRe - im[b] * curIm;
                    double tIm = re[b] * curIm + im[b] * curRe;
                    re[b] = re[a] - tRe; im[b] = im[a] - tIm;
                    re[a] += tRe; im[a] += tIm;
                    (curRe, curIm) = (curRe * wRe - curIm * wIm, curRe * wIm + curIm * wRe);
                }
            }
        }
    }
}
