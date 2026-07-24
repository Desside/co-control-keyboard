# Technical Specification: HID Transport & Protocol
## Target: Code-AI / Implementation Agent

### 1. USB Identifiers & Interface
- **Wired VID/PID**: `0x258A:0x010C`
- **2.4G VID/PID**: `0x258A:0x010D`
- **Target Interface**: `MI_01` (Vendor-defined collection)
- **Report ID**: `0x06` (Fixed at 520 bytes)

### 2. Windows API Transport Layer
To ensure the app remains stable and compatible with Windows 11 system updates (mimicking high-stability system utilities), the following WinAPI pattern must be used:

- **Device Discovery**: Use `SetupAPI` to enumerate HID devices and find the correct Device Path.
- **Handle Management**: Open the device using `CreateFile` with `GENERIC_READ | GENERIC_WRITE`.
- **Feature Reports**: Use `HidD_SetFeature` and `HidD_GetFeature` for the 520-byte configuration packets.
- **Bulk/Raw Data**: Use `WriteFile` and `ReadFile` for the TFT data stream on `MI_02`.

### 3. Protocol Command Reference
| Command | Action | Logic / Payload |
| :--- | :--- | :--- |
| `0x04` | Config Write | Write to Flash. **CRITICAL**: Requires CRC checksum validation. |
| `0x84` | Config Read | Read internal state. Returns a 136-byte response. |
| `0x06` | Per-Key RGB | Planar layout: R[126] $\to$ G[126] $\to$ B[126]. |
| `0x0A` | Custom Profile | Defined groups of 21 bytes (7 LEDs $\times$ 3 RGB). |
| `0x08` | Direct Mode | Bypasses Flash; real-time LED update (requires keepalive). |
| `0x82` | Model Query | Hardware identification string. |

### 4. Constraints
- **SinoWealth Chipset**: Speed/Brightness are hardware-clamped.
- **TFT Timing**: 4096-byte chunks with $\approx 65\text{ms}$ spacing.
- **Stability**: Implement a Command Queue to avoid USB buffer saturation.
