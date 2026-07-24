using System;

namespace CoControl.Sensors;

public enum SensorChannel
{
    CpuTemperature,
    CpuLoad,
    GpuTemperature,
    GpuLoad,
    GpuVramUsage,
}

/// <summary>
/// Source of system metrics. Implementations: LibreHardwareMonitorProvider (real),
/// fakes in tests.
/// </summary>
public interface ISensorProvider : IDisposable
{
    /// <summary>Returns the current value, or null if the sensor is unavailable.</summary>
    float? Read(SensorChannel channel);
}
