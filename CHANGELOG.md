# Changelog

All notable changes to AeroControl are documented here.

The project follows semantic versioning after the initial public preview.

## [Unreleased]

### Planned

- Community compatibility reports
- Additional model-validated device controls

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
