# Windows code signing

## Current status

AeroControl release executables are currently unsigned. `Get-AuthenticodeSignature` therefore reports `NotSigned`, Windows UAC identifies the elevated process as an unknown publisher, and Microsoft Defender SmartScreen can show a reputation warning that requires **More info** before **Run anyway** appears.

These are related but distinct checks:

- **Authenticode publisher identity:** a trusted code-signing certificate lets Windows display the verified publisher instead of `Unknown publisher`.
- **SmartScreen reputation:** Microsoft checks downloaded files, URLs, apps, and signing certificates. A valid signature is important but does not guarantee that a brand-new binary immediately has established reputation.

Do not use a self-signed certificate for public releases. Other computers do not trust it, so it does not establish a public publisher identity. Do not tell users to disable Defender. Until releases are signed, publish a SHA-256 digest and direct users to the GitHub release page.

Microsoft documentation:

- [Microsoft Defender SmartScreen overview](https://learn.microsoft.com/windows/security/operating-system-security/virus-and-threat-protection/microsoft-defender-smartscreen/)
- [Smart App Control code-signing guidance](https://learn.microsoft.com/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control)

## Recommended path: Microsoft Artifact Signing

[Artifact Signing](https://learn.microsoft.com/azure/artifact-signing/overview) is Microsoft's managed signing service. It supports Public Trust profiles, keeps certificate keys in managed HSMs, handles certificate lifecycle, and signs file digests without uploading the application binary.

As of July 2026, the Basic SKU is listed at approximately **US $9.99/month**, including 5,000 signatures per month. Pricing varies by agreement and region; check the [official pricing page](https://azure.microsoft.com/pricing/details/artifact-signing/) before enabling it.

Operator-owned prerequisites:

1. An Azure subscription with billing enabled.
2. Artifact Signing identity validation for the publisher.
3. An Artifact Signing account and **Public Trust** certificate profile.
4. An Entra application or workload identity with the **Artifact Signing Certificate Profile Signer** role.
5. A GitHub OIDC federated credential scoped to this repository and release environment.

These steps require legal identity and billing decisions and cannot be completed by repository automation alone.

## Intended GitHub release flow

After the operator completes the prerequisites:

1. Publish the framework-dependent `win-x64` executable.
2. Authenticate from GitHub Actions using OIDC (`id-token: write`) and `azure/login`.
3. Sign `AeroControl.exe` with [`azure/artifact-signing-action@v2`](https://github.com/Azure/artifact-signing-action).
4. Use RSA/SHA-256 and the Artifact Signing RFC 3161 timestamp service. Artifact Signing certificates are short-lived, so timestamping is required for long-term signature validity.
5. Fail the workflow unless `Get-AuthenticodeSignature` reports `Valid` and the expected publisher subject.
6. Run the signed executable smoke captures.
7. Create the ZIP only after signing, then publish its SHA-256 and upload it to the GitHub release.

The action requires a Windows runner. Microsoft recommends OIDC instead of a long-lived client secret.

## Alternatives

### Microsoft Store

The Microsoft Store provides a centralized, certified distribution route for Win32 and MSIX apps. Microsoft currently advertises free registration for individual developers in supported markets. Store packaging and certification are additional work, but this route can improve installation, updates, and user trust.

See [Publish apps and games to Microsoft Store](https://learn.microsoft.com/windows/apps/publish/).

### Public code-signing certificate

A public RSA code-signing certificate from a trusted certificate authority can be used with Windows `SignTool`. The certificate and private key must be protected according to current CA and industry requirements, and every release must be timestamped. Costs and hardware/cloud-key requirements vary by provider.

## Verification commands

```powershell
$expectedPublisher = '<approved publisher subject>'
$signature = Get-AuthenticodeSignature .\AeroControl.exe
if ($signature.Status -ne 'Valid') {
    throw "Invalid Authenticode signature: $($signature.StatusMessage)"
}
if ($signature.SignerCertificate.Subject -ne $expectedPublisher) {
    throw "Unexpected publisher: $($signature.SignerCertificate.Subject)"
}
if (-not $signature.TimeStamperCertificate) {
    throw 'The release signature is not timestamped.'
}

Get-FileHash .\AeroControl.exe -Algorithm SHA256
```

A signed release is acceptable only when the signature status is `Valid`, the signer matches the approved publisher identity, the timestamp is present, and CI has tested the exact signed bytes that are packaged for release.
