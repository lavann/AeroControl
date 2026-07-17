# AeroControl repository instructions

- [x] Project requirements clarified
- [x] .NET 8 WPF solution scaffolded
- [x] Hardware provider and safety gate implemented
- [x] Build, format, test, publish, and debug tasks configured
- [x] README, screenshot, architecture diagram, license, and disclaimer added

## Session continuity

- `OPERATOR_LOG.md` and `NEXT_STEPS.md` are intentionally ignored local handoff files.
- At session start, first verify the current branch, HEAD, and working-tree state, then read both files when present. Treat them as orientation only; Git, current code, and fresh validation are authoritative.
- Before building, testing, publishing, or beginning new work, fetch the configured upstream and verify whether the current branch is behind or diverged. Fast-forward only when the working tree is clean; otherwise preserve local work and resolve the state explicitly. Never validate or publish a checkout known to be stale.
- After the final commit or upstream integration, rerun the required Release build and tests against that exact source state before pushing or publishing.
- When the operator signals the end of the workday, append a dated outcome to `OPERATOR_LOG.md` and rewrite `NEXT_STEPS.md` with the verified branch, HEAD, working-tree state, validation results, blockers, and exact first action for the next session.
- Keep both files local. Never force-add them or record secrets, credentials, personal device identifiers, raw diagnostics, or proprietary material in them.

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
