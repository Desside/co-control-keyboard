using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CoControl.HAL;

/// <summary>
/// Raw bulk write surface (TFT stream on MI_02). Testable via fakes.
/// </summary>
public interface IRawPipe
{
    void Write(ReadOnlySpan<byte> data);
}

/// <summary>
/// WriteFile-based bulk pipe for the F75 Max TFT screen (MI_02 interface).
/// </summary>
public sealed class RawPipeDevice : IRawPipe, IDisposable
{
    private SafeFileHandle? _handle;
    private bool _disposed;

    public bool IsOpen => _handle != null && !_handle.IsInvalid && !_handle.IsClosed;

    public void Open(string devicePath)
    {
        if (IsOpen) throw new InvalidOperationException("Pipe already open");

        _handle = NativeMethods.CreateFile(
            devicePath,
            NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (_handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            throw new IOException($"CreateFile failed (0x{error:X}): {devicePath}", Marshal.GetExceptionForHR(error));
        }
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (!IsOpen) throw new InvalidOperationException("Pipe not open");

        byte[] buffer = data.ToArray();
        if (!NativeMethods.WriteFile(_handle!, buffer, (uint)buffer.Length, out uint written, IntPtr.Zero))
        {
            int error = Marshal.GetLastWin32Error();
            throw new IOException($"WriteFile failed (0x{error:X})", Marshal.GetExceptionForHR(error));
        }
        if (written != buffer.Length)
            throw new IOException($"Short write: {written}/{buffer.Length} bytes");
    }

    public void Close()
    {
        if (_handle != null && !_handle.IsClosed && !_handle.IsInvalid)
            _handle.Close();
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
