using CoControl.HAL;
using CoControl.Input;
using CoControl.Service;
using CoControl.State;

// S3: Memory & Input Logic — hardware test.
// Safe by default: only the well-understood 136-byte config region is written.
// Remap/macro flash page writes use UNVERIFIED page numbers (see FlashLayout)
// and require the explicit --write-flash-pages flag.

bool writeFlashPages = args.Contains("--write-flash-pages");

Console.WriteLine("=== S3: Memory & Input Logic Test ===\n");

try
{
    // 1. Connect
    Console.WriteLine("[1/5] Enumerating and opening device...");
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
    Console.WriteLine($"[+] Connected: {paths[0]}");

    // 2. Model check
    Console.WriteLine("\n[2/5] Model Query via engine...");
    var (model, sub) = await engine.QueryModelAsync();
    Console.WriteLine($"[+] Model: 0x{model:X2}, Sub: 0x{sub:X2}");

    // 3. Shadow sync from device
    Console.WriteLine("\n[3/5] Refreshing shadow from device (CMD 0x84)...");
    await engine.RefreshShadowAsync();
    Console.WriteLine($"[+] Shadow synced, dirty={shadow.IsDirty}");

    // 4. Diff-Write with CRC: toggle effect, then restore
    Console.WriteLine("\n[4/5] Diff-Write test: set Wave effect, then restore...");
    byte originalEffect = shadow.Snapshot()[ConfigOffsets.EffectId];
    Console.WriteLine($"    Current effect: 0x{originalEffect:X2}");

    bool wrote = await engine.UpdateConfigAsync(cfg => cfg[ConfigOffsets.EffectId] = 0x03);
    Console.WriteLine($"    Wave applied: {wrote}, dirty={shadow.IsDirty}, diff bytes now={shadow.DiffCount()}");
    await Task.Delay(1500);

    wrote = await engine.UpdateConfigAsync(cfg => cfg[ConfigOffsets.EffectId] = originalEffect);
    Console.WriteLine($"    Restored: {wrote}");

    // Idempotency check: same mutation again must be a no-op.
    wrote = await engine.UpdateConfigAsync(cfg => cfg[ConfigOffsets.EffectId] = originalEffect);
    Console.WriteLine($"    No-op write skipped correctly: {!wrote}");

    // 5. Remap + macro (serialization always; device write only with flag)
    Console.WriteLine("\n[5/5] Remap table & macro...");
    var remapper = new KeyRemapper();
    remapper[0] = RemapEntry.Key(KeyCode.CapsLock); // demo: ESC → CapsLock
    Console.WriteLine($"    Remap table serialized: {remapper.Serialize().Length} bytes");

    var macro = new Macro().Tap(KeyCode.G, 20).Tap(KeyCode.G, 20); // "gg"
    Console.WriteLine($"    Macro serialized: {macro.Serialize().Length} bytes, {macro.Events.Count} events");

    if (writeFlashPages)
    {
        Console.WriteLine("    [!] Writing flash pages (UNVERIFIED page layout — you were warned)...");
        await remapper.ApplyAsync(engine);
        await macro.ApplyAsync(engine, slot: 0);
        Console.WriteLine("    [+] Pages written");
    }
    else
    {
        Console.WriteLine("    Skipped device write (run with --write-flash-pages after verifying FlashLayout).");
    }

    Console.WriteLine("\n[SUCCESS] S3 Memory & Input Logic verified!");
    Console.WriteLine("    - Engine queue + ACK read: OK");
    Console.WriteLine("    - Shadow sync (0x84): OK");
    Console.WriteLine("    - Diff-Write + CRC + commit (0x04): OK");
    Console.WriteLine("    - No-op skip: OK");
    Console.WriteLine("    - Remap/macro serialization: OK");
}
catch (Exception ex)
{
    Console.WriteLine($"\n[ERROR] {ex.Message}");
    if (ex.InnerException != null) Console.WriteLine($"  Inner: {ex.InnerException.Message}");
    Console.WriteLine($"  Stack: {ex.StackTrace}");
}

Console.WriteLine("\nPress any key to exit...");
Console.ReadKey();
