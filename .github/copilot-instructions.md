# AeroControl repository instructions

- [x] Project requirements clarified
- [x] .NET 8 WPF solution scaffolded
- [x] Hardware provider and safety gate implemented
- [x] Build, format, test, publish, and debug tasks configured
- [x] README, screenshot, architecture diagram, license, and disclaimer added

## Engineering rules

- Keep firmware logic in `AeroControl.Core`; WPF must depend on `IAeroHardwareService` rather than `System.Management` directly.
- Never add proprietary Gigabyte/AORUS binaries, firmware, decompiled source, copied UI assets, secrets, or personal device identifiers.
- Treat every firmware write as model-sensitive. Require bounded input, capability detection, a reversible default path, readback when available, fake-bridge tests, and compatibility evidence.
- Automated tests must never instantiate the production WMI bridge or write to real hardware.
- Preserve automatic firmware fan control as the safe fallback and keep restore-on-exit enabled by default.
- Do not broaden the verified-model claim without exact model/SKU and BIOS evidence.
- Keep the app usable without elevation; request administrator access visibly only when the firmware provider requires it.
- Keep the MIT license and hardware-specific disclaimer visible in documentation and before the first write.
- Run `dotnet format AeroControl.sln --verify-no-changes`, `dotnet build AeroControl.sln -c Release`, and `dotnet test AeroControl.sln -c Release --no-build` before publication.
