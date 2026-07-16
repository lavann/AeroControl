# Security policy

## Supported versions

AeroControl is pre-1.0 software. Security fixes are applied to the latest revision on the default branch.

## Reporting a vulnerability

Do not open a public issue for vulnerabilities that could enable arbitrary WMI calls, privilege-boundary bypass, unsafe firmware writes, command injection, or disclosure of device identifiers.

Use GitHub private vulnerability reporting for this repository. Include:

- The affected revision
- Reproduction steps using demo or fake hardware where possible
- The expected and observed safety boundary
- Whether real hardware was affected

Do not include serial numbers, account names, or unrelated diagnostics. Maintainers will acknowledge a complete report, assess impact, and coordinate disclosure after a fix is available.

## Security boundaries

AeroControl is not a Windows security boundary. Administrator elevation is visible and initiated by the operator. Risk acknowledgement is a human-safety gate, not an authorization mechanism. Firmware and embedded-controller safety remain the responsibility of the device firmware.
