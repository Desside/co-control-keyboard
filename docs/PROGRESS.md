# Progress & Runbook

## Sprint status

| Sprint | Status | Verified on hardware |
| :--- | :--- | :--- |
| S1 Connectivity | DONE | yes — enumeration, open, attributes, model query |
| S2 RGB Base | DONE | yes — Direct Mode confirmed via S4_DirectRgbProbe (col06, FeatureLen=520) |
| S3 Memory & Input | DONE (code) | config Diff-Write verified; remap/macro **pages unverified** (see FlashLayout) |
| S4 Advanced | DONE | yes — 351 frames/no errors, CPU temp → color live |
| S5 Polish | DONE (code) | stress test + spectrum need a hardware run |

## Key protocol facts (verified)

- Vendor interface: `MI_01 col06`, the only one with `FeatureReportByteLength = 520`.
  Selected automatically by `DeviceEnumerator.FindVendorInterfacePath()`.
- Direct Mode packet (confirmed live, same as Aula F87 Pro — github.com/Ahorts/aula-f87pro):
  `[0x06, 0x08, 0x00, 0x00, 0x01, 0x00, 0x7A, 0x01]` + interleaved RGB (LED index × 3) + zero pad to 520.
- LED index space: 0..101 (102 LEDs), column-major with stride 6 (ESC=0, `=1, Tab=2 …).
- `SP_DEVICE_INTERFACE_DETAIL_DATA`: device path lives at offset **4**, not `cbSize` (8 on x64).
  Reading at 8 silently strips the leading `\\` and every `CreateFile` fails with 0x7B.
- Keyboard collections can't be opened with `GENERIC_READ` — enumerate attributes with
  `desiredAccess = 0`.

## Unverified assumptions (verify before writing!)

- `FlashLayout` page numbers for the remap table / macro slots are placeholders.
  Capture the vendor software with Wireshark + USBPcap before enabling
  `S3_MemoryTest --write-flash-pages`.
- Config region offsets in `ConfigOffsets` beyond `EffectId` params are partially guessed.

## How to run

```
dotnet test CoControl.sln                       # 100+ unit tests, no hardware needed
dotnet run --project tests/Diag_DevicePaths     # list HID interfaces
dotnet run --project tests/S4_AdvancedTest      # animations + sensors (admin for sensors)
dotnet run --project tests/S5_StressTest        # 50 flash writes + 60 s direct mode
dotnet run --project tests/S5_StressTest -- --full --minutes 60   # release gate
publish.cmd                                     # dist\cocontrol.exe (single file)
```

## CLI

```
cocontrol color FF6600        cocontrol pulse red 1.5
cocontrol rainbow             cocontrol temp 30 90
cocontrol spectrum            cocontrol off
```

## Remaining ideas (post-S5)

- Verify flash page layout → enable remap/macros on hardware.
- TFT upload test on an F75 Max (`S4_AdvancedTest --tft`).
- GUI (WPF) on top of `CoControl` library; tray app with profiles.
- Verify per-key LED map key-by-key (interactive mapping tool).
