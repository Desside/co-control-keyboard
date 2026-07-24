using System;
using System.Threading;
using CoControl.HAL;
using CoControl.Protocol;
using CoControl.State;
using CoControl.Rgb;

Console.WriteLine("=== S2: RGB Base Control Test ===");
Console.WriteLine();

try
{
    // 1. Enumerate device
    Console.WriteLine("[1/8] Enumerating HID devices...");
    Console.Out.Flush();
    var paths = DeviceEnumerator.FindAulaF75Paths();
    Console.WriteLine($"    Found {paths.Count} path(s)");
    Console.Out.Flush();
    if (paths.Count == 0)
    {
        Console.WriteLine("[!] No Aula F75 found. Connect via USB-C (wired mode).");
        return;
    }
    Console.WriteLine($"[+] Found device: {paths[0]}");

    // 2. Open & setup
    Console.WriteLine("[2/8] Opening device handle...");
    Console.Out.Flush();
    using var device = new HidDevice();
    device.Open(DeviceEnumerator.FindVendorInterfacePath() ?? paths[0]);
    Console.WriteLine("[+] Handle opened");
    Console.Out.Flush();

    // 3. Verify VID/PID
    Console.WriteLine("[3/8] Reading device attributes...");
    Console.Out.Flush();
    var (vid, pid, ver) = device.GetAttributes();
    Console.WriteLine($"[+] VID: 0x{vid:X4}, PID: 0x{pid:X4}, Ver: {ver}");
    Console.Out.Flush();

    // 4. Model Query
    Console.WriteLine("\n[Test 1/7] Model Query (CMD 0x82)...");
    Console.Out.Flush();
    var modelPkt = PacketBuilder.BuildModelQuery();
    device.SetFeature(modelPkt);
    var modelResp = device.GetFeature();
    var (model, sub) = PacketParser.ParseModelQuery(modelResp);
    Console.WriteLine($"    Model: 0x{model:X2}, Sub: 0x{sub:X2}");
    Console.Out.Flush();

    // 5. Config Read
    Console.WriteLine("\n[Test 2/7] Config Read (CMD 0x84)...");
    Console.Out.Flush();
    var readPkt = PacketBuilder.BuildConfigRead();
    device.SetFeature(readPkt);
    var readResp = device.GetFeature();
    var config = PacketParser.ParseConfigRead(readResp);
    Console.WriteLine($"    Config loaded ({config.Length} bytes)");
    Console.WriteLine($"    Effect ID: 0x{config[ConfigOffsets.EffectId]:X2}, Brightness: {config[ConfigOffsets.EffectParamOffset(config[ConfigOffsets.EffectId])]}, Speed: {(config[ConfigOffsets.EffectParamOffset(config[ConfigOffsets.EffectId]) + 1] >> 4) & 0xF}");
    Console.Out.Flush();

    // 6. Initialize Shadow Config
    Console.WriteLine("\n[Test 3/7] Shadow Config initialization...");
    Console.Out.Flush();
    var shadow = new ShadowConfig();
    shadow.SyncFromDevice(config);
    Console.WriteLine("    Shadow synced");
    Console.Out.Flush();

    // 7. Test Per-Key RGB (Planar) - Set ESC (LED 0) to Red
    Console.WriteLine("\n[Test 4/7] Per-Key RGB (Planar) - ESC = Red...");
    Console.Out.Flush();
    var red = new byte[126];
    var green = new byte[126];
    var blue = new byte[126];
    red[0] = 0xFF; // LED 0 = ESC
    var pkt = PacketBuilder.BuildPerKeyRgb(red, green, blue);
    device.SetFeature(pkt);
    Console.WriteLine("    Sent Planar packet (R[126] G[126] B[126])");
    Console.Out.Flush();

    Thread.Sleep(500);

    // 8. Test: All keys Green
    Console.WriteLine("\n[Test 5/7] All keys Green...");
    Console.Out.Flush();
    Array.Fill(green, (byte)0x80);
    pkt = PacketBuilder.BuildPerKeyRgb(red, green, blue);
    device.SetFeature(pkt);
    Thread.Sleep(500);

    // 9. Test: Wave effect via Config Write using Shadow
    Console.WriteLine("\n[Test 6/7] Config Write - Set Wave Effect (ID 0x03)...");
    Console.Out.Flush();
    shadow.SetByte(ConfigOffsets.CustomModeFlag, 0x00); // Custom mode flag = 0 (hardware effect)
    shadow.SetByte(ConfigOffsets.EffectId, 0x03);       // Effect ID = 3 (Wave)
    var paramOffset = ConfigOffsets.EffectParamOffset(0x03);
    shadow.SetByte(paramOffset, 0x04);                  // Brightness = max (4)
    shadow.SetByte(paramOffset + 1, (byte)((0x03 << 4) | 0x07)); // Speed = 3, flags = 7
    
    if (shadow.TryBeginWrite(out var diffConfig, out var diffCrc))
    {
        var writePkt = PacketBuilder.BuildConfigWrite(diffConfig, diffCrc);
        device.SetFeature(writePkt);
        shadow.CommitWrite();
        Console.WriteLine($"    Effect applied via Diff-Write (CRC 0x{diffCrc:X4})");
    }
    else
    {
        Console.WriteLine("    No changes to write");
    }
    Console.Out.Flush();

    Thread.Sleep(1000);

    // 10. Test: Direct Mode animation (simple pulse)
    Console.WriteLine("\n[Test 7/7] Direct Mode pulse animation (2 sec)...");
    Console.Out.Flush();
    var directBuf = new byte[122 * 3];
    for (int frame = 0; frame < 40; frame++)
    {
        float intensity = (float)Math.Sin(frame * 0.3) * 0.5f + 0.5f;
        byte val = (byte)(intensity * 200);
        Array.Fill(directBuf, val);
        var directPkt = PacketBuilder.BuildDirectMode(directBuf);
        device.SetFeature(directPkt);
        Thread.Sleep(50);
    }

    // 11. Restore default
    Console.WriteLine("\n[Cleanup] Restoring default effect...");
    Console.Out.Flush();
    shadow.SetByte(ConfigOffsets.EffectId, 0x0A); // Default: Marquee (ID 10)
    var defaultParamOffset = ConfigOffsets.EffectParamOffset(0x0A);
    shadow.SetByte(defaultParamOffset, 0x03);
    shadow.SetByte(defaultParamOffset + 1, 0x37);
    
    if (shadow.TryBeginWrite(out var defaultConfig, out var defaultCrc))
    {
        var writePkt = PacketBuilder.BuildConfigWrite(defaultConfig, defaultCrc);
        device.SetFeature(writePkt);
        shadow.CommitWrite();
    }

    Console.WriteLine("\n[SUCCESS] S2 RGB Base Control verified!");
    Console.WriteLine("    - Model Query: OK");
    Console.WriteLine("    - Config Read/Write: OK");
    Console.WriteLine("    - Shadow + Diff-Write: OK");
    Console.WriteLine("    - Planar Per-Key RGB: OK");
    Console.WriteLine("    - Direct Mode: OK");
    Console.Out.Flush();
}
catch (Exception ex)
{
    Console.WriteLine($"\n[ERROR] {ex.Message}");
    if (ex.InnerException != null)
        Console.WriteLine($"  Inner: {ex.InnerException.Message}");
    Console.WriteLine($"  Stack: {ex.StackTrace}");
}

Console.WriteLine("\nPress any key to exit...");
Console.ReadKey();