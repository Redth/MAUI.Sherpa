# Expedition Packs

Expedition Packs are password-protected release kits that carry the tools, signing gear, build route, and deployment credentials needed to reproduce a MAUI release in CI. Prepare them from **Secrets > Expedition Packs** in MAUI Sherpa and execute them with the `maui-sherpa` .NET tool.

## Security model

- `.sherpapack` files use PBKDF2-SHA256 with a random salt and AES-256-GCM authenticated encryption.
- Version 2 packs use compact JSON, Brotli compression before encryption, and a fixed authenticated binary envelope. Version 1 packs remain readable.
- Keep the encrypted pack and its password in separate systems. The password defaults to the masked `SHERPA_PACK_PASSWORD` environment variable (with `SHERPA_BUNDLE_PASSWORD` retained for compatibility) or can be read from standard input with `--password-stdin`.
- Decrypted assets exist only in a permission-restricted temporary workspace. Sherpa deletes that workspace after each platform run.
- Secret values are registered with the process runner and redacted from captured output. Variables are passed through the child-process environment instead of command-line MSBuild properties.
- Build artifacts are carried out of the temporary workspace to `artifacts/expedition-packs/<environment>/<platform>/` unless `--output` is supplied.

The password cannot protect a pack after an untrusted CI job decrypts it. Use trusted runners, restrict log/artifact access, pin third-party actions, and rotate credentials if a runner is compromised.

## Authoring model

A saved Expedition Pack selects an existing Sherpa publish profile and adds environments, platforms, build settings, variables, token replacements, toolchain requirements, and deployment targets. Export resolves the profile's certificates, provisioning profiles, keystores, Apple API keys, Google service accounts, and managed secrets into a self-contained encrypted payload.

The decrypted version 1 payload conceptually has this shape:

```json
{
  "version": 1,
  "name": "Contoso Mobile",
  "variables": {
    "ApplicationId": "com.contoso.mobile",
    "ApplicationDisplayVersion": "1.4.${BuildNumber}"
  },
  "secretVariables": [
    "ANDROID_RELEASE_KEYSTORE_PASSWORD"
  ],
  "toolchain": {
    "dotnetSdkVersion": "10.0.400",
    "workloadSetVersion": "10.0.400",
    "workloads": ["maui"],
    "androidSdkPackages": ["platforms;android-36", "build-tools;36.0.0"],
    "xcodeVersion": "26.2"
  },
  "environments": {
    "production": {
      "variables": {
        "ApiBaseUrl": "https://api.example.com"
      },
      "platforms": {
        "android": {
          "build": {
            "project": "src/App/App.csproj",
            "configuration": "Release",
            "targetFramework": "net10.0-android",
            "replacements": [{ "path": "src/App/Constants.cs" }]
          },
          "deploy": [
            {
              "provider": "googlePlay",
              "settings": {
                "packageName": "com.contoso.mobile",
                "track": "internal",
                "serviceAccountJsonPath": "${GOOGLE_RELEASE_SERVICE_ACCOUNT_JSON}"
              }
            }
          ]
        }
      }
    }
  }
}
```

The actual file is opaque encrypted binary data and should not be hand-edited.

## Variables and replacements

Variables merge from least to most specific:

1. Expedition Pack globals
2. Selected environment
3. Selected platform
4. Selected phase
5. Repeated CLI `--variable NAME=VALUE` overrides

Both `${Name}` and `{{ Name }}` token forms are supported. References can compose other variables; missing references and cycles fail validation. Replacement paths must be relative files inside the staged source tree.

Build variables become child-process environment variables. MSBuild imports valid environment names as initial properties, so project files can use `$(ApplicationId)`, `$(ApplicationVersion)`, and similar values without placing secrets on the process command line.

## CLI

Install the tool and inspect an Expedition Pack:

```bash
dotnet tool install --global MauiSherpa.Cli
export SHERPA_PACK_PASSWORD='use-a-masked-ci-secret'
maui-sherpa pack validate ./ci/app.sherpapack
```

Run the complete workflow:

```bash
maui-sherpa pack run ./ci/app.sherpapack \
  --environment production \
  --platform android \
  --variable BuildNumber=1842 \
  --json
```

Run phases independently:

```bash
maui-sherpa pack install ./ci/app.sherpapack -e production -p ios
maui-sherpa pack build ./ci/app.sherpapack -e production -p ios
maui-sherpa pack deploy ./ci/app.sherpapack -e production -p ios --artifact ./artifacts/MyApp.ipa
```

Convert an encrypted pack into GitHub secret-sized values:

```bash
maui-sherpa pack split ./ci/app.sherpapack --output ./pack-secrets
gh secret set SHERPA_PACK_1 < ./pack-secrets/SHERPA_PACK_1.txt
gh secret set SHERPA_PACK_2 < ./pack-secrets/SHERPA_PACK_2.txt
```

Small packs produce a single `SHERPA_PACK` value. Larger packs produce 1-based
`SHERPA_PACK_1`, `SHERPA_PACK_2`, and so on. Every numbered part records its index,
total part count, and a digest of the complete encoded pack. The CLI requires every
declared part and rejects missing, reordered, extra, or corrupted chunks rather than
silently reading until the first empty value.

Common options:

| Option | Purpose |
| --- | --- |
| `--environment`, `-e` | Required environment name |
| `--platform`, `-p` | Repeatable platform selection |
| `--phase` | Repeatable phase selection for `run` |
| `--project` | Project path relative to `--source` |
| `--source` | Source tree to stage; defaults to the current directory |
| `--output` | Persistent artifact directory |
| `--artifact` | Existing artifact for deploy-only runs |
| `--variable NAME=VALUE` | Repeatable highest-precedence variable |
| `--dry-run` | Validate the plan without invoking tools or uploads |
| `--parallel` | Run selected platforms in isolated workspaces concurrently |
| `--json` | Stable machine-readable result |
| `--from-env` | Read `SHERPA_PACK` or validated numbered chunks instead of a file |
| `--pack-env-prefix` | Override the `SHERPA_PACK` prefix |

Apple builds require macOS and Windows packaging requires Windows. Unsupported host/platform combinations fail before build execution.

## Deployment providers

| Provider | Platform/artifact | Required settings |
| --- | --- | --- |
| TestFlight | iOS `.ipa`, macOS `.pkg` | `apiKey`, `apiIssuer`, `AppleApiKeyPath` |
| Google Play | Android `.aab` or `.apk` | `packageName`, `track`, `serviceAccountJsonPath`; optional `executable` defaults to `fastlane` |
| Firebase App Distribution | Android `.aab` or `.apk` | `appId` and at least one of `groups` or `testers`; optional `releaseNotes` and `executable` |
| Amazon Appstore | Android `.apk` | `clientId`, `clientSecret`, `applicationId`; uses Amazon's official App Submission API |

Deployment transports are isolated behind providers. Validate that the corresponding vendor CLI is installed on the runner, or declare it in runner setup.

## GitHub Actions

Commit the encrypted Expedition Pack or download it from protected artifact storage. Store only its password in GitHub Actions secrets.

```yaml
jobs:
  android-release:
    runs-on: macos-26
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - run: dotnet tool install --global MauiSherpa.Cli
      - name: Build and deploy
        env:
          SHERPA_PACK_PASSWORD: ${{ secrets.SHERPA_PACK_PASSWORD }}
        run: |
          maui-sherpa pack run ci/app.sherpapack \
            --environment production \
            --platform android \
            --variable BuildNumber=${{ github.run_number }} \
            --json
      - uses: actions/upload-artifact@v4
        with:
          name: android-release
          path: artifacts/expedition-packs/production/android/
```

GitHub limits each Actions secret to 48 KB. Keeping the encrypted `.sherpapack` in
the repository and only its password in a secret is the recommended path and matches
[GitHub's documented large-secret pattern](https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/use-secrets#storing-large-secrets).
If repository policy requires the pack
itself to be stored as secrets, map every generated part explicitly—GitHub does not
automatically expose or dynamically enumerate repository secrets:

```yaml
env:
  SHERPA_PACK_1: ${{ secrets.SHERPA_PACK_1 }}
  SHERPA_PACK_2: ${{ secrets.SHERPA_PACK_2 }}
  SHERPA_PACK_PASSWORD: ${{ secrets.SHERPA_PACK_PASSWORD }}
run: |
  maui-sherpa pack run --from-env \
    --environment production \
    --platform android
```

## Azure DevOps

Store `SHERPA_PACK_PASSWORD` as a secret pipeline variable.

```yaml
steps:
  - checkout: self
  - task: UseDotNet@2
    inputs:
      useGlobalJson: true
  - script: dotnet tool install --global MauiSherpa.Cli
    displayName: Install MAUI Sherpa CLI
  - script: |
      maui-sherpa pack run ci/app.sherpapack \
        --environment production \
        --platform android \
        --variable BuildNumber=$(Build.BuildId) \
        --json
    displayName: Build and deploy
    env:
      SHERPA_PACK_PASSWORD: $(SHERPA_PACK_PASSWORD)
  - publish: artifacts/expedition-packs/production/android
    artifact: android-release
```
