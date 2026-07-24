# Project: Co-Control App Keyboard (Aula F75)
## Introduction

### Overview
The "Co-Control App Keyboard" is a professional-grade system utility for Windows 11 designed to provide deep hardware-level control over the Aula F75 mechanical keyboard. The goal is to bypass the limitations of official vendor software and implement a high-performance interface for managing the keyboard's internal memory and lighting.

### Goals
- **Deep Hardware Control**: Direct interaction with the USB HID protocol to manage per-key RGB and internal Flash memory.
- **System Integration**: Real-time visualization of system sensors (CPU/GPU temperature) and audio-reactive lighting.
- **Precision Customization**: Professional tools for key remapping, macro management, and profile synchronization.

### Target Hardware
- **Device**: Aula F75 / F75 Max
- **MCU**: SinoWealth (HID-based)
- **Capabilities**: 75% Layout, Per-key RGB, Internal Memory, TFT Screen (Max variant).

### Core Philosophy
The project is built on a "Low-Level First" architecture. By treating the keyboard as a programmable device, the app will enable features such as host-side animation rendering, precise sensor-to-LED mapping, and a safe, versioned approach to internal memory management.
