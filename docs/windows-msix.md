# Windows MSIX release setup

MAUI Sherpa can publish signed, self-contained x64 and arm64 MSIX packages while
retaining the Inno Setup release path as a rollback option.

## Required GitHub configuration

Create a protected `windows-release` environment with required reviewers. Add
the following repository or environment variables:

- `WINDOWS_MSIX_ENABLED`: set to `true` only after the signing and installation
  validation has completed.
- `WINDOWS_MSIX_PUBLISHER`: the exact subject of the Azure Artifact Signing
  certificate.
- `WINDOWS_MSIX_PACKAGE_FAMILY_NAME`: the family name reported by a validated,
  signed installation, for example `codes.redth.mauisherpa_abcdefghijklm`.
- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`
- `AZURE_ARTIFACT_SIGNING_ENDPOINT`
- `AZURE_ARTIFACT_SIGNING_ACCOUNT`
- `AZURE_ARTIFACT_SIGNING_CERTIFICATE_PROFILE`

The Azure federated credential must permit the GitHub OIDC subject:

```text
repo:Redth/MAUI.Sherpa:environment:windows-release
```

Keep the existing `SENTRY_DSN` and `WINGET_FORK_DEPLOY_KEY` secrets available to
the reusable workflows.

## Release flow

1. The normal release workflow publishes the existing Windows ZIP and Inno
   installers.
2. When `WINDOWS_MSIX_ENABLED` is `true`, `windows-msix.yml` rebuilds the
   validated tag as self-contained MSIX packages.
3. Azure Artifact Signing signs both architectures and the workflow verifies
   the signature, timestamp, publisher, and package identity.
4. `Publish-AppInstallerChannel.ps1` uploads immutable MSIX and App Installer
   assets to the versioned release, then promotes the two App Installer files
   on the rolling `windows-latest` release.
5. WinGet waits for MSIX publication and generates an MSIX manifest. When the
   feature flag is disabled, it continues generating the Inno manifest.

The MSIX WinGet manifest uses `UpgradeBehavior: uninstallPrevious`, so WinGet
removes the prior Inno installation before installing the MSIX package.

Stable `0.x` versions are valid MSIX versions and are supported by this flow.

## Migration behavior

The first packaged launch copies unpackaged MAUI `SecureStorage` and known
preference values into the package's `ApplicationData.LocalSettings`. Existing
packaged values are never overwritten, and the unpackaged files remain in
place so users can roll back to the Inno installation.

The shared application data under `%APPDATA%\MauiSherpa` is unchanged. Direct
MSIX installations can coexist with Inno during validation. WinGet upgrades
remove the previous Inno installation after preserving its user data for the
first packaged launch migration.

Embedded App Installer background updates require Windows build 21300 or newer.
Older supported Windows versions can still open the bundled App Installer file
manually, and WinGet installations continue to update through WinGet.
