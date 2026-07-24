# Implementation Guide: Application Architecture
## Target: Code-AI / Implementation Agent

### 1. High-Level Structural Design
The application must be architected to decouple the volatile UI from the critical hardware communication layer, following the pattern of professional drivers.

**Architecture Layers:**
- **HAL (Hardware Abstraction Layer)**: Low-level WinAPI wrapper. Handles raw byte arrays and `CreateFile` handles.
- **Protocol Engine**: Logic for assembling/disassembling the 520-byte packets. Manages the state machine (Begin $\to$ Data $\to$ Apply $\to$ Finish).
- **State Manager (Local Shadow Copy)**: Maintains a local mirror of the keyboard's Flash memory. This prevents "USB lag" by reducing `GET_REPORT` calls.
- **Background Service**: A daemon for real-time sensor polling (CPU/GPU) and audio stream analysis.
- **UI Layer**: WinUI 3 / WPF interface. Communicates with the Service via IPC.

### 2. Critical Module Implementation
#### A. Memory Safety (Flash Management)
- **Diff-Writing**: Compare Local Shadow Copy with the desired state. Only send `0x04` packets for modified bytes to reduce Flash wear.
- **CRC Calculation**: Every `0x04` write must be preceded by a CRC check to prevent firmware corruption.

#### B. RGB Engine
- **Planar-to-Interleaved**: Convert standard RGB colors to the Planar format required by the `0x06` command.
- **Host-Side Rendering**: Implement a frame-buffer loop for animations, pushing data via Direct Mode (`0x08`) at 30-60Hz.

#### C. Sensor Mapping
- **Interface**: Integrate `LibreHardwareMonitor` for system metrics.
- **Interpolation**: Map `Temperature (float)` $\to$ `ColorGradient (RGB)` $\to$ `LED Index`.

### 3. Roadmap for Implementation
1. **Sprint 1 (Connectivity)**: Device discovery $\to$ Read/Write simple 520-byte packets.
2. **Sprint 2 (RGB Base)**: Per-key color control $\to$ Planar conversion $\to$ Local profile save.
3. **Sprint 3 (Memory & Logic)**: Diff-Write implementation $\to$ Key remapping $\to$ Macro storage.
4. **Sprint 4 (Advanced)**: Direct Mode animations $\to$ System sensor integration $\to$ TFT upload.
5. **Sprint 5 (Final)**: Audio visualizer $\to$ Performance optimization $\to$ Stability stress-tests.
