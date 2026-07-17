# Contributing to AeroControl

AeroControl welcomes fixes, model reports, tests, and carefully validated hardware support. Hardware-control changes have a higher review bar than ordinary UI changes.

## Before opening a change

1. Search existing issues for the model and firmware method.
2. Open a [hardware-support issue](https://github.com/lavann/AeroControl/issues/new?template=hardware-support.yml) before adding a new write sequence.
3. Prefer the versioned Diagnostics JSON export and inspect it before uploading.
4. Explain how the setting can be read back and safely reversed.

## Compatibility reports

In AeroControl, open **Diagnostics**, select **Refresh**, then **Export JSON**. The `aerocontrol.compatibility-report.v1` schema is bounded, sanitized, and marked `IsEvidenceOnly`; it contains no raw firmware payloads and never enables writes. Review every exported file before attaching it to an issue.

If the export is unavailable, use the read-only method discovery commands in [docs/compatibility.md](docs/compatibility.md). Never invoke unknown methods to gather a report. Remove serial numbers, UUIDs, MAC addresses, Windows usernames, raw paths, secrets, and unrelated system details from all submissions.

Reports are evidence for maintainers to review. They do not make a model verified, generate a model allowlist entry, or satisfy the controlled write/readback requirements below.

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
