using CoControl.HAL;

// Direct RGB probe — byte-for-byte replica of the packet confirmed working on
// Aula F87 Pro / F75 (VID 258A, PID 010C) by github.com/Ahorts/aula-f87pro:
//   [0x06, 0x08, 0x00, 0x00, 0x01, 0x00, 0x7A, 0x01] + interleaved RGB (102 LEDs) + zero pad to 520
// Tries every candidate interface and asks you to confirm what you see —
// this isolates transport/interface problems from packet-format problems.

const int NUM_LEDS = 102;
const int PACKET_SIZE = 520;

static byte[] BuildPacket(byte r, byte g, byte b, int ledCount = NUM_LEDS)
{
    var pkt = new byte[PACKET_SIZE];
    pkt[0] = 0x06; pkt[1] = 0x08;
    pkt[2] = 0x00; pkt[3] = 0x00;
    pkt[4] = 0x01; pkt[5] = 0x00;
    pkt[6] = 0x7A; pkt[7] = 0x01;
    for (int i = 0; i < ledCount; i++)
    {
        pkt[8 + i * 3] = r;
        pkt[8 + i * 3 + 1] = g;
        pkt[8 + i * 3 + 2] = b;
    }
    return pkt;
}

Console.WriteLine("=== Direct RGB Probe (F87-verified packet) ===\n");

var paths = DeviceEnumerator.FindAulaF75Paths();
if (paths.Count == 0)
{
    Console.WriteLine("[!] No Aula device (VID 258A) found.");
    return;
}

// Candidates: prefer the 520-byte feature interface, but offer all of them.
var candidates = paths
    .Select(p => (Path: p, FeatureLen: DeviceEnumerator.GetFeatureReportLength(p)))
    .OrderByDescending(c => c.FeatureLen)
    .ToList();

Console.WriteLine("Candidate interfaces:");
for (int i = 0; i < candidates.Count; i++)
    Console.WriteLine($"  [{i}] FeatureLen={candidates[i].FeatureLen,4}  {candidates[i].Path}");
Console.WriteLine();

foreach (var (path, featureLen) in candidates)
{
    if (featureLen < PACKET_SIZE)
    {
        Console.WriteLine($"--- Skipping (FeatureLen={featureLen} < 520): ...{path[^40..]}");
        continue;
    }

    Console.WriteLine($"--- Testing: ...{path[^40..]}");
    using var device = new HidDevice();
    try
    {
        device.Open(path);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"    [!] Open failed: {ex.Message}");
        continue;
    }

    try
    {
        // Step 1: solid red
        Console.WriteLine("    Sending SOLID RED...");
        device.SetFeature(BuildPacket(255, 0, 0));
        Console.Write("    >>> Is the keyboard RED now? (y/n): ");
        bool red = Console.ReadLine()?.Trim().ToLowerInvariant() is "y" or "yes" or "д" or "да";

        if (!red)
        {
            Console.WriteLine("    No effect on this interface.\n");
            continue;
        }

        // Step 2: green / blue / white sweep
        foreach (var (name, r, g, b) in new[] { ("GREEN", (byte)0, (byte)255, (byte)0), ("BLUE", (byte)0, (byte)0, (byte)255), ("WHITE", (byte)255, (byte)255, (byte)255) })
        {
            Console.WriteLine($"    Sending {name}...");
            device.SetFeature(BuildPacket(r, g, b));
            await Task.Delay(1000);
        }

        // Step 3: first 10 LEDs red only — verifies LED indexing start
        Console.WriteLine("    Sending RED on first 10 LEDs only (left edge columns)...");
        var pkt10 = BuildPacket(0, 0, 0);
        for (int i = 0; i < 10; i++) pkt10[8 + i * 3] = 255;
        device.SetFeature(pkt10);
        Console.Write("    >>> Which keys are red? (describe, Enter to continue): ");
        Console.ReadLine();

        // Step 4: off
        Console.WriteLine("    Sending OFF...");
        device.SetFeature(BuildPacket(0, 0, 0));

        Console.WriteLine($"\n[SUCCESS] Working RGB interface:\n  {path}");
        Console.WriteLine("Direct Mode (0x08) confirmed working with 102-LED interleaved layout.");
        break;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"    [!] SetFeature failed: {ex.Message}\n");
    }
}

Console.WriteLine("\nDone. Press any key to exit...");
Console.ReadKey();
