using System;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace CoControl.Sensors;

/// <summary>
/// Reads CPU/GPU metrics via LibreHardwareMonitorLib.
/// NOTE: full sensor access requires the process to run elevated (admin).
/// Without elevation some channels return null.
/// </summary>
public sealed class LibreHardwareMonitorProvider : ISensorProvider
{
    private readonly Computer _computer;
    private readonly object _lock = new();
    private bool _disposed;

    public LibreHardwareMonitorProvider()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
        };
        _computer.Open();
    }

    public float? Read(SensorChannel channel)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var (hwTypes, sensorType, nameHint) = channel switch
            {
                SensorChannel.CpuTemperature => (new[] { HardwareType.Cpu }, SensorType.Temperature, (string?)null),
                SensorChannel.CpuLoad => (new[] { HardwareType.Cpu }, SensorType.Load, "Total"),
                SensorChannel.GpuTemperature => (GpuTypes, SensorType.Temperature, null),
                SensorChannel.GpuLoad => (GpuTypes, SensorType.Load, "Core"),
                SensorChannel.GpuVramUsage => (GpuTypes, SensorType.SmallData, "Memory Used"),
                _ => throw new ArgumentOutOfRangeException(nameof(channel)),
            };

            foreach (var hw in _computer.Hardware.Where(h => hwTypes.Contains(h.HardwareType)))
            {
                hw.Update();
                var sensors = hw.Sensors.Where(s => s.SensorType == sensorType && s.Value.HasValue);
                var sensor = nameHint == null
                    ? sensors.FirstOrDefault()
                    : sensors.FirstOrDefault(s => s.Name.Contains(nameHint, StringComparison.OrdinalIgnoreCase))
                      ?? sensors.FirstOrDefault();
                if (sensor?.Value is { } v) return v;
            }
            return null;
        }
    }

    private static readonly HardwareType[] GpuTypes =
    {
        HardwareType.GpuNvidia, HardwareType.GpuAmd, HardwareType.GpuIntel,
    };

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _computer.Close();
        }
    }
}
