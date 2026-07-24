using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CoControl.HAL;

public static class DeviceEnumerator
{
    public static List<string> FindAulaF75Paths()
    {
        var paths = new List<string>();

        NativeMethods.HidD_GetHidGuid(out Guid hidGuid);

        IntPtr deviceInfoSet = NativeMethods.SetupDiGetClassDevs(
            ref hidGuid,
            null,
            IntPtr.Zero,
            NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);

        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
            return paths;

        try
        {
            var deviceInterfaceData = new NativeMethods.SP_DEVICE_INTERFACE_DATA
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.SP_DEVICE_INTERFACE_DATA>()
            };

            uint index = 0;
            while (NativeMethods.SetupDiEnumDeviceInterfaces(
                deviceInfoSet,
                IntPtr.Zero,
                ref hidGuid,
                index,
                ref deviceInterfaceData))
            {
                index++;

                // First call: get required buffer size
                uint requiredSize = 0;
                NativeMethods.SetupDiGetDeviceInterfaceDetail(
                    deviceInfoSet,
                    ref deviceInterfaceData,
                    IntPtr.Zero,
                    0,
                    out requiredSize,
                    IntPtr.Zero);

                if (requiredSize == 0) continue;

                // Allocate buffer and write cbSize (CRITICAL: must be exactly 8 on x64, 6 on x86)
                IntPtr detailPtr = Marshal.AllocHGlobal((int)requiredSize);
                int cbSize = IntPtr.Size == 8 ? 8 : 4 + Marshal.SystemDefaultCharSize;
                Marshal.WriteInt32(detailPtr, cbSize);

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
                        // DevicePath is at fixed offset 4 (right after the DWORD cbSize
                        // field). NOT at offset cbSize: on x64 cbSize is 8 due to struct
                        // padding, and reading there cuts the leading "\\" off the path.
                        string? devicePath = Marshal.PtrToStringAuto(detailPtr + 4);

                        if (devicePath != null && IsAulaF75(devicePath))
                            paths.Add(devicePath);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detailPtr);
                }
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return paths;
    }

    /// <summary>
    /// Path of the 520-byte Feature Report interface — the one the protocol
    /// actually uses. Selected by HID capabilities (FeatureReportByteLength ≥ 520),
    /// falling back to the "mi_01" path substring.
    /// </summary>
    public static string? FindVendorInterfacePath()
    {
        foreach (var path in FindAulaF75Paths())
        {
            if (GetFeatureReportLength(path) >= WinApiConstants.PACKET_SIZE)
                return path;
        }
        return Find("mi_01");
    }

    /// <summary>
    /// FeatureReportByteLength (incl. Report ID byte) of a HID interface;
    /// 0 if it cannot be determined.
    /// </summary>
    public static ushort GetFeatureReportLength(string devicePath)
    {
        SafeFileHandle? handle = null;
        IntPtr preparsed = IntPtr.Zero;
        try
        {
            handle = NativeMethods.CreateFile(
                devicePath, 0,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle.IsInvalid) return 0;

            if (!NativeMethods.HidD_GetPreparsedData(handle, out preparsed)) return 0;
            if (NativeMethods.HidP_GetCaps(preparsed, out var caps) != NativeMethods.HIDP_STATUS_SUCCESS) return 0;
            return caps.FeatureReportByteLength;
        }
        catch
        {
            return 0;
        }
        finally
        {
            if (preparsed != IntPtr.Zero) NativeMethods.HidD_FreePreparsedData(preparsed);
            handle?.Close();
        }
    }

    /// <summary>
    /// Path of the config interface (MI_01, Feature Reports), if identifiable.
    /// </summary>
    public static string? FindConfigInterfacePath()
        => Find("mi_01");

    /// <summary>
    /// Path of the TFT bulk interface (MI_02, F75 Max only), if present.
    /// </summary>
    public static string? FindTftInterfacePath()
        => Find("mi_02");

    private static string? Find(string interfaceMarker)
    {
        foreach (var path in FindAulaF75Paths())
        {
            if (path.Contains(interfaceMarker, StringComparison.OrdinalIgnoreCase))
                return path;
        }
        return null;
    }

    private static bool IsAulaF75(string devicePath)
    {
        SafeFileHandle? handle = null;
        try
        {
            // desiredAccess = 0: metadata-only open. Windows denies GENERIC_READ
            // on keyboard/mouse collections (exclusive system access), but
            // attribute queries work without any access rights.
            handle = NativeMethods.CreateFile(
                devicePath,
                0,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero,
                NativeMethods.OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (handle.IsInvalid) return false;

            if (!NativeMethods.HidD_GetAttributes(handle, out var attrs))
                return false;

            return attrs.vendorID == WinApiConstants.VID_WIRED &&
                   (attrs.productID == WinApiConstants.PID_WIRED || attrs.productID == WinApiConstants.PID_24G);
        }
        catch
        {
            return false;
        }
        finally
        {
            handle?.Close();
        }
    }
}