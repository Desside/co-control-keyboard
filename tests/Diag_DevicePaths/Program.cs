using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using CoControl.HAL;

Console.WriteLine("=== HID Device Path Diagnostics ===");
Console.WriteLine();

NativeMethods.HidD_GetHidGuid(out Guid hidGuid);
Console.WriteLine($"HID Class GUID: {hidGuid}");

IntPtr deviceInfoSet = NativeMethods.SetupDiGetClassDevs(
    ref hidGuid,
    null,
    IntPtr.Zero,
    NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);

if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
{
    int error = Marshal.GetLastWin32Error();
    Console.WriteLine($"Failed to get device info set. Error: 0x{error:X}");
    return;
}
Console.WriteLine("Device info set obtained.");
Console.Out.Flush();

int cbSizeOffset = IntPtr.Size == 8 ? 8 : 4 + Marshal.SystemDefaultCharSize;

try
{
    var deviceInterfaceData = new NativeMethods.SP_DEVICE_INTERFACE_DATA
    {
        cbSize = (uint)Marshal.SizeOf<NativeMethods.SP_DEVICE_INTERFACE_DATA>()
    };

    uint index = 0;
    int aulaPathCount = 0;
    int totalCount = 0;
    int lastError = 0;

    while (NativeMethods.SetupDiEnumDeviceInterfaces(
        deviceInfoSet,
        IntPtr.Zero,
        ref hidGuid,
        index,
        ref deviceInterfaceData))
    {
        index++;
        totalCount++;

        uint requiredSize = 0;
        NativeMethods.SetupDiGetDeviceInterfaceDetail(
            deviceInfoSet,
            ref deviceInterfaceData,
            IntPtr.Zero,
            0,
            out requiredSize,
            IntPtr.Zero);

        if (requiredSize == 0) continue;

        IntPtr detailPtr = Marshal.AllocHGlobal((int)requiredSize);
        Marshal.WriteInt32(detailPtr, cbSizeOffset);

        try
        {
            if (NativeMethods.SetupDiGetDeviceInterfaceDetail(
                deviceInfoSet,
                ref deviceInterfaceData,
                detailPtr,
                requiredSize,
                out _,
                IntPtr.Zero))
            {
                // DevicePath is at fixed offset 4 (after the DWORD cbSize field);
                // cbSizeOffset (8 on x64) would cut the leading "\\" off the path.
                string? devicePath = Marshal.PtrToStringAuto(detailPtr + 4);

                if (devicePath != null)
                {
                    SafeFileHandle? handle = null;
                    ushort vid = 0, pid = 0;
                    int openError = 0;
                    try
                    {
                        // desiredAccess = 0: attribute query needs no access rights;
                        // GENERIC_READ is denied on keyboard collections.
                        handle = NativeMethods.CreateFile(
                            devicePath,
                            0,
                            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                            IntPtr.Zero,
                            NativeMethods.OPEN_EXISTING,
                            0,
                            IntPtr.Zero);

                        if (handle.IsInvalid)
                            openError = Marshal.GetLastWin32Error();
                        else if (NativeMethods.HidD_GetAttributes(handle, out var attrs))
                        {
                            vid = attrs.vendorID;
                            pid = attrs.productID;
                        }
                    }
                    catch { }
                    finally
                    {
                        handle?.Close();
                    }

                    bool isAula = vid == 0x258A || vid == 0x0c45;
                    if (isAula) aulaPathCount++;
                    string marker = isAula ? $"[AULA #{aulaPathCount}]" : "        ";
                    string status = openError != 0 ? $"open failed 0x{openError:X}" : $"VID=0x{vid:X4} PID=0x{pid:X4}";
                    ushort featLen = DeviceEnumerator.GetFeatureReportLength(devicePath);
                    Console.WriteLine($"{marker} {status} FeatureLen={featLen}");
                    Console.WriteLine($"         {devicePath}");
                    Console.Out.Flush();
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(detailPtr);
        }

        lastError = Marshal.GetLastWin32Error();
    }

    Console.WriteLine($"=== Scanned {totalCount} HID paths, found {aulaPathCount} Aula/MaxField paths ===");
}
finally
{
    NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
}

Console.WriteLine();
Console.WriteLine("Now testing with our current DeviceEnumerator.FindAulaF75Paths()...");
var paths = DeviceEnumerator.FindAulaF75Paths();
Console.WriteLine($"FindAulaF75Paths() returned {paths.Count} path(s):");
foreach (var p in paths)
    Console.WriteLine($"  FeatureLen={DeviceEnumerator.GetFeatureReportLength(p),4}  {p}");

Console.WriteLine();
var vendorPath = DeviceEnumerator.FindVendorInterfacePath();
Console.WriteLine($"FindVendorInterfacePath(): {vendorPath ?? "(not found)"}");