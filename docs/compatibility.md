# Hardware compatibility

AeroControl discovers firmware capabilities at runtime. A method being present proves only that the firmware exposes its name; it does not prove identical semantics across laptop generations.

## Verified configuration

| Field | Value |
| --- | --- |
| Manufacturer | GIGABYTE |
| Model | AERO 15-SA |
| System SKU | P75SA |
| CPU | Intel Core i7-9750H |
| BIOS | FB09 |
| Operating system | Windows 11, build 26200 |
| Firmware classes | `GB_WMIACPI_Get`, `GB_WMIACPI_Set` |

Verified fan-duty mapping:

| Requested duty | Raw firmware value | Observed Fan 1 | Observed Fan 2 |
| ---: | ---: | ---: | ---: |
| 70% | 160 | Model-specific; not yet recorded | Model-specific; not yet recorded |
| 80% | 183 | 4,637 RPM | 4,597 RPM |
| 100% | 229 | 5,404 RPM | 5,310 RPM |

RPM is not linear with electrical duty and varies with hardware, voltage, temperature, dust, bearing wear, and firmware. The observed values are evidence that the commands took effect on one machine, not target RPM guarantees.

## Required fan methods

Read path:

- `getCpuTemp`
- `getGpuTemp1`
- `getRpm1`
- `getRpm2`
- `GetCPUFanDuty`
- `GetGPUFanDuty`
- `GetAutoFanStatus`
- `GetFixedFanStatus`
- `GetFixedFanSpeed`

Write path:

- `SetAutoFanStatus`
- `SetStepFanStatus`
- `SetFixedFanStatus`
- `SetFixedFanSpeed`
- `SetGPUFanDuty`

AeroControl refuses fan writes when the required write methods are absent.

## Read-only capability collection

The following Administrator PowerShell commands list method names without invoking them:

```powershell
([wmiclass]'\\.\root\WMI:GB_WMIACPI_Get').psbase.Methods.Name | Sort-Object
([wmiclass]'\\.\root\WMI:GB_WMIACPI_Set').psbase.Methods.Name | Sort-Object
```

When filing a model-support issue, include the model, SKU, BIOS version, Windows version, and method names. Remove serial numbers, UUIDs, MAC addresses, and usernames.

## Support levels

- **Verified:** Maintainers or contributors completed controlled write/readback testing on the exact model.
- **Experimental:** Required methods are detected, but semantics have not been validated on that model.
- **Unsupported:** Required WMI classes or methods are missing.

Do not infer compatibility from branding alone. AERO and AORUS generations may use different embedded controllers, WMI schemas, or value encodings.
