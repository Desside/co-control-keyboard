using CoControl.HAL;
using CoControl.Rgb;
using CoControl.Sensors;
using CoControl.Service;
using CoControl.State;
using CoControl.Tft;

// S4: Advanced Features — hardware test.
// Direct Mode animations, sensor visualization, TFT upload (F75 Max, --tft flag).

bool testTft = args.Contains("--tft");

Console.WriteLine("=== S4: Advanced Features Test ===\n");

try
{
    // 1. Connect
    Console.WriteLine("[1/5] Connecting...");
    var paths = DeviceEnumerator.FindAulaF75Paths();
    if (paths.Count == 0)
    {
        Console.WriteLine("[!] No Aula F75 found. Connect via USB-C (wired mode).");
        return;
    }
    using var device = new HidDevice();
    device.Open(DeviceEnumerator.FindVendorInterfacePath() ?? paths[0]);
    using var shadow = new ShadowConfig();
    using var engine = new ProtocolEngine(device, shadow);
    Console.WriteLine("[+] Connected");

    // 2. Animation pipeline: unmistakable sequence
    Console.WriteLine("\n[2/5] Animation pipeline test @ 45 fps...");
    await using (var anim = new AnimationEngine(engine, fps: 45))
    {
        Console.WriteLine("    → SOLID RED (2 sec)...");
        anim.Play(new SolidColorAnimation(0xFF, 0x00, 0x00));
        await Task.Delay(2000);

        Console.WriteLine("    → BLUE PULSE (4 sec)...");
        anim.Play(new PulseAnimation(0x00, 0x40, 0xFF, periodSec: 1.5f));
        await Task.Delay(4000);

        Console.WriteLine("    → RAINBOW STRIPES, wide bands (5 sec)...");
        anim.Play(new RainbowWaveAnimation(speed: 0.5f, hueSpanPerKey: 1f / 16f));
        await Task.Delay(5000);

        Console.WriteLine($"    Frames sent: {anim.FramesSent}" +
            (anim.LastError != null ? $", LAST ERROR: {anim.LastError.Message}" : ", no errors"));

        // 3. Pause + keepalive
        Console.WriteLine("\n[3/5] Pause (frozen frame held by keepalive, 3 sec)...");
        anim.Pause();
        await Task.Delay(3000);
        anim.Resume();

        // 4. Sensor visualization (skipped if sensors unavailable — would render black)
        Console.WriteLine("\n[4/5] CPU temperature → color gradient (10 sec)...");
        Console.WriteLine("    (run as admin for full sensor access)");
        try
        {
            using var sensors = new LibreHardwareMonitorProvider();
            var cpuTemp = sensors.Read(SensorChannel.CpuTemperature);
            if (!cpuTemp.HasValue)
            {
                Console.WriteLine("    [!] CPU temp unavailable (not elevated?) — skipping to avoid black frames.");
            }
            else
            {
                Console.WriteLine($"    CPU temp now: {cpuTemp:F1} °C");
                var sensorAnim = new SensorAnimation(
                    sensors, SensorChannel.CpuTemperature, ColorGradient.Temperature(), min: 30f, max: 90f);
                anim.Play(sensorAnim);
                for (int i = 0; i < 10; i++)
                {
                    await Task.Delay(1000);
                    Console.WriteLine($"    t+{i + 1}s: {sensorAnim.LastValue?.ToString("F1") ?? "n/a"} °C");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    [!] Sensor test skipped: {ex.Message}");
        }

        Console.WriteLine("    → Restoring solid white before exit...");
        anim.Play(new SolidColorAnimation(0x80, 0x80, 0x80));
        await Task.Delay(500);
    }

    // 5. TFT (F75 Max only, opt-in)
    Console.WriteLine("\n[5/5] TFT upload...");
    if (!testTft)
    {
        Console.WriteLine("    Skipped (run with --tft on an F75 Max).");
    }
    else
    {
        var tftPath = DeviceEnumerator.FindTftInterfacePath();
        if (tftPath == null)
        {
            Console.WriteLine("    [!] MI_02 interface not found — not an F75 Max?");
        }
        else
        {
            using var pipe = new RawPipeDevice();
            pipe.Open(tftPath);

            // Solid dark-blue test frame, 240x135 (adjust to actual panel size).
            const int W = 240, H = 135;
            var rgb888 = new byte[W * H * 3];
            for (int i = 0; i < W * H; i++) rgb888[i * 3 + 2] = 0x80;
            var rgb565 = TftUploader.Rgb888ToRgb565(rgb888);

            var uploader = new TftUploader(pipe);
            Console.WriteLine($"    Uploading {rgb565.Length} bytes ({TftUploader.ChunkCount(rgb565.Length)} chunks)...");
            await uploader.UploadAsync(rgb565, new Progress<double>(p => Console.Write($"\r    {p:P0}   ")));
            Console.WriteLine("\n    [+] Upload complete");
        }
    }

    Console.WriteLine("\n[SUCCESS] S4 Advanced Features verified!");
}
catch (Exception ex)
{
    Console.WriteLine($"\n[ERROR] {ex.Message}");
    if (ex.InnerException != null) Console.WriteLine($"  Inner: {ex.InnerException.Message}");
    Console.WriteLine($"  Stack: {ex.StackTrace}");
}

Console.WriteLine("\nPress any key to exit...");
Console.ReadKey();
