## Summary

Describe the focused change and why it is needed.

## Validation

- [ ] `dotnet format AeroControl.sln --verify-no-changes`
- [ ] `dotnet build AeroControl.sln -c Release`
- [ ] `dotnet test AeroControl.sln -c Release`
- [ ] Demo-mode UI checked when presentation changed

## Hardware safety

- [ ] No firmware write behavior changed
- [ ] Or: model, BIOS, bounded inputs, readback, reversal, tests, and compatibility evidence are documented
- [ ] No proprietary binaries, firmware, copied vendor code, secrets, or device identifiers are included

## Screenshots

Include before/after images for visible changes.
