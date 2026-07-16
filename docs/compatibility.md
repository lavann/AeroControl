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

The `getRpm1` and `getRpm2` methods return a byte-packed `UInt16`, not a literal RPM value. AeroControl decodes it using the same byte order as the vendor utility:

```text
rpm = ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF)
```

For example, raw values `34828` and `13580` decode to `3208 RPM` and `3125 RPM`. Version 0.1.0 displayed the raw values; this is fixed in 0.1.1.

## Fan methods

Minimum telemetry:

- `getCpuTemp`
- `getRpm1`
- `getRpm2`

Optional telemetry:

- `getGpuTemp1`
- `GetCPUFanDuty`
- `GetGPUFanDuty`
- `GetAutoFanStatus`
- `GetStepFanStatus`
- `GetFixedFanStatus`
- `GetFixedFanSpeed`

`GetCPUFanDuty` is advertised by the verified firmware but returns `Invalid object`. AeroControl ignores that optional failure and uses `GetFixedFanSpeed` as the system-fan duty readback while fixed mode is active.

Mandatory readback before fan writes:

- `GetGPUFanDuty`
- `GetAutoFanStatus`
- `GetStepFanStatus`
- `GetFixedFanStatus`
- `GetFixedFanSpeed`

Mandatory write path:

- `SetAutoFanStatus`
- `SetStepFanStatus`
- `SetFixedFanStatus`
- `SetFixedFanSpeed`
- `SetGPUFanDuty`

AeroControl refuses fan writes when any mandatory write or readback method is absent.

## Read-only capability collection

The following Administrator PowerShell commands list method names without invoking them:

```powershell
([wmiclass]'\\.\root\WMI:GB_WMIACPI_Get').psbase.Methods.Name | Sort-Object
([wmiclass]'\\.\root\WMI:GB_WMIACPI_Set').psbase.Methods.Name | Sort-Object
```

When filing a model-support issue, include the model, SKU, BIOS version, Windows version, and method names. Remove serial numbers, UUIDs, MAC addresses, and usernames.

## Support levels

- **Verified:** Maintainers or contributors completed controlled write/readback testing on the exact model.
- **Read-only:** Required methods are detected, but writes remain disabled because exact manufacturer/model/SKU/BIOS semantics have not been validated.
- **Unsupported:** Required WMI classes or methods are missing.

Do not infer compatibility from branding alone. AERO and AORUS generations may use different embedded controllers, WMI schemas, or value encodings.
