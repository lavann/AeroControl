# Per-user installation and startup

AeroControl supports both portable use and a non-elevated per-user setup executable.

## Portable

Extract the release ZIP and run `AeroControl.exe` directly. No files are copied elsewhere and startup is not configured automatically.

## Per-user setup

Run `AeroControl.Setup.exe` from the extracted release folder. It requires these sibling files:

- `AeroControl.exe`
- `LICENSE`
- `DISCLAIMER.md`

Setup installs those three files to:

```text
%LOCALAPPDATA%\AeroControl
```

No administrator rights or UAC elevation are requested. Setup refuses to update or remove files while AeroControl is running.

## Launch at sign-in

Both setup and the app Settings view can enable startup for the current user. The implementation writes a quoted executable path to:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run\AeroControl
```

Disabling the option removes that value only when it points to this AeroControl executable. No scheduled task, service, machine-wide registry value, or startup-folder shortcut is created.

## Removal

Use **Remove installation** in setup after closing AeroControl. Setup removes the installed executable, license, disclaimer, and any startup value owned by that installed executable. User preferences under `%LOCALAPPDATA%\AeroControl\settings.json` are intentionally preserved unless the user deletes them manually.

## Publisher warning

Current setup and app executables are unsigned. Windows can show Unknown publisher and SmartScreen warnings. Verify the release SHA-256 and do not disable Defender. Public signing remains tracked in GitHub issue #4.