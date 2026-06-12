# Sherpa Bundle Specification

**Version:** 1.0 (draft)
**Status:** Draft
**File extension:** `.sherpabundle`
**Format:** JSON (UTF-8, no BOM)

`sherpacli` consumes a `.sherpabundle` file plus command-line overrides to drive
the **setup → build → deploy** pipeline for a .NET (typically .NET MAUI)
project across one or more target platforms.

---

## 1. CLI

```
sherpacli <bundle>.sherpabundle
          -environment:<name>
          [-platform:<list>]
          [-step:<list>]
          [-project:<path>]
          [-variable:"<name>=<value>" ...]
          [-replacetoken:"<name>=<value>" ...]
          [-msbuild:"<property>=<value>" ...]
```

| Flag | Repeatable | Required | Description |
|---|---|---|---|
| `<bundle>` (positional) | no | yes | Path **or reference URI** (see §6) to the `.sherpabundle` file. |
| `-environment:<name>` | no | yes | Name of the environment block to apply (e.g. `production`). Case-insensitive match against bundle keys. |
| `-platform:<list>` | no | no | Comma-separated list of platforms: `android`, `ios`, `macos`, `maccatalyst`, `windows`. Defaults to every platform defined in the selected environment. |
| `-step:<list>` | no | no | Comma-separated list of `setup`, `build`, `deploy`, or `all`. Defaults to `all`. Steps run in order: `setup` → `build` → `deploy`. |
| `-project:<path>` | no | no | Path to the target `.csproj`. If omitted, `sherpacli` infers a single `.csproj` from the working directory. |
| `-variable:"name=value"` | yes | no | Sets a variable resolvable via `${name}` inside any string in the bundle. |
| `-replacetoken:"name=value"` | yes | no | Overrides a `ReplaceTokens` entry. Highest precedence (see §5). |
| `-msbuild:"prop=value"` | yes | no | Overrides an `MSBuildProperties` entry. Highest precedence (see §5). |

**Parallelism (open question):** Initial implementation runs platforms and steps
sequentially. Parallel execution across platforms is a planned `-parallel` flag.

---

## 2. Bundle structure

```jsonc
{
  "Build":        { /* defaults applied to every environment */ },
  "<EnvName>":    { /* one or more named environments, e.g. "Production" */ }
}
```

Top-level keys:

- **`Build`** *(reserved, optional)* — baseline defaults. Merged into every
  environment as the lowest-precedence layer.
- **Any other key** — a named environment (`Production`, `Development`,
  `Staging`, etc.). Selected at runtime via `-environment:<name>`.

### 2.1 Environment block

```jsonc
{
  "Variables":         { /* default values for ${name} substitution */ },
  "ReplaceTokens":     { /* tokens applied to all platforms */ },
  "MSBuildProperties": { /* properties applied to all platforms */ },

  "Android":     { /* see §3.1 */ },
  "iOS":         { /* see §3.2 */ },
  "MacOS":       { /* see §3.3 */ },
  "MacCatalyst": { /* see §3.3 */ },
  "Windows":     { /* see §3.4 */ }
}
```

All fields are optional. An environment with no platform blocks is valid (it
contributes only environment-level defaults).

---

## 3. Platform blocks

### 3.1 Android

```jsonc
"Android": {
  "Setup": {
    "Keystores": [
      {
        "Content":       "<base64-encoded .jks/.keystore>",
        "StorePassword": "<optional>",
        "KeyAlias":      "<alias>",
        "KeyPassword":   "<password>"
      }
    ]
  },
  "Build": {
    "MSBuildProperties": {
      "ApplicationId":      "org.mycompany.myapp",
      "ApplicationName":    "My App",
      "ApplicationVersion": "1.0.${buildNumber}"
    },
    "ReplaceTokens": {
      "Hello":           "Android World ${buildNumber}",
      "FirebaseApiKey":  "..."
    }
  },
  "Deploy": [ /* see §4 */ ]
}
```

### 3.2 iOS

```jsonc
"iOS": {
  "Setup": {
    "Profiles":     [ { "Content": "<base64 .mobileprovision>" } ],
    "Certificates": [ { "Content": "<base64 .p12>", "Password": "..." } ]
  },
  "Build": {
    "MSBuildProperties": { /* ... */ },
    "ReplaceTokens":     { "Hello": "iOS World" }
  },
  "Deploy": [ /* see §4 */ ]
}
```

Profiles and certificates are arrays to support apps with multiple targets
(main app + widgets, share extensions, etc.).

### 3.3 MacOS / MacCatalyst

Flat layout — a single provisioning profile and certificate per platform.

```jsonc
"MacOS": {
  "ProvisioningProfile":  "<base64 .provisionprofile>",
  "Certificate":          "<base64 .p12>",
  "CertificatePassword":  "...",
  "Variables":            { "Hello": "MacOS World" }
}
```

`MacCatalyst` uses the same shape.

> Future versions may upgrade these to the Setup/Build/Deploy layout used by
> Android/iOS. For now, MSBuild property and replace-token overrides come from
> the environment-level `MSBuildProperties` / `ReplaceTokens`.

### 3.4 Windows

Flat layout — code-signing certificate only (no provisioning profile).

```jsonc
"Windows": {
  "Certificate":          "<base64 .pfx>",
  "CertificatePassword":  "...",
  "Variables":            { "Hello": "Windows World" }
}
```

---

## 4. Deploy targets

`Deploy` (Android/iOS) is an **array** so a single build can be shipped to
multiple destinations in one run.

```jsonc
"Deploy": [
  { "Provider": "TestFlight", "ApiKey": "<base64 .p8>" },
  { "Provider": "Firebase",   "ApiKey": "<firebase key>", "AppId": "..." }
]
```

Every entry **must** include `Provider`. Remaining fields are provider-specific.
Common providers:

| Provider | Platform(s) | Required fields |
|---|---|---|
| `TestFlight` | iOS | `ApiKey` (base64 App Store Connect `.p8`), `IssuerId`, `KeyId` |
| `Firebase` | iOS, Android | `ApiKey`, `AppId`, optional `TesterGroups` |
| `PlayStore` | Android | `ServiceAccountKey` (base64 JSON), `Track` (`internal`/`alpha`/`beta`/`production`) |
| `AmazonAppStore` | Android | `ClientId`, `ClientSecret` |
| `MicrosoftStore` | Windows | `TenantId`, `ClientId`, `ClientSecret` |

Unknown providers are accepted; `sherpacli` matches by name to a registered
provider plugin and validates required fields at run time.

---

## 5. Substitution & override model

### 5.1 Variables — `${name}`

Any string value in the bundle may reference variables using `${name}`.
Variable sources, highest precedence first:

1. CLI `-variable:"name=value"`
2. Environment-level `Variables` block
3. Top-level `Build.Variables` block

Referencing an undefined variable is a **hard error** — `sherpacli` aborts
before running any step.

### 5.2 ReplaceTokens

A `ReplaceTokens` map is a flat key/value dictionary the tool substitutes into
source/asset files during the `build` step (for example, `${SentryDsn}` in
`appsettings.json`).

Effective `ReplaceTokens` for a platform is the merge of these layers, with
later layers overriding earlier ones:

1. `Build.ReplaceTokens` (global defaults)
2. `<Env>.ReplaceTokens` (environment-level)
3. `<Env>.<Platform>.Build.ReplaceTokens` (platform-specific)
4. CLI `-replacetoken:"name=value"` (highest precedence)

Variable substitution (§5.1) is applied to token **values** before they are
written.

### 5.3 MSBuildProperties

Same merge order as ReplaceTokens, with CLI `-msbuild:` winning. Properties are
passed to MSBuild as `-p:Name=Value`.

### 5.4 Worked example

Given the bundle:

```jsonc
"Build":       { "ReplaceTokens": { "SentryDsn": "https://default" } },
"Production":  {
  "ReplaceTokens": { "Hello": "World", "SentryDsn": "https://prod" },
  "Android":   { "Build": { "ReplaceTokens": { "Hello": "Android World ${buildNumber}" } } }
}
```

And the CLI:

```
sherpacli app.sherpabundle -environment:production -platform:android \
  -variable:"buildNumber=1234" \
  -replacetoken:"Hello=Android World 2"
```

Effective Android ReplaceTokens:

```
SentryDsn = "https://prod"          # from Production
Hello     = "Android World 2"       # CLI override wins over "Android World 1234"
```

---

## 6. Bundle delivery & secret references

A fully-populated bundle embeds base64 signing material for every platform —
keystores, `.p12` certificates, provisioning profiles, API keys. In practice a
multi-platform bundle runs **80–300 KB** (the iOS/macOS provisioning profiles
dominate). That:

- exceeds per-item secret-store limits — **Azure Key Vault: 25 KB**,
  **GitHub Actions secret: 48 KB** (raw payload before base64 ≈ limit ÷ 1.37); and
- is usually something teams would rather **not commit to the repo**.

To address both, **any secret-bearing string** in the bundle MAY be a *reference
URI* that `sherpacli` resolves at run time instead of an inline value, and the
**bundle file itself** may be fetched from a reference rather than read from disk.

### 6.1 Reference syntax

A reference is a string of the form `<scheme>://<locator>`. Wherever the spec
calls for inline content — `Content`, `Certificate`, `ProvisioningProfile`,
`ApiKey`, `ServiceAccountKey`, any `*Password`, etc. — `sherpacli` checks whether
the value matches a known reference scheme. If it does, the reference is resolved
and its result substituted before any further processing. A plain (non-URI)
string is used verbatim, so fully-inlined bundles remain valid.

| Scheme | Example | Resolves to |
|---|---|---|
| `file://` | `file://./secrets/app.keystore` | Contents of a local file. |
| `env://` | `env://ANDROID_KEYSTORE_B64` | Value of an environment variable. |
| `secret://` | `secret://IOS_DIST_P12` | A named secret from the CI provider's secret store (GitHub Actions secret, Azure DevOps variable), surfaced to the process as an environment variable. |
| `keyvault://` | `keyvault://myvault/ios-dist-p12` | An Azure Key Vault secret value. |
| `az://` | `az://release-secrets/ios-dist.p12` | An Azure Blob Storage object. |
| `s3://` | `s3://release-secrets/ios-dist.p12` | An AWS S3 object. |
| `gs://` | `gs://release-secrets/ios-dist.p12` | A Google Cloud Storage object. |
| `https://` | `https://…` | An arbitrary HTTPS `GET`. |

### 6.2 Encoding

Fields that expect base64 (e.g. `Content`, `Certificate`, `ProvisioningProfile`)
are populated as follows:

- **Binary-resolving schemes** (`file://`, `az://`, `s3://`, `gs://`, `https://`)
  return raw bytes; `sherpacli` base64-encodes them automatically.
- **String-resolving schemes** (`env://`, `secret://`, `keyvault://`) return the
  stored string as-is, on the assumption it already holds the base64 (or literal
  secret) the field expects.

### 6.3 Resolution timing & credentials

- References are resolved **lazily** — only for the selected environment,
  platforms, and steps. A run targeting only Android never resolves iOS secrets.
- Cloud schemes (`keyvault`, `az`, `s3`, `gs`) authenticate via **ambient
  credentials**: OIDC / workload identity federation is preferred (no long-lived
  secret), falling back to the platform default credential chain (managed
  identity, environment credentials, `az login`, etc.). The bundle never carries
  inline cloud credentials.
- A reference that cannot be resolved is a **hard error** before any step runs
  (same failure model as an undefined `${variable}`, see §5.1).

### 6.4 Whole-bundle indirection

The positional `<bundle>` argument accepts a reference URI as well as a path:

```
sherpacli az://release-secrets/app.sherpabundle -environment:production
```

This is the recommended pattern when the bundle should not live in git: store it
in private object storage and fetch it via OIDC at build time — nothing sensitive
in the repo, nothing oversized in the secret store.

### 6.5 Recommended CI patterns

- **Externalized bundle** — keep the whole bundle in private object storage; CI
  authenticates via OIDC; pass `az://…/app.sherpabundle` (or `s3://`, `gs://`) as
  the bundle argument. No size limit, no long-lived secret, nothing committed.
- **Skeleton + references** *(recommended)* — commit a non-secret skeleton bundle
  whose `Content`/`Certificate`/`ApiKey` fields are `keyvault://`, `secret://`, or
  `az://` references. Each artifact is stored and rotated individually, the repo
  stays clean, and most artifacts sit comfortably under the 25 KB Key Vault
  ceiling. Fat provisioning profiles that approach 25 KB should use a blob
  (`az://`) reference rather than Key Vault.
- **Azure DevOps Secure Files** — the whole bundle (or individual artifacts) may
  be uploaded as an ADO Secure File and referenced via `file://` after a
  `DownloadSecureFile@1` step, with no per-item size concerns.

---

## 7. Build inference

If `-project:` is omitted, `sherpacli` infers the target project and toolchain:

1. Resolve `.csproj` — pick the single `.csproj` in CWD, or error if ambiguous.
2. Resolve the .NET SDK — honor `global.json` if present; otherwise install the
   latest SDK matching the project's `TargetFramework` / `TargetFrameworks`.
3. From the resolved TFM:
   - Android: install matching `Microsoft.Android.Sdk.*` workload.
   - iOS / MacCatalyst / MacOS: install matching `Microsoft.iOS.Sdk.*` /
     `Microsoft.MacCatalyst.Sdk.*` / `Microsoft.macOS.Sdk.*`, then resolve
     the required Xcode version.
   - Windows: install the matching Windows SDK / WindowsAppSDK workload.
4. Restore the matching **workload set** version.

---

## 8. Output

After a successful run, `sherpacli` emits a JSON result on stdout (and writes
`sherpa-output.json` next to the bundle):

```jsonc
{
  "Environment": "Production",
  "Platforms": {
    "Android": {
      "Version":   "1.0.1234",
      "Artifacts": { "Aab": "./bin/app-release.aab", "Apk": "./bin/app-release.apk" },
      "Deploys":   [ { "Provider": "PlayStore", "Status": "Succeeded", "Url": "..." } ]
    },
    "iOS": {
      "Version":   "1.0.1234",
      "Artifacts": { "Ipa": "./bin/app.ipa" },
      "Deploys":   [ { "Provider": "TestFlight", "Status": "Succeeded" } ]
    }
  }
}
```

These values are also exposed as environment variables for CI consumption:
`SHERPA_ANDROID_AAB`, `SHERPA_IOS_IPA`, `SHERPA_VERSION`, etc.

---

## 9. JSON Schema

A normative JSON Schema (draft 2020-12) ships alongside this spec as
`sherpa.schema.json`. Tooling can wire it up via:

```jsonc
// in the bundle file (optional)
"$schema": "https://schemas.sherpa.dev/sherpabundle/v1.json"
```

---

## 10. Implementation notes

> Captures decisions, interpretations, and known limitations from the first
> end-to-end implementation. Where the spec was silent, the choice made is
> recorded here so future work stays consistent. **§6 (Bundle delivery &
> references) is not yet implemented** — the current code consumes a raw-JSON
> bundle from a local path only.

### 10.1 Project layout

| Project | Role |
|---|---|
| `MauiSherpa.Bundle` | Pipeline engine — models, loader, substitution, steps, deploy providers. References `MauiSherpa.Core`. |
| `MauiSherpa.Bundle.Cli` | The `sherpacli` executable (`dotnet tool`, command name `sherpacli`). Thin front-end over the engine. |
| `MauiSherpa.Bundle.Tests` | xUnit coverage of the deterministic core (parsing, §5 merge/substitution, CLI parsing, token replacement). |

The deterministic core (bundle parsing, the §5 engine, CLI parsing) is unit
tested. Build/deploy shell out to real toolchains and are not covered by
automated tests.

### 10.2 Bundle format & the encryption seam

The on-disk format today is **raw JSON only**. Loading goes through
`IBundleLoader` → `SherpaBundleSerializer.Deserialize(ReadOnlySpan<byte>)`. The
planned **encrypted-binary** format is a drop-in: a new `IBundleLoader` decrypts
the bytes and defers to the same `Deserialize`; nothing else in the pipeline
changes. The serializer tolerates comments, trailing commas, a leading BOM, and
ignores a top-level `$schema` key.

### 10.3 Interpretations of under-specified behavior

- **Flat-platform `Variables` (MacOS/MacCatalyst/Windows)** are treated as
  **platform-scoped `ReplaceTokens`**, not as variable-resolution sources. This
  keeps §5.1 intact (variables come only from CLI / env / `Build`) while giving
  the flat platforms a per-platform token layer (e.g. `"Hello": "Windows World"`).
- **`ReplaceTokens` file scope (§5.2)** — the spec does not enumerate which files
  are scanned. The implementation rewrites **all text files under the project
  directory**, excluding `bin`, `obj`, `.git`, `.vs`, `.idea`, `node_modules`,
  `.github`, and known-binary extensions. A token `Foo` replaces the literal
  `${Foo}`; unknown `${...}` occurrences are left untouched. *Open question:
  consider a configurable include-glob (e.g. `appsettings*.json`, `Info.plist`)
  to narrow the blast radius.*
- **MSBuild property keys** merge case-insensitively (so CLI
  `-msbuild:"applicationversion=…"` overrides `ApplicationVersion`, per §5.4);
  **ReplaceTokens keys** are case-sensitive (token names are literal).
- **`Version` (§7)** is taken from the effective `ApplicationVersion`, falling
  back to `ApplicationDisplayVersion`.

### 10.4 Setup, build & inference status

- **Setup** materializes signing assets to a temp **scratch directory** and
  records the MSBuild signing properties the build consumes:
  Android (`AndroidSigningKeyStore`/`…KeyAlias`/`…StorePass`/`…KeyPass`),
  Windows (`PackageCertificateKeyFile`/`…Password`). Apple profiles are copied to
  `~/Library/MobileDevice/Provisioning Profiles/<UUID>.<ext>` and certificates are
  `security import`-ed into the login keychain (macOS only — skipped with a
  warning elsewhere). Multiple Android keystores: only the **first** is used for
  signing.
- The scratch directory is **deleted after the run** (it holds decoded
  keystores/certs). Set `SHERPA_KEEP_SCRATCH` to retain it for debugging.
- **Build** runs `dotnet publish -f <tfm> -c Release` with a platform-default
  `RuntimeIdentifier` (overridable via `-msbuild:RuntimeIdentifier=…`) plus all
  merged `-p:` properties, then collects artifacts from `bin/` by platform glob.
- **Inference (§6)** honors `global.json` implicitly (the SDK resolves it) and
  runs `dotnet workload restore`; a failure is a warning, not fatal. Xcode is
  probed and logged for Apple targets but a **specific required Xcode version is
  not yet resolved/selected**.

### 10.5 Deploy provider status

Providers are matched by `Provider` name (case-insensitive) via a registry
(§4). Current behavior:

| Provider | Mechanism | Status |
|---|---|---|
| `TestFlight` | `xcrun altool --upload-app` (.p8 staged to `~/.appstoreconnect/private_keys`) | Full upload; macOS only |
| `Firebase` | `firebase appdistribution:distribute` (CLI required on PATH) | Full distribution |
| `PlayStore` | `fastlane supply` (fastlane required on PATH) | Full upload; `PackageName` from `ApplicationId` |
| `AmazonAppStore` | Login-with-Amazon client-credentials token | **Auth only** → returns `Skipped`; Submission-API edit/commit not auto-committed |
| `MicrosoftStore` | Azure AD client-credentials token (Partner Center) | **Auth only** → returns `Skipped`; submission upload/commit not auto-committed |

A deploy result is one of `Succeeded` / `Skipped` / `Failed`. A missing CLI tool
yields `Skipped` (with install guidance); a genuine failure yields `Failed` and
the process exits non-zero. Unknown/unsupported providers yield `Failed`.

### 10.6 Output & CI integration (§7)

- The JSON result is written to **stdout** (PascalCase keys) and to
  `sherpa-output.json` beside the bundle. Human-readable progress goes to
  **stderr**, so `--json` / piping stdout stays clean.
- CI variables emitted: `SHERPA_VERSION` and `SHERPA_<PLATFORM>_<KIND>` (e.g.
  `SHERPA_ANDROID_AAB`, `SHERPA_IOS_IPA`). These are appended to `$GITHUB_ENV`
  and `$GITHUB_OUTPUT` when present, and emitted as
  `##vso[task.setvariable …]` when `TF_BUILD` is set.

### 10.7 Known limitations

- **Step splitting across invocations.** The supported path is a single
  invocation running `setup → build → deploy` (the default). Android-keystore and
  Windows-`.pfx` signing properties are process-local and written to the
  ephemeral scratch dir, so a standalone `-step:setup` invocation does **not**
  carry them to a later `-step:build` invocation. (Apple keychain/profile installs
  *do* persist system-wide.) Cross-invocation persistence would need a stable,
  opt-in scratch location.
- **`-parallel` is not implemented** — platforms and steps run sequentially
  (see §1).
- **§6 references and whole-bundle indirection are not implemented.**
