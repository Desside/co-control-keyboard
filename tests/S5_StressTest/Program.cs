using System.Diagnostics;
using CoControl.HAL;
using CoControl.Rgb;
using CoControl.Service;
using CoControl.State;

// S5: Stress tests (Definition of Done, MASTER_PLAN §5.1/§7):
//   1. Consecutive config writes without CRC error or USB timeout.
//      Default 50 writes; --full runs the specified 500 (real Flash wear!).
//   2. Sustained Direct Mode run. Default 60 s @ 60 fps; --minutes N for longer.
//
// Usage: S5_StressTest [--full] [--minutes N] [--skip-flash] [--skip-direct]

bool full = args.Contains("--full");
bool skipFlash = args.Contains("--skip-flash");
bool skipDirect = args.Contains("--skip-direct");
int minutes = 0;
for (int i = 0; i < args.Length - 1; i++)
    if (args[i] == "--minutes") minutes = int.Parse(args[i + 1]);

int flashWrites = full ? 500 : 50;
TimeSpan directDuration = minutes > 0 ? TimeSpan.FromMinutes(minutes) : TimeSpan.FromSeconds(60);

Console.WriteLine("=== S5: Stress Test ===");
Console.WriteLine($"    Flash writes: {(skipFlash ? "skipped" : flashWrites.ToString())}");
Console.WriteLine($"    Direct Mode:  {(skipDirect ? "skipped" : directDuration.ToString())} @ 60 fps\n");

var path = DeviceEnumerator.FindVendorInterfacePath();
if (path == null)
{
    Console.WriteLine("[!] No Aula F75 found.");
    return;
}
using var device = new HidDevice();
device.Open(path);
using var shadow = new ShadowConfig();
using var engine = new ProtocolEngine(device, shadow);
Console.WriteLine("[+] Connected\n");

// ---------- 1. Flash write stress ----------
if (!skipFlash)
{
    Console.WriteLine($"[1/2] {flashWrites} consecutive Diff-Writes (CRC-guarded)...");
    Console.WriteLine("      NOTE: real Flash wear — the full 500-write run is a release gate, not a routine test.");

    await engine.RefreshShadowAsync();
    byte originalEffect = shadow.Snapshot()[ConfigOffsets.EffectId];

    var latencies = new List<double>(flashWrites);
    int errors = 0;
    var sw = new Stopwatch();

    for (int i = 0; i < flashWrites; i++)
    {
        // Alternate a harmless byte to force a real diff each iteration.
        byte value = (byte)(i % 2 == 0 ? 0x03 : originalEffect);
        try
        {
            sw.Restart();
            await engine.UpdateConfigAsync(cfg => cfg[ConfigOffsets.EffectId] = value);
            latencies.Add(sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            errors++;
            Console.WriteLine($"      [!] Write {i + 1} failed: {ex.Message}");
            if (errors > 5) { Console.WriteLine("      Aborting flash stress (too many errors)."); break; }
        }

        if ((i + 1) % 25 == 0)
            Console.WriteLine($"      {i + 1}/{flashWrites} done (avg {latencies.Average():F1} ms/write)");
    }

    // Restore original effect
    await engine.UpdateConfigAsync(cfg => cfg[ConfigOffsets.EffectId] = originalEffect);

    // Verify device state matches shadow
    await engine.RefreshShadowAsync();
    bool consistent = shadow.Snapshot()[ConfigOffsets.EffectId] == originalEffect;

    latencies.Sort();
    Console.WriteLine($"      Result: {latencies.Count} ok, {errors} errors");
    if (latencies.Count > 0)
    {
        Console.WriteLine($"      Latency ms: avg {latencies.Average():F1}, " +
                          $"p50 {latencies[latencies.Count / 2]:F1}, " +
                          $"p95 {latencies[(int)(latencies.Count * 0.95)]:F1}, " +
                          $"max {latencies[^1]:F1}");
    }
    Console.WriteLine($"      Read-back consistent: {(consistent ? "YES" : "NO — INVESTIGATE")}\n");
}

// ---------- 2. Direct Mode sustained run ----------
if (!skipDirect)
{
    Console.WriteLine($"[2/2] Direct Mode sustained run: {directDuration} @ 60 fps...");

    await using var anim = new AnimationEngine(engine, fps: 60);
    anim.Play(new RainbowWaveAnimation(speed: 0.5f, hueSpanPerKey: 1f / 16f));

    var runSw = Stopwatch.StartNew();
    long lastFrames = 0;
    int errorCount = 0;
    Exception? lastSeenError = null;

    while (runSw.Elapsed < directDuration)
    {
        await Task.Delay(TimeSpan.FromSeconds(10));
        long frames = anim.FramesSent;
        double fps = (frames - lastFrames) / 10.0;
        lastFrames = frames;

        if (anim.LastError != null && !ReferenceEquals(anim.LastError, lastSeenError))
        {
            errorCount++;
            lastSeenError = anim.LastError;
        }

        Console.WriteLine($"      t={runSw.Elapsed:hh\\:mm\\:ss}  frames={frames}  fps={fps:F1}" +
                          (anim.LastError != null ? $"  lastError={anim.LastError.Message}" : ""));
    }

    double avgFps = anim.FramesSent / runSw.Elapsed.TotalSeconds;
    Console.WriteLine($"      Result: {anim.FramesSent} frames in {runSw.Elapsed}, avg {avgFps:F1} fps, " +
                      $"{errorCount} distinct errors");
    Console.WriteLine(avgFps >= 55 ? "      PASS: sustained ≥55 fps" : "      MARGINAL: fps below 55 — check USB load");
}

Console.WriteLine("\nDone. Press any key to exit...");
Console.ReadKey();
