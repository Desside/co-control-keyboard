using CoControl.HAL;

Console.WriteLine("=== Co-Control App: S1 Connectivity Test ===");
Console.WriteLine();

try
{
    // 1. Enumerate Aula F75 devices
    Console.WriteLine("[1/4] Enumerating HID devices (VID:0x258A)...");
    var paths = DeviceEnumerator.FindAulaF75Paths();

    if (paths.Count == 0)
    {
        Console.WriteLine("    [!] No Aula F75 found. Ensure keyboard is connected via USB-C (wired mode).");
        return;
    }

    Console.WriteLine($"    [+] Found {paths.Count} device interface(s):");
    foreach (var p in paths)
        Console.WriteLine($"        {p}");

    // 2. Open the vendor interface (520-byte Feature Reports)
    Console.WriteLine("\n[2/4] Opening device handle (CreateFile)...");
    using var device = new HidDevice();
    device.Open(DeviceEnumerator.FindVendorInterfacePath() ?? paths[0]);
    Console.WriteLine("    [+] Handle opened successfully");

    // 3. Get attributes (VID/PID validation)
    Console.WriteLine("\n[3/4] Reading device attributes (HidD_GetAttributes)...");
    var (vid, pid, ver) = device.GetAttributes();
    Console.WriteLine($"    [+] VID: 0x{vid:X4}, PID: 0x{pid:X4}, Version: {ver}");

    if (vid != 0x258A || (pid != 0x010C && pid != 0x010D))
    {
        Console.WriteLine("    [!] VID/PID mismatch!");
        return;
    }

    // 4. Model Query (CMD 0x82) - safe read-only command
    Console.WriteLine("\n[4/4] Sending Model Query (CMD 0x82)...");
    var query = new byte[520];
    query[0] = 0x06; // Report ID
    query[1] = 0x82; // Model Query
    query[2] = 0x01;
    query[3] = 0x00;
    query[4] = 0x01;
    query[5] = 0x00;
    query[6] = 0x06;

    device.SetFeature(query);

    // Read response
    var response = device.GetFeature(0x06);
    Console.WriteLine($"    [+] Response received ({response.Length} bytes)");
    Console.WriteLine($"        Bytes 0-13: {BitConverter.ToString(response, 0, 14)}");

    // Validate response structure (per protocol)
    if (response[0] == 0x06 && response[1] == 0x82)
    {
        Console.WriteLine("\n[SUCCESS] S1 Connectivity Foundation verified!");
        Console.WriteLine("    - Device discovery: OK");
        Console.WriteLine("    - Handle management: OK");
        Console.WriteLine("    - Feature Report I/O: OK");
        Console.WriteLine("    - Model Query (0x82): OK");
    }
    else
    {
        Console.WriteLine("\n[WARNING] Unexpected response format");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"\n[ERROR] {ex.Message}");
    if (ex is System.ComponentModel.Win32Exception w32)
        Console.WriteLine($"    Win32 Error: 0x{w32.NativeErrorCode:X8} ({w32.NativeErrorCode})");
}

Console.WriteLine("\nPress any key to exit...");
Console.ReadKey();