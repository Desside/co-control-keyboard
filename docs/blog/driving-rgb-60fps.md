# Driving RGB at 60 FPS: Planar vs. Direct Mode in HID Keyboards

## Opening

**The problem:** Most "gaming" keyboards provide a few static patterns (Rainbow, Breath, Static). To implement a real-time audio visualizer or CPU heat-map, you need to update individual LEDs 60 times per second. However, writing to a keyboard's Flash memory is slow and wears out the chip.

**The insight:** The Aula F75 has a "Direct Mode" (HID Command `0x08`) that bypasses Flash entirely, streaming RGB data directly to the LEDs. The catch? It requires a specific interleaved data format and a constant "keepalive" heartbeat, or the firmware reverts to factory defaults within 900ms.

**What you'll learn:** How to implement a high-performance RGB pipeline in .NET 8, convert between Planar and Interleaved color layouts, and build a frame loop that maintains hardware state without saturating the USB bus.

---

## Body

### 1. The Layout Clash: Planar vs. Interleaved

The Aula F75 uses two completely different ways to describe color.

**Planar Layout (Config Mode):** Used for saving profiles to Flash.
`[Red 0..125][Green 0..125][Blue 0..125]`
If you want to change the color of Key 1, you change `byte[0]`, `byte[126]`, and `byte[252]`.

**Interleaved Layout (Direct Mode):** Used for real-time animations.
`[R0, G0, B0][R1, G1, B1]...`
This is the standard format for almost every RGB library, making it the ideal target for rendering.

**The Implementation:**
To support both, we use a `PlanarRgbConverter`. To keep it fast, we avoid object allocations in the hot path and use `Span<byte>` for memory efficiency.

```csharp
public static void ToDirectMode(ReadOnlySpan<byte> rgb88x3, Span<byte> rgb366)
{
    // 88 keys -> 122 LEDs mapping
    for (int i = 0; i < 122 && i < 88; i++)
    {
        int led = KeyToLed[i]; 
        if (led < 122)
        {
            int src = i * 3;
            int dst = led * 3;
            rgb366[dst]     = rgb88x3[src];     // R
            rgb366[dst + 1] = rgb88x3[src + 1]; // G
            rgb366[dst + 2] = rgb88x3[src + 2]; // B
        }
    }
}
```

---

### 2. The 60 FPS Frame Loop

Pushing 366 bytes to a HID device every 16.6ms (60 FPS) can easily saturate a USB bus if not handled correctly. We implement a dedicated `AnimationEngine` that decouples the **Rendering** from the **Transmission**.

**The Pipeline:**
1. **Render:** `IAnimation.Render()` fills a buffer with RGB values based on `timeSec`.
2. **Convert:** `PlanarRgbConverter` transforms the buffer to Direct Mode.
3. **Send:** `ProtocolEngine` enqueues the packet to a single-worker thread to prevent USB collisions.

**Timing Strategy:**
We use `Stopwatch` for high-precision timing and `Task.Delay` to maintain a steady heartbeat.

```csharp
// Frame loop logic
var frameStart = clock.Elapsed;
if (!_paused)
{
    await RenderFrameAsync((float)frameStart.TotalSeconds);
    FramesSent++;
    lastKeepalive = frameStart;
}
```

---

### 3. The "Keepalive" Heartbeat

A critical discovery during reverse-engineering: **Direct Mode is volatile.** 

If the host stops sending packets for $\approx 900\text{ms}$, the keyboard assumes the app has crashed and reverts to the internal firmware effect. This creates a jarring experience when a user pauses an animation.

**The Solution:** When the engine is paused, we don't stop sending data. We re-send the *last rendered frame* every 900ms.

**The Trade-off:** 
- **Benefit:** Seamless pausing. The keyboard stays frozen on the last frame.
- **Cost:** Minor USB traffic during idle. Since it's only 1 packet per second, the overhead is negligible ($< 1\text{KB/s}$).

---

### 4. Scaling to Complex Effects: The Audio Visualizer

The real test of this pipeline is the Audio Spectrum visualizer. This requires three systems working in perfect sync:
1. **WASAPI Loopback:** Captures system audio.
2. **FFT (Fast Fourier Transform):** Converts time-domain audio to frequency magnitudes.
3. **RGB Mapping:** Maps frequency bins $\to$ Key indices $\to$ Color Gradients.

**Performance Metrics:**
- **Conversion Latency:** $\approx 15\mu\text{s}$ per frame.
- **Total Pipeline Delay:** $\approx 40\text{ms}$ (Capture $\to$ FFT $\to$ USB).
- **CPU Impact:** $< 1\%$ on a modern i5/i7 due to `stackalloc` and `Span` usage in the FFT hot path.

---

## Closing

**Key Takeaway:** Driving hardware at high refresh rates isn't about the raw speed of the CPU, but about managing the transport layer. By implementing a `Shadow layout` and a keepalive heartbeat, we can achieve 60 FPS visuals without risking hardware stability.

**Action for you:** If you're building a HID tool, don't use `Schedules` or `Timers`. Use a `while` loop with `Stopwatch` and `Task.Delay` to ensure your frames are spaced evenly and your keepalives never drop.

---

## Honest Trade-offs

| Decision | Why we did it | The Downside |
|----------|----------------|---------------|
| **Interleaved RGB** | Matches standard animation logic | Requires a conversion step before sending |
| **Single Worker Queue** | Prevents USB packet collision/corruption | Adds a tiny amount of latency ($\approx 0.5\text{ms}$) |
| **Symmetric 60 FPS** | Smooth visuals for audio spectrum | Higher power consumption on battery-powered keyboards |
| **Symmetric 45 FPS (Default)**| Balanced smoothness and stability | Slight "judder" compared to 60 FPS |
