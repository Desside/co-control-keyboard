using System;
using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;
using CoControl.HAL;

namespace CoControl.Tft;

/// <summary>
/// Uploads image data to the F75 Max TFT screen over the MI_02 bulk pipe.
/// Hardware constraint: 4096-byte chunks with ≈65 ms inter-chunk delay
/// to match the MCU's DMA timing. Do not lower the delay.
/// </summary>
public sealed class TftUploader
{
    public const int ChunkSize = 4096;
    public static readonly TimeSpan DefaultChunkDelay = TimeSpan.FromMilliseconds(65);

    private readonly IRawPipe _pipe;
    private readonly TimeSpan _chunkDelay;

    /// <param name="chunkDelay">Override only in tests; hardware needs ≈65 ms.</param>
    public TftUploader(IRawPipe pipe, TimeSpan? chunkDelay = null)
    {
        _pipe = pipe ?? throw new ArgumentNullException(nameof(pipe));
        _chunkDelay = chunkDelay ?? DefaultChunkDelay;
    }

    /// <summary>Number of chunks a payload will be split into.</summary>
    public static int ChunkCount(int byteCount) => (byteCount + ChunkSize - 1) / ChunkSize;

    /// <summary>
    /// Streams the payload in 4096-byte chunks with the mandatory delay.
    /// Reports progress as 0..1.
    /// </summary>
    public async Task UploadAsync(ReadOnlyMemory<byte> data, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (data.IsEmpty) throw new ArgumentException("No data to upload");

        int total = ChunkCount(data.Length);
        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();

            int offset = i * ChunkSize;
            int len = Math.Min(ChunkSize, data.Length - offset);
            _pipe.Write(data.Span.Slice(offset, len));

            progress?.Report((i + 1) / (double)total);

            if (i < total - 1)
                await Task.Delay(_chunkDelay, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Converts 24-bit RGB888 (interleaved, row-major) to RGB565 little-endian —
    /// the native TFT pixel format.
    /// </summary>
    public static byte[] Rgb888ToRgb565(ReadOnlySpan<byte> rgb888)
    {
        if (rgb888.Length % 3 != 0) throw new ArgumentException("RGB888 data length must be a multiple of 3");

        int pixels = rgb888.Length / 3;
        var result = new byte[pixels * 2];
        for (int i = 0; i < pixels; i++)
        {
            byte r = rgb888[i * 3], g = rgb888[i * 3 + 1], b = rgb888[i * 3 + 2];
            ushort v = (ushort)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(i * 2, 2), v);
        }
        return result;
    }
}
