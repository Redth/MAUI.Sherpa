# Secrets storage architecture

MAUI Sherpa stores credentials, signing material, provider configuration, and app-managed secrets. The target architecture is a single SQLCipher-backed local vault for app-owned sensitive data, with OS secure storage used only for the SQLCipher root key.

## Current storage audit

| Area | Code surface | Sensitive data | Current / target storage |
| --- | --- | --- | --- |
| SQLCipher root key | `ILocalVaultKeyStore`, `LocalSecretsKeyStore` | Local vault encryption key | OS secure storage entry `MAUI Sherpa Local Vault`. This is the only long-term OS secure-storage dependency for migrated app-owned data. |
| Central local vault | `ILocalVaultStore`, `SqlCipherLocalVaultStore` | Generic app-owned secrets, metadata, settings, and provider data | Shiny DocumentDB over SQLCipher in `local-vault.db`. Records use a generic scope/path/key envelope. |
| Local secrets provider | `LocalSqlCipherSecretsProvider` | Managed secrets, local copies of synced certs/keystores, publish profile payloads | Built-in default provider and logical provider view over the central local vault under `local-provider-secret`. It is always present in Settings so users can switch back to local-only storage while leaving cloud providers configured. Existing flat keys are preserved in metadata for compatibility. |
| Legacy Local provider DB | first-pass `local-secrets.db` | Local-provider secret values | Lazily migrated into the central vault on Local provider use, then deleted after successful verification/write. |
| Secure-storage compatibility | `VaultSecureStorageService` | Existing app-owned key/value secrets | Reads/writes the central vault under `secure`. On first read, legacy OS/fallback secure-storage values are copied into the vault and removed from the old store. |
| Encrypted settings | `EncryptedSettingsService` | Settings snapshots that may include sensitive config | Vault-backed settings document under `settings`. Existing `settings.enc`, `.bak`, `.unreadable`, and `MauiSherpa_MasterKey` are removed after successful migration. |
| Apple identities | `AppleIdentityService` | P8 private key content | Secret values now flow through vault-backed `ISecureStorageService`; metadata remains in `apple-identities.json` until the identity metadata adapter is migrated. |
| Google identities | `GoogleIdentityService` | Service account JSON/private key | Secret values now flow through vault-backed `ISecureStorageService`; metadata remains in `google-identities.json` until the identity metadata adapter is migrated. |
| Apple Developer download auth | `AppleDownloadAuthService` | Session cookies and Apple ID | Values now flow through vault-backed `ISecureStorageService`. Password is not persisted. |
| Firebase push credentials | `FirebasePushService` | FCM service account JSON/private key | Value now flows through vault-backed `ISecureStorageService`. |
| Android keystores | `KeystoreService`, `KeystoreSyncService` | Keystore password and keystore file | Passwords now flow through vault-backed `ISecureStorageService`; metadata remains in `android-keystores.json` until the keystore metadata adapter is migrated. User-selected keystore files remain at their file paths. |
| Secrets provider config | `CloudSecretsService`, `CloudSecretsProviderFactory` | Client secrets, tokens, service account JSON, vault passwords | Provider metadata and non-secret settings are vault-backed under `cloud-provider`. Secret settings use vault-backed `ISecureStorageService`. Legacy JSON files are removed after migration. |
| Managed remote secrets | `ManagedSecretsService`, `CertificateSyncService`, `KeystoreSyncService`, `PublishProfileService` | User-managed bytes, P12 payloads/passwords, JKS payloads/passwords, publish profiles | Active `ICloudSecretsService` provider. Remote providers keep remote data; Local provider stores in the central vault. |
| Publisher config | `SecretsPublisherService` | GitHub/Gitea/GitLab/Azure DevOps PATs and publisher settings | Existing `secrets_publishers` blob now flows through vault-backed `ISecureStorageService`; a typed publisher metadata adapter can split it later. |
| Push projects | `PushProjectService` | Device tokens, APNs config paths, payloads, send history | Legacy `push-projects.json`; planned vault adapter because operational data can be sensitive. |
| Platform certificate/private-key stores | `LocalCertificateService`, platform APIs | Installed signing identities | Remain in macOS Keychain, Windows certificate store, or Linux user X509 store. The vault stores Sherpa metadata and Local-provider sync copies, not installed platform private-key material. |
| Non-sensitive caches/artifacts | logs, profiling artifacts, Apple root cert cache, downloaded tools, update temp files, Copilot caches, window size/basic UI prefs | Non-secret operational data | Outside the vault unless a future feature makes the data secret-bearing. |

## Local vault model

The central vault is intentionally migration-light. It uses one generic document envelope rather than feature-specific SQL tables:

```csharp
public sealed class LocalVaultItem
{
    public string Id { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Path { get; set; } = "/";
    public string Key { get; set; } = "";
    public string ContentType { get; set; } = "";
    public byte[] Value { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

`Id` is a deterministic SHA-256 hash of normalized `scope + path + key`, so callers can upsert and retrieve records without a separate index table. `Scope` separates feature domains such as `settings`, `secure`, `cloud-provider`, `local-provider-secret`, and `migration`. `Path` and `Key` provide hierarchy while allowing values to remain JSON, text, or binary.

```mermaid
flowchart LR
    Services[Feature services] --> Vault[ILocalVaultStore]
    SecureFacade[VaultSecureStorageService] --> Vault
    Settings[EncryptedSettingsService] --> Vault
    LocalProvider[Local ICloudSecretsProvider] --> Vault
    Vault --> SqlCipher[Shiny DocumentDB SQLCipher local-vault.db]
    Vault --> KeyStore[ILocalVaultKeyStore]
    KeyStore --> OS[OS secure storage: MAUI Sherpa Local Vault]
```

## Canonical secret paths

New path-aware code should use:

```csharp
public readonly record struct SecretPath(string FolderPath, string Key);
```

Normalization rules:

- Empty or null folder paths normalize to `/`.
- `/` is the logical separator on every platform.
- Folder paths are normalized to leading slash, no trailing slash, and no duplicate separators.
- `.` and `..` path segments are rejected.
- Secret leaf keys cannot be empty or contain path separators.
- Existing flat provider APIs remain supported by encoding a `SecretPath` as `folder/key` and decoding flat keys on the last `/`.

Managed-secret folders are first-class metadata records so the UI can create and select empty folders before any secret exists in them. `ManagedSecretsService` persists those records under `sherpa-secret-folders/` while continuing to infer folders from legacy slash-delimited secret keys for backward compatibility. It also writes a hidden `sherpa-folder-marker` placeholder under the folder's own secret path so providers that only materialize folders when a child secret exists can still represent empty folders. Creating a secret no longer asks the user to type a folder path; the user creates folders with an explicit gesture and chooses an existing folder when adding the secret. Editing a secret can move it to another folder by writing the value and metadata under the new key, then deleting the old key after the new write succeeds. Existing folders can be renamed, which moves managed secrets, child folders, and folder markers under the new path, or deleted when empty.

Recommended mappings for Sherpa-managed keys:

| Legacy key pattern | Canonical path |
| --- | --- |
| `sherpa-secrets/api-key` | path `/managed`, key `api-key` |
| `sherpa-secrets-meta/api-key` | metadata on `/managed/api-key` or companion metadata record |
| `CERT_{serial}_P12` | path `/certificates/{serial}`, key `p12` |
| `CERT_{serial}_PWD` | path `/certificates/{serial}`, key `password` |
| `KEYSTORE_{alias}_JKS` | path `/keystores/{alias}`, key `jks` |
| `KEYSTORE_{alias}_PWD` | path `/keystores/{alias}`, key `password` |
| `sherpa-publish-profiles/{id}` | path `/publish-profiles`, key `{id}` |

Remote provider migrations should rewrite Sherpa-managed legacy keys into canonical paths only after the local vault migration has completed and the provider can verify all rewritten values.

## Compatibility adapters

`VaultSecureStorageService` keeps the existing `ISecureStorageService` contract while changing its persistence boundary. Consumers such as Apple identities, Google identities, Firebase push, publisher config, and keystore passwords do not need immediate rewrites; their values are stored as vault records in the `secure` scope. Legacy secure-storage values are migrated on first read and deleted from the legacy store after the vault write succeeds.

`EncryptedSettingsService` is also a compatibility facade. It reads the vault first, migrates an existing `settings.enc` file when needed, and writes new settings directly to the `settings` scope. After a successful migration, it deletes `settings.enc`, `settings.enc.bak`, `settings.enc.unreadable`, and the old `MauiSherpa_MasterKey`.

`CloudSecretsService` stores provider metadata and non-secret provider settings in the `cloud-provider` scope. If legacy `cloud-secrets-providers.json` or `cloud-secrets-{id}.json` files exist, they are imported into the vault and deleted after successful writes. Provider secret settings continue to use `ISecureStorageService`, which now maps them to the vault.

## Export/import impact

The existing `BackupService` continues to export password-protected settings snapshots, but those snapshots now hydrate through services backed by the local vault:

- Settings are read from the `settings` vault scope.
- Provider metadata and non-secret provider settings are read from the `cloud-provider` vault scope through `CloudSecretsService`.
- Provider secret settings, publisher config, identity private keys, Firebase credentials, and keystore passwords are read through vault-backed `ISecureStorageService`.
- Local-provider secrets remain available through `ICloudSecretsService` and can be included by feature-specific export flows without opening a separate database.

A future raw vault package can export selected `scope/path/key` records directly for advanced backup or migration scenarios, but the user-facing backup path does not need a schema-specific database migration to pick up the vault-backed adapters added here.

## Migration and cleanup

Migrations must be explicit, idempotent, and cleanup-aware:

1. Open or create `local-vault.db` with the OS secure-storage root key.
2. Detect a legacy source and acquire a migration lock for that step.
3. Write migrated records into the vault and re-read representative records.
4. Record a migration marker under the `migration` scope.
5. Delete migrated secret-bearing legacy storage only after successful vault writes.
6. Leave legacy storage intact on failure so the migration can be retried.

The implemented Local-provider bridge migrates `local-secrets.db` into `local-vault.db`, preserves each original flat key in metadata, and deletes the old SQLite file and sidecars after successful migration. The secure-storage, encrypted-settings, and secrets-provider configuration adapters follow the same cleanup rule for the data they migrate.

## Guidance for new code

- Store app-owned secret-bearing data through `ILocalVaultStore` or higher-level services backed by it.
- Use `ICloudSecretsService` or `IManagedSecretsService` for values that may sync to Local or remote providers.
- Do not add new feature-specific encrypted files or secure-storage keys for app-owned data.
- Keep OS secure storage limited to root encryption keys or platform tokens that cannot safely move yet.
- Keep installed certificates/private keys in platform stores; put only Sherpa metadata, references, and Local-provider sync copies in the vault.