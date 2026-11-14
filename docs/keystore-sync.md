# Keystore Cloud Sync

MAUI Sherpa can sync your Android signing keystores to a cloud secrets provider, making them available across machines. This document explains how the feature works, what data is stored where, and what to keep in mind when using it.

## How It Works

When you sync a keystore to the cloud, three pieces of data are uploaded as separate secrets:

| Secret | Contents |
|--------|----------|
| `KEYSTORE_{alias}_JKS` | The keystore file (binary) |
| `KEYSTORE_{alias}_PWD` | The keystore password (plaintext UTF-8) |
| `KEYSTORE_{alias}_META` | JSON metadata (alias, type, creation date, notes) |

When you download a keystore from the cloud, the file is saved to your local keystores directory and the password is stored in your platform's secure storage (macOS Keychain / Windows DPAPI).

## Supported Providers

| Provider | Authentication | Encryption Model |
|----------|---------------|-----------------|
| **Azure Key Vault** | Service Principal (Tenant ID, Client ID, Client Secret) | Azure-managed (server-side) |
| **AWS Secrets Manager** | Access Key ID + Secret Access Key | AWS KMS (server-side) |
| **Google Secret Manager** | Service Account JSON credentials | Google-managed (server-side) |
| **Azure DevOps** | Personal Access Token | Azure-managed (server-side) |
| **Infisical** | Machine Identity (Client ID + Client Secret) | Server-side |
| **1Password** | Desktop app auth or Service Account Token | End-to-end encrypted (1Password) |
| **Vaultwarden / Bitwarden** | Email + Master Password + API Key | Client-side encryption (AES-256, PBKDF2 key derivation) |

All providers communicate over HTTPS/TLS.

## Local Storage

| Data | Location |
|------|----------|
| Keystore files | `~/Library/Application Support/MauiSherpa/keystores/` (macOS) |
| Keystore metadata | `~/Library/Application Support/MauiSherpa/android-keystores.json` |
| Keystore passwords | macOS Keychain (Release) / fallback file (Debug builds only) |
| Cloud provider credentials | macOS Keychain (Release) / fallback file (Debug builds only) |

On Windows, the app data directory is `%APPDATA%\MauiSherpa\` and passwords use Windows DPAPI.

## Things to Know

- **Your keystore password is uploaded to the cloud provider.** This is necessary for the sync feature to work — when you download a keystore on another machine, the password needs to come with it. The security of that password depends entirely on your cloud provider.

- **Server-side vs client-side encryption matters.** With Azure Key Vault, AWS, Google, Azure DevOps, and Infisical, the provider manages encryption — meaning the provider (and anyone with access to your account) can read your secrets. With 1Password and Vaultwarden/Bitwarden, encryption happens on your machine before data leaves it.

- **Use dedicated credentials with minimal permissions.** For cloud providers that support IAM or RBAC (Azure, AWS, Google), create a service principal or service account scoped to only the secrets your keystores need. Don't reuse broad admin credentials.

- **Keystore files on disk are not separately encrypted by MAUI Sherpa.** They rely on the keystore format's own encryption (PKCS12 or JKS) plus OS file permissions. The keystore password is what protects the key material inside.
