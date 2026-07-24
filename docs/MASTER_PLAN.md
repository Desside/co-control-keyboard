# CO-CONTROL APP: Aula F75 — Project Master Plan
**Version 2.0 | Architecture & Roadmap**

## 1. Project Overview
The Co-Control App is a professional-grade Windows 11 system utility providing deep hardware-level control over the Aula F75 mechanical keyboard (SinoWealth MCU). It bypasses the limitations of official vendor software and exposes the full capability of the device through a clean, layered architecture.

### PROJECT OVERVIEW
| Item | Specification |
| :--- | :--- |
| **Target Device** | Aula F75 / F75 Max (SinoWealth MCU) |
| **Platform** | Windows 11 — WinUI 3 / WPF |
| **Communication** | USB HID, Feature Reports (520 bytes), WinAPI |
| **VID/PID (Wired)** | `0x258A : 0x010C` |
| **VID/PID (2.4G)** | `0x258A : 0x010D` |
| **Target Interface** | MI_01 (Vendor-defined) \| Report ID: 0x06 |
| **Architecture** | HAL $\to$ Protocol Engine $\to$ State Manager $\to$ Service $\to$ UI |
| **Total Sprints** | 5 sprints \| incremental delivery |

### 1.1 Core Goals
- **Deep Hardware Control**: Direct HID communication; per-key RGB; internal Flash memory management.
- **System Integration**: Real-time visualization of CPU/GPU sensor data on individual LEDs.
- **Precision Customization**: Key remapping, macro management, versioned profile sync.
- **Stability & Safety**: Diff-Write, CRC validation, Command Queue to prevent Flash corruption.

---

## 2. System Architecture
The system is decomposed into seven strictly ordered layers. Each layer communicates only with its immediate neighbors.

| LAYER | RESPONSIBILITY |
| :--- | :--- |
| **① UI Layer** | WinUI 3 / WPF — profiles, macros, animations, sensor dashboard |
| **② IPC Bridge** | Named Pipe / gRPC — decouples UI from hardware service |
| **③ Background Service** | Sensor polling (CPU/GPU), audio stream analysis, keepalive |
| **④ State Manager** | Local Shadow Copy of Flash — diff-write, CRC, cache |
| **⑤ Protocol Engine** | 520-byte packet assembly/disassembly, state machine, queue |
| **⑥ HAL** | WinAPI wrapper — `CreateFile`, `HidD_SetFeature/GetFeature`, `SetupAPI` |
| **⑦ USB HID / Raw Pipe** | Physical transport — Feature Reports (MI_01) + Bulk (MI_02 TFT) |

### 2.1 Data Flow
1. **UI $\to$ IPC Bridge $\to$ Background Service**: User intent, profile selection.
2. **Background Service $\to$ State Manager**: Desired LED state, macro triggers.
3. **State Manager $\to$ Protocol Engine**: Only diff bytes — not full frames.
4. **Protocol Engine $\to$ HAL**: Fully formed 520-byte Feature Reports.
5. **HAL $\to$ USB HID**: `CreateFile` handle, `HidD_SetFeature` / `WriteFile`.

---

## 3. Transport & HID Protocol

### 3.1 WinAPI Transport
- **Device Discovery**: `SetupAPI` enumerates HID devices by VID/PID and finds the device path for MI_01.
- **Handle Management**: `CreateFile` with `GENERIC_READ | GENERIC_WRITE`; one handle per session.
- **Feature Reports**: `HidD_SetFeature` (write) and `HidD_GetFeature` (read) for all 520-byte config packets.
- **TFT Stream**: `WriteFile` / `ReadFile` on MI_02 for raw bulk data (4096-byte chunks, 65ms spacing).

### 3.2 Command Reference
| CMD | ACTION | LOGIC / PAYLOAD |
| :--- | :--- | :--- |
| **0x04** | Config Write | Flash write — **CRITICAL**: must be preceded by CRC checksum validation |
| **0x84** | Config Read | Read internal state $\to$ 136-byte response payload |
| **0x06** | Per-Key RGB | Planar layout: R[126] $\to$ G[126] $\to$ B[126] (378 bytes total) |
| **0x0A** | Custom Profile | Defined LED groups — 21 bytes per group (7 LEDs $\times$ 3 RGB) |
| **0x08** | Direct Mode | Real-time LED update, bypasses Flash; requires periodic keepalive |
| **0x82** | Model Query | Hardware identification string (use for safe device validation) |

### 3.3 Packet Structure
- **Fixed size**: 520 bytes per Feature Report (Report ID 0x06).
- **Byte 0**: Report ID (always 0x06).
- **Byte 1**: Command opcode (e.g. 0x04, 0x08, 0x84 …).
- **Bytes 2–519**: Payload (command-specific; pad unused bytes with 0x00).

---

## 4. Module Specifications

### 4.1 HAL — Hardware Abstraction Layer
The HAL is the only module allowed to call WinAPI directly.
- `openDevice(path: string) \to handle`
- `setFeature(handle, buffer: byte[520]) \to void`
- `getFeature(handle, reportId: byte) \to byte[520]`
*Error handling: WinAPI errors are translated to typed HAL exceptions.*

### 4.2 Protocol Engine
- **Packet Builder**: Assembles 520-byte buffers from structured command objects.
- **Packet Parser**: Disassembles responses into typed result objects.
- **State Machine**: Enforces `Begin \to Data \to Apply \to Finish` sequence for Flash writes.
- **Command Queue**: Serializes all outbound packets to prevent USB buffer saturation.

### 4.3 State Manager (Local Shadow Copy)
- **In-Memory Mirror**: Keeps a copy of every Flash byte last written/read.
- **Diff Engine**: Compares desired state with shadow; only emits `0x04` packets for changed bytes.
- **CRC Guard**: Calculates CRC before every write; aborts if mismatch detected.
- **Persistence**: Serializes shadow to disk on profile save; reloads on startup.

### 4.4 RGB Engine
- **Planar Converter**: Transforms standard `[R,G,B]` arrays into `R[126] \to G[126] \to B[126]` layout.
- **Frame Buffer**: Maintains current and next frame for animation diffing.
- **Direct Mode Loop**: Pushes frames via `0x08` at 30–60 Hz with keepalive injection.
- **Gradient Mapper**: Maps float sensor values to interpolated RGB on a configurable gradient.

### 4.5 Background Service (Daemon)
- **Sensor Poller**: Integrates `LibreHardwareMonitor` (CPU/GPU temp, load, VRAM).
- **Audio Analyser**: Captures system audio via WASAPI loopback; performs FFT for frequency bands.
- **IPC Server**: Exposes Named Pipe / gRPC endpoint for UI commands.
- **Keepalive Manager**: Sends `0x08` pings while Direct Mode is active.

### 4.6 TFT Uploader (F75 Max only)
- Converts bitmap/PNG to device-native pixel format.
- Splits data into 4096-byte chunks via `MI_02` (`WriteFile`).
- Enforces 65ms inter-chunk delay to match hardware DMA timing.

---

## 5. Sprint Roadmap

| # | SPRINT | DELIVERABLES | MODULES / RISKS |
| :--- | :--- | :--- | :--- |
| **S1** | Connectivity Foundation | Device discovery $\to$ Open handle $\to$ Read/Write 520-byte packets $\to$ Basic error handling | HAL, SetupAPI \| Risk: LOW |
| **S2** | RGB Base Control | Per-key color (0x06) $\to$ Planar RGB conversion $\to$ Local profile save/load $\to$ UI color picker | Protocol Engine, RGB Engine, State Manager \| Risk: LOW |
| **S3** | Memory & Input Logic | Diff-Write (0x04) + CRC $\to$ Shadow copy sync $\to$ Key remapping $\to$ Macro recording | State Manager, Flash Manager, Input Engine \| Risk: MEDIUM |
| **S4** | Advanced Features | Direct Mode (0x08) @ 30–60 Hz $\to$ Sensor integration (LibreHardwareMonitor) $\to$ TFT upload | Animation Engine, Sensor Bridge, TFT Uploader \| Risk: HIGH |
| **S5** | Polish & Release | Audio visualizer $\to$ Performance profiling $\to$ Stress tests $\to$ Installer packaging | Audio Engine, QA Suite, Installer \| Risk: MEDIUM |

### 5.1 Definition of Done
- All deliverables in the sprint table are implemented and unit-tested.
- No regression in previously shipped layers.
- Stress test: 500 consecutive writes without CRC error or USB timeout.
- Code review sign-off before merge.

---

## 6. Hardware Constraints & Risk Register
| CONSTRAINT | MITIGATION / RULE |
| :--- | :--- |
| **Flash Wear** | Diff-Write only — compare Shadow Copy before every `0x04` write. |
| **CRC Integrity** | CRC must be computed and validated before every config write. |
| **USB Saturation** | Command Queue mandatory — never write packets back-to-back. |
| **SinoWealth Limits** | Speed and Brightness are hardware-clamped — do not override. |
| **TFT Timing** | 4096-byte chunks with $\approx 65\text{ms}$ inter-chunk delay (MI_02 bulk). |
| **Direct Mode** | Keepalive packet must be sent periodically or LEDs revert to firmware. |
| **Flash Corruption** | Never cut power or disconnect during a `0x04` write sequence. |

---

## 7. Testing Strategy
- **Unit Tests**: Packet Builder, Planar Converter, CRC Calculator, Diff Engine.
- **Integration Tests**: HAL mock injection, end-to-end profile round-trip (write $\to$ read $\to$ compare).
- **Hardware Stress Tests**: 500 consecutive profile writes, Direct Mode 60Hz for 1 hour, 50x TFT uploads.

---

## 8. Glossary
- **HAL**: Hardware Abstraction Layer — lowest-level module; only layer touching WinAPI.
- **Shadow Copy**: In-memory mirror of keyboard Flash memory used to compute write diffs.
- **Diff-Write**: Strategy of sending only changed bytes to minimise Flash erase/write cycles.
- **Planar RGB**: Device layout where all R values for all keys precede all G, then all B values.
- **Direct Mode**: `0x08` command that bypasses Flash and drives LEDs directly from host RAM.
- **Keepalive**: Periodic packet required to maintain Direct Mode.
- **Feature Report**: 520-byte HID packet exchanged via `HidD_SetFeature` / `HidD_GetFeature`.
- **MI_01 / MI_02**: HID interface indices on the Aula F75.
- **SetupAPI**: Windows API used to enumerate connected HID devices and retrieve device paths.
