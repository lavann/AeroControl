# Contributing to AeroControl

AeroControl welcomes fixes, model reports, tests, and carefully validated hardware support. Hardware-control changes have a higher review bar than ordinary UI changes.

## Before opening a change

1. Search existing issues for the model and firmware method.
2. Open a hardware-support issue before adding a new write sequence.
3. Remove serial numbers, UUIDs, MAC addresses, Windows usernames, and other identifiers from diagnostics.
4. Explain how the setting can be read back and safely reversed.

## Development setup

```powershell
dotnet restore
dotnet build AeroControl.sln
dotnet test AeroControl.sln
```

Use demo mode for UI work:

```powershell
dotnet run --project src/AeroControl/AeroControl.csproj -- --demo
```

## Hardware-control requirements

A pull request that adds or changes a firmware write must include:

- The exact supported model/SKU and BIOS version
- Evidence that the method is exposed by the installed WMI provider
- Input semantics from public documentation or controlled read/write/readback testing
- A bounded input range
- A reversible automatic/default path
- Unit tests using `IWmiBridge`; tests must never write to real hardware
- UI acknowledgement when the operation carries new risk
- Compatibility documentation

Do not submit proprietary Gigabyte binaries, firmware images, decompiled source, copied UI assets, secrets, or personal device identifiers. Public WMI class/method metadata and independently written interoperability code are acceptable.

## Code style

- Keep the hardware layer independent of WPF.
- Prefer capability checks over model-name branching.
- Keep model allowlists narrow when semantics differ.
- Return explicit failures; never claim a write succeeded solely because no exception was thrown when readback exists.
- Preserve automatic firmware control as the safe fallback.
- Run `dotnet format --verify-no-changes` before requesting review.

## Pull requests

Keep changes focused and explain the safety argument. Include screenshots for visible UI changes and update the README image when the primary dashboard changes.
