using System;
using System.IO;
using CoControl.Protocol;

namespace CoControl.State;

/// <summary>
/// Local Shadow Copy of keyboard Flash (136-byte config region).
/// Implements Diff-Write with two-phase commit to minimize Flash wear:
///   1. Mutate via <see cref="Mutate"/> / <see cref="SetByte"/> / <see cref="SetRange"/>.
///   2. <see cref="TryBeginWrite"/> — snapshot of pending state + CRC.
///   3. After the device confirms the write: <see cref="CommitWrite"/>.
///      On USB/CRC failure: <see cref="AbortWrite"/> (shadow stays dirty, retry possible).
/// </summary>
public sealed class ShadowConfig : IDisposable
{
    public const int CONFIG_SIZE = 136;

    private readonly byte[] _shadow = new byte[CONFIG_SIZE];       // desired state
    private readonly byte[] _lastWritten = new byte[CONFIG_SIZE];  // last state confirmed by device
    private byte[]? _pendingWrite;                                 // snapshot sent to device, awaiting commit
    private bool _dirty;
    private readonly string _persistPath;
    private readonly object _lock = new();

    public ShadowConfig(string? persistPath = null)
    {
        _persistPath = persistPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CoControl", "shadow.bin");

        Load();
    }

    /// <summary>True if shadow differs from last device-confirmed state.</summary>
    public bool IsDirty { get { lock (_lock) return _dirty; } }

    /// <summary>True if a write was started but not yet committed/aborted.</summary>
    public bool HasPendingWrite { get { lock (_lock) return _pendingWrite != null; } }

    /// <summary>Returns a defensive copy of the current desired state.</summary>
    public byte[] Snapshot()
    {
        lock (_lock)
        {
            return (byte[])_shadow.Clone();
        }
    }

    /// <summary>
    /// Syncs shadow from a fresh device read (startup / after Config Read).
    /// Clears dirty state — the device is the source of truth here.
    /// </summary>
    public void SyncFromDevice(ReadOnlySpan<byte> deviceConfig)
    {
        if (deviceConfig.Length != CONFIG_SIZE)
            throw new ArgumentException($"Config must be {CONFIG_SIZE} bytes");

        lock (_lock)
        {
            deviceConfig.CopyTo(_shadow);
            deviceConfig.CopyTo(_lastWritten);
            _pendingWrite = null;
            _dirty = false;
        }
        Save();
    }

    /// <summary>
    /// Applies a mutation to a copy of the shadow, then diffs it back in.
    /// This is the only safe way to expose the buffer to callers:
    /// dirty tracking cannot be bypassed.
    /// Returns true if anything actually changed.
    /// </summary>
    public bool Mutate(Action<byte[]> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);
        byte[] copy = Snapshot();
        mutator(copy);
        return SetRange(0, copy);
    }

    /// <summary>Mutates a single byte. Returns true if changed.</summary>
    public bool SetByte(int offset, byte value)
    {
        if (offset < 0 || offset >= CONFIG_SIZE) throw new ArgumentOutOfRangeException(nameof(offset));

        lock (_lock)
        {
            if (_shadow[offset] == value) return false;
            _shadow[offset] = value;
            _dirty = true;
            return true;
        }
    }

    /// <summary>Mutates a range. Returns true if any byte changed.</summary>
    public bool SetRange(int offset, ReadOnlySpan<byte> values)
    {
        if (offset < 0 || offset + values.Length > CONFIG_SIZE) throw new ArgumentOutOfRangeException(nameof(offset));

        lock (_lock)
        {
            bool changed = false;
            for (int i = 0; i < values.Length; i++)
            {
                if (_shadow[offset + i] != values[i])
                {
                    _shadow[offset + i] = values[i];
                    changed = true;
                }
            }
            if (changed) _dirty = true;
            return changed;
        }
    }

    /// <summary>Number of bytes that differ from the last confirmed device state.</summary>
    public int DiffCount()
    {
        lock (_lock)
        {
            int n = 0;
            for (int i = 0; i < CONFIG_SIZE; i++)
                if (_shadow[i] != _lastWritten[i]) n++;
            return n;
        }
    }

    /// <summary>
    /// Phase 1 of Diff-Write: if the shadow is dirty, snapshots the desired
    /// state and returns it together with its CRC16. The shadow stays dirty
    /// until <see cref="CommitWrite"/> confirms the device accepted the data.
    /// Returns false if there is nothing to write.
    /// </summary>
    public bool TryBeginWrite(out byte[] config136, out ushort crc)
    {
        lock (_lock)
        {
            if (!_dirty)
            {
                config136 = Array.Empty<byte>();
                crc = 0;
                return false;
            }

            _pendingWrite = (byte[])_shadow.Clone();
            config136 = (byte[])_pendingWrite.Clone();
            crc = Crc16.Compute(_pendingWrite);
            return true;
        }
    }

    /// <summary>
    /// Phase 2 of Diff-Write: the device confirmed the write.
    /// Pending snapshot becomes the new baseline; dirty is cleared unless
    /// the shadow was mutated again while the write was in flight.
    /// </summary>
    public void CommitWrite()
    {
        lock (_lock)
        {
            if (_pendingWrite == null) throw new InvalidOperationException("No pending write to commit");

            _pendingWrite.AsSpan().CopyTo(_lastWritten);

            // Still dirty if shadow moved on while the packet was in flight.
            _dirty = !_shadow.AsSpan().SequenceEqual(_lastWritten);
            _pendingWrite = null;
        }
        Save();
    }

    /// <summary>The write failed — keep shadow dirty so it can be retried.</summary>
    public void AbortWrite()
    {
        lock (_lock)
        {
            _pendingWrite = null;
        }
    }

    /// <summary>Persists shadow to disk.</summary>
    public void Save()
    {
        try
        {
            byte[] copy = Snapshot();
            Directory.CreateDirectory(Path.GetDirectoryName(_persistPath)!);
            File.WriteAllBytes(_persistPath, copy);
        }
        catch { /* persistence is best-effort */ }
    }

    /// <summary>Loads shadow from disk (cold start, before first device read).</summary>
    public void Load()
    {
        try
        {
            if (!File.Exists(_persistPath)) return;
            var data = File.ReadAllBytes(_persistPath);
            if (data.Length != CONFIG_SIZE) return;

            lock (_lock)
            {
                data.AsSpan().CopyTo(_shadow);
                data.AsSpan().CopyTo(_lastWritten);
                _dirty = false;
            }
        }
        catch { /* ignore */ }
    }

    public void Dispose() => Save();
}

/// <summary>
/// High-level config field offsets (per protocol spec).
/// </summary>
public static class ConfigOffsets
{
    // Header: 0-7
    // Mode config: 8-15
    public const int ModeCount = 16;           // byte 16: number of modes (0x07)
    public const int CustomModeFlag = 17;      // byte 17: 0=HW effect, 1=Custom per-key
    public const int EffectId = 18;            // byte 18: Effect ID (0x00..0x12)
    public const int SideLightEffect = 26;     // byte 26: Side light effect ID
    public const int BatteryLightEffect = 36;  // byte 36: Battery indicator effect ID

    // Per-effect params: 64 + 2*effect_id (brightness, speed_flags)
    public static int EffectParamOffset(int effectId) => 64 + 2 * effectId;
    // Byte 0: brightness (1-4)
    // Byte 1: (speed << 4) | flags
}
