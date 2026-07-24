# CoControl Keyboard

Hardware control application for co-control keyboards with RGB lighting, TFT display, audio control, and sensor monitoring.

## Architecture

```
src/
├── Audio/           # Audio control (volume, devices via NAudio)
├── HAL/             # Hardware Abstraction Layer
├── Input/           # Input handling (keyboard, encoder, touch)
├── Protocol/        # Communication protocol (serial/USB)
├── Rgb/             # RGB lighting control
├── Sensors/         # Sensor monitoring (temperature, etc.)
├── Service/         # Background services
├── State/           # State management
├── Tft/             # TFT display driver
├── UI/              # UI components (Avalonia)
├── CoControl/       # Core library (net8.0-windows)
└── CoControl.App/   # Console/Host application (net8.0-windows)
```

## Tech Stack

- **.NET 8** (Windows, x64)
- **Avalonia UI** - Cross-platform UI framework
- **NAudio** - Audio device control
- **LibreHardwareMonitorLib** - Hardware monitoring
- **LibreHardwareMonitorLib** - Hardware sensors
- **System.Device.Gpio** - GPIO for hardware I/O

## Project Structure

| Project | Type | Description |
|---------|------|-------------|
| `CoControl` | Class Library | Core library (HAL, Protocol, RGB, Sensors, State, Audio, Input, TFT, UI) |
| `CoControl.App` | Console App | Host application entry point |
| `CoControl.Tests` | Test Project | Unit tests (xUnit) |
| `CoControl.UnitTests` | Test Project | Additional unit tests |

## Building

```bash
# Restore & Build
dotnet build src/CoControl.sln

# Run app
dotnet run --project src/CoControl.App/CoControl.App.csproj

# Run tests
dotnet test src/CoControl.Tests/CoControl.Tests.csproj
dotnet test src/CoControl.UnitTests/CoControl.UnitTests.csproj
```

## Publish (Single-file, self-contained)

```bash
dotnet publish src/CoControl.App/CoControl.App.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:PublishTrimmed=true \
  -o ./publish/win-x64
```

## Project Structure Details

### Core Library (`src/CoControl/`)

| Module | Responsibility |
|--------|---------------|
| `Audio/` | Volume control, device enumeration via NAudio |
| `HAL/` | Hardware abstraction (GPIO, I2C, SPI, UART) |
| `Input/` | Keyboard matrix, rotary encoder, touch input |
| `Protocol/` | Serial/USB protocol (packets, CRC, commands) |
| `Rgb/` | RGB LED control (animations, effects, zones) |
| `Sensors/` | Temperature, humidity, voltage sensors |
| `Service/` | Background services (polling, monitoring) |
| `State/` | Reactive state management (ReactiveUI) |
| `Tft/` | TFT display driver (ST7789, ILI9341, etc.) |
| `UI/` | Avalonia views, view models, styles |

### Application (`src/CoControl.App/`)

Entry point hosting the application host, DI container, and service lifecycle.

## Dependencies

| Package | Purpose |
|---------|---------|
| `LibreHardwareMonitorLib` | CPU/GPU/RAM sensors |
| `NAudio` | Audio endpoint volume control |
| `System.Device.Gpio` | GPIO pin control |
| `System.IO.Ports` | Serial port communication |
| `Avalonia` | UI framework |
| `ReactiveUI` | Reactive MVVM |

## Development

```bash
# Watch & run (hot reload)
dotnet watch run --project src/CoControl.App

# Format
dotnet format src/CoControl.sln

# Analyze
dotnet build src/CoControl.sln --no-restore -v q
```

## Architecture Notes

- **Core library** (`CoControl`) targets `net8.0-windows` with `AllowUnsafeBlocks`
- **App** references core library via `ProjectReference`
- **InternalsVisibleTo** for test projects
- **PlatformTarget: x64** required for native interop (GPIO, Hid, Audio)
- **Nullable enabled**, **ImplicitUsings enabled**

## Hardware Support

- **Keyboards**: Custom co-control keyboards (matrix + encoder + RGB)
- **Displays**: SPI TFT (ST7789, ILI9341, GC9A01)
- **Sensors**: LM75, SHT3x, ADS1115 (via I2C)
- **Audio**: Windows Core Audio API via NAudio
- **RGB**: WS2812/WS2812B, SK6812 via SPI/UART

## License

MIT License - see LICENSE file for details.
