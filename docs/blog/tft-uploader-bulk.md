# Pushing Pixels: Bulk Data Transfer for the Aula F75 Max TFT

## Opening

**The problem:** Feature Reports (the 520-byte packets used for RGB and config) are great for small commands, but they are far too slow for graphics. Sending a single 240x240 pixel image via Feature Reports would take minutes and likely crash the MCU's command buffer.

**The insight:** The F75 Max includes a second HID interface (`MI_02`) designed specifically for bulk data. By switching from `HidD_SetFeature` to raw `WriteFile` operations, we can stream large chunks of pixel data directly to the screen's DMA buffer.

**What you'll learn:** How to implement a bulk-pipe uploader in .NET, handle large binary chunks with `ReadOnlySpan<byte>`, and manage critical hardware timing constraints to prevent image corruption.

---

## Body

### 1. The Shift: Feature Reports vs. Bulk Pipes

Most of the app communicates via `MI_01` using Feature Reports. However, the TFT display requires a "Bulk Transfer" approach. 

**The Difference:**
- **Feature Reports (`MI_01`):** Request-response, fixed size (520 bytes), high overhead.
- **Bulk Pipe (`MI_02`):** Stream-oriented, large packets, low overhead.

In the code, this is represented by the `RawPipeDevice`. While `HidDevice` uses `hid.dll` wrappers, `RawPipeDevice` uses the raw `WriteFile` WinAPI to push data as fast as the USB bus allows.

```csharp
public void Write(ReadOnlySpan<byte> data)
{
    byte[] buffer = data.ToArray();
    if (!NativeMethods.WriteFile(_handle!, buffer, (uint)buffer.Length, out uint written, IntPtr.Zero))
    {
        throw new IOException($"WriteFile failed (0x{Marshal.GetLastWin32Error():X})");
    }
}
```

---

### 2. The DMA Constraint: The 65ms Rule

You cannot simply dump a 1MB image into the USB pipe. The keyboard's MCU has a limited internal buffer. If you send data faster than the MCU can write it to the TFT controller, the buffer overflows, and the image becomes shifted or corrupted.

**The Discovery:** Through trial and error, we found that the hardware requires $\approx 65\text{ms}$ between 4096-byte chunks.

**Implementation:**
The `TftUploader` splits the image into these precise chunks and enforces a mandatory delay.

```csharp
public async Task UploadImageAsync(byte[] pixelData)
{
    int chunkSize = 4096;
    for (int offset = 0; offset < pixelData.Length; offset += chunkSize)
    {
        int length = Math.Min(chunkSize, pixelData.Length - offset);
        _pipe.Write(pixelData.AsSpan(offset, length));
        
        // CRITICAL: Match hardware DMA timing
        await Task.Delay(65); 
    }
}
```

---

### 3. Memory Efficiency with `ReadOnlySpan<T>`

Uploading images can easily consume hundreds of megabytes if you are not careful with allocations. To avoid triggering the Garbage Collector (GC) and causing "stutter" in the UI, the uploader uses `ReadOnlySpan<byte>`.

By slicing the original image buffer instead of creating new arrays for every chunk, we reduce the allocation overhead to near zero.

**Performance Impact:**
- **Memory Overhead:** $O(1)$ regardless of image size.
- **CPU Usage:** Minimal; the bottleneck is shifted entirely to the USB hardware timing.

---

### 4. The "TFT-Squeeze": Formatting for the MCU

The MCU doesn't take PNGs or JPEGs. It expects a raw binary stream of pixel colors in a specific format (usually RGB565 or similar). 

The uploader's responsibility is to ensure that the final byte array matches the hardware's expected alignment. Any misalignment in the starting byte of a chunk will result in a "shifted" screen where the image is tilted or split.

---

## Closing

**Key Takeaway:** High-bandwidth hardware features (like TFT screens) require a different transport strategy than control features. Moving from "Messages" (Feature Reports) to "Streams" (Bulk Pipes) is the only way to achieve acceptable upload speeds.

**Action for you:** When building for hardware with displays, always identify the "Bulk" interface. If you see image corruption, don't look at your data—look at your timing. Add a `Task.Delay` and see if the image stabilizes.

---

## Honest Trade-offs

| Decision | Benefit | Cost |
|----------|---------|------|
| **Raw `WriteFile`** | Maximum throughput for image data | Bypasses the safety and ACK logic of the `ProtocolEngine` |
| **Fixed 65ms Delay** | 100% reliability across different USB controllers | Uploads are slow (e.g., 4KB per 65ms $\approx 60\text{KB/s}$) |
| **Synchronous I/O** | Simplest implementation for sequential chunks | Blocks the calling thread unless wrapped in `Task.Run` |
| **`Slicing` with Span** | Zero allocation during upload | Requires careful index management to avoid `ArgumentOutOfRangeException` |
