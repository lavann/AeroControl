# Changelog

All notable changes to AeroControl are documented here.

The project follows semantic versioning after the initial public preview.

## [Unreleased]

### Planned

- Additional model-validated device controls

## [0.4.0] - 2026-07-17

### Compatibility reporting

- Versioned, bounded compatibility-report JSON with an explicit evidence-only marker and sanitized read-only telemetry
- Guided hardware-support intake for reviewed diagnostics exports and manual method-name fallback

## [0.3.0] - 2026-07-16

### Added

- Session-only Monitor charts for temperatures, dual-fan RPM, battery charge, and power flow
- Explicit CSV telemetry export with bounded, non-personal columns
- Sanitized Diagnostics view and JSON export for compatibility support
- Notification-area icon, sustained temperature alerts, and fixed-mode fan-stall warnings
- Per-user persistent settings for refresh/history, alerts, remembered view, tray behavior, and restore-on-exit
- Named fan profiles that reuse the existing verified fixed-duty command path
- Per-user setup executable targeting `%LOCALAPPDATA%\AeroControl`
- Reversible current-user sign-in startup option through `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`

## [0.2.0] - 2026-07-16

### Added

- Persistent Cooling and Battery navigation tabs, with Cooling as the default launch view
- Read-only Windows battery dashboard for charge, power state, voltage, capacity retention, and reported cycles
- Structured `powercfg /batteryreport /xml` parsing for design and full-charge capacity
- Deterministic `--view cooling|battery` screenshot selection
- Release-signing guidance for Microsoft Artifact Signing, SmartScreen, and UAC publisher identity

### Changed

- CI smoke-renders both application tabs from the published executable

## [0.1.2] - 2026-07-16

### Fixed

- Stop requiring the broken optional `GetGPUFanDuty` getter after fixed fan commands
- Verify fixed mode through automatic/step/fixed status and `GetFixedFanSpeed`, matching the working vendor/PowerShell sequence
- Preserve the `SetGPUFanDuty` write while treating its getter as optional telemetry
- Include the exact WMI method name in future getter/setter failure messages

## [0.1.1] - 2026-07-16

### Fixed

- Decode `getRpm1` and `getRpm2` as Gigabyte's byte-packed `UInt16` format instead of displaying the raw value
- Reject implausible decoded fan speeds above 10,000 RPM
- Treat the verified configuration's broken `GetCPUFanDuty` getter as optional
- Use `GetFixedFanSpeed` as the CPU/system duty fallback while fixed mode is active
- Stop surfacing optional CPU-duty getter failures as dashboard errors

## [0.1.0] - 2026-07-16

### Added

- .NET 8 WPF cooling dashboard
- Gigabyte WMI capability discovery
- CPU/GPU temperature and dual-fan telemetry
- Automatic and fixed 30-100% fan control
- Verified 70%, 80%, and 100% presets for AERO 15-SA
- Automatic-mode restoration on exit
- Administrator restart flow
- Versioned hardware-risk acknowledgement
- Demo provider and deterministic screenshot capture
- Unit-tested WMI abstraction with no hardware writes in tests
- Exact manufacturer/model/SKU/BIOS write allowlist
- Full fixed/automatic readback and automatic rollback after partial failures
- Hardware-bound risk acceptance and bounded WMI operations
- MIT license and hardware-specific disclaimer
