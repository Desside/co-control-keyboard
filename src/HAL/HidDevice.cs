using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CoControl.HAL;

/// <summary>
/// Minimal HID transport surface — lets ProtocolEngine be tested without hardware.
/// </summary>
public interface IHidDevice
{
    void SetFeature(byte[] report);
    byte[] GetFeature(byte reportId = WinApiConstants.REPORT_ID);
}

public sealed class HidDevice : IHidDevice, IDisposable
{
    private SafeFileHandle? _handle;
    private bool _disposed;

    public bool IsOpen => _handle != null && !_handle.IsInvalid && !_handle.IsClosed;

    public void Open(string devicePath)
    {
        if (IsOpen) throw new InvalidOperationException("Device already open");

        _handle = NativeMethods.CreateFile(
            devicePath,
            NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            0, // synchronous I/O: HidD_Set/GetFeature must not use an overlapped handle
            IntPtr.Zero);

        if (_handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            throw new IOException($"CreateFile failed (0x{error:X}): {devicePath}", Marshal.GetExceptionForHR(error));
        }

        // Verify VID/PID
        if (!NativeMethods.HidD_GetAttributes(_handle, out var attrs))
        {
            Close();
            throw new IOException("HidD_GetAttributes failed");
        }

        if (attrs.vendorID != WinApiConstants.VID_WIRED ||
            (attrs.productID != WinApiConstants.PID_WIRED && attrs.productID != WinApiConstants.PID_24G))
        {
            Close();
            throw new InvalidOperationException($"Device VID/PID mismatch: {attrs.vendorID:X4}:{attrs.productID:X4}");
        }
    }

    public (ushort VendorId, ushort ProductId, ushort Version) GetAttributes()
    {
        if (!IsOpen) throw new InvalidOperationException("Device not open");
        
        if (!NativeMethods.HidD_GetAttributes(_handle!, out var attrs))
            throw new IOException("HidD_GetAttributes failed");

        return (attrs.vendorID, attrs.productID, attrs.versionNumber);
    }

    public void SetFeature(byte[] report)
    {
        ValidateReport(report, "SetFeature");
        if (!NativeMethods.HidD_SetFeature(_handle!, report, (uint)report.Length))
        {
            int error = Marshal.GetLastWin32Error();
            throw new IOException($"HidD_SetFeature failed (0x{error:X})", Marshal.GetExceptionForHR(error));
        }
    }

    public byte[] GetFeature(byte reportId = WinApiConstants.REPORT_ID)
    {
        var buffer = new byte[WinApiConstants.PACKET_SIZE];
        buffer[0] = reportId;

        if (!NativeMethods.HidD_GetFeature(_handle!, buffer, (uint)buffer.Length))
        {
            int error = Marshal.GetLastWin32Error();
            throw new IOException($"HidD_GetFeature failed (0x{error:X})", Marshal.GetExceptionForHR(error));
        }

        return buffer;
    }

    private static void ValidateReport(byte[] report, string op)
    {
        if (report == null) throw new ArgumentNullException(nameof(report));
        if (report.Length != WinApiConstants.PACKET_SIZE)
            throw new ArgumentException($"{op}: report must be exactly {WinApiConstants.PACKET_SIZE} bytes");
        if (report[0] != WinApiConstants.REPORT_ID)
            throw new ArgumentException($"{op}: Report ID must be 0x{WinApiConstants.REPORT_ID:X2}");
    }

    public void Close()
    {
        if (_handle != null && !_handle.IsClosed && !_handle.IsInvalid)
        {
            _handle.Close();
        }
        _handle = null;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Close();
            _disposed = true;
        }
    }
}