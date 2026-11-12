using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Android keystores live in the Local Vault secrets provider: the keystore file, its keystore
/// and key passwords and its metadata, laid out as <see cref="KeystoreVaultLayout"/> describes.
/// Older versions registered a path to a file on disk in <c>android-keystores.json</c> and kept
/// passwords in secure storage; those keystores are moved into the vault the first time their
/// file is found.
/// </summary>
public class KeystoreService : IKeystoreService
{
    private const string KeystoresFileName = "android-keystores.json";
    private const string LegacyPasswordKeyPrefix = "android_keystore_pwd_";
    private const string LegacyKeyPasswordKeyPrefix = "android_keystore_keypwd_";

    private readonly IOpenJdkSettingsService _jdkSettings;
    private readonly ISecureStorageService _secureStorage;
    private readonly ILoggingService _logger;
    private readonly ISecretsProviderRegistry? _providerRegistry;
    private readonly ICloudSecretsService? _cloudSecrets;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Where older versions registered keystore file paths.</summary>
    internal string LegacyRegistryPath { get; init; } =
        Path.Combine(AppDataPath.GetAppDataDirectory(), KeystoresFileName);

    public KeystoreService(
        IOpenJdkSettingsService jdkSettings,
        ISecureStorageService secureStorage,
        ILoggingService logger,
        ISecretsProviderRegistry? providerRegistry = null,
        ICloudSecretsService? cloudSecrets = null)
    {
        _jdkSettings = jdkSettings;
        _secureStorage = secureStorage;
        _logger = logger;
        _providerRegistry = providerRegistry;
        _cloudSecrets = cloudSecrets;
    }

    public async Task<string?> GetKeytoolPathAsync()
    {
        var jdkPath = await _jdkSettings.GetEffectiveJdkPathAsync();
        if (string.IsNullOrEmpty(jdkPath))
            return null;

        var keytool = Path.Combine(jdkPath, "bin",
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "keytool.exe" : "keytool");

        return File.Exists(keytool) ? keytool : null;
    }

    public async Task<AndroidKeystore> CreateKeystoreAsync(
        string alias,
        string keyPassword,
        string keystorePassword,
        string cn, string ou, string o, string l, string st, string c,
        int validityDays = 10000,
        string keyAlg = "RSA",
        int keySize = 2048,
        string keystoreType = "PKCS12")
    {
        var keytool = await GetKeytoolPathAsync()
            ?? throw new InvalidOperationException("keytool not found. Please configure OpenJDK path in Settings.");

        var dname = $"CN={Escape(cn)}, OU={Escape(ou)}, O={Escape(o)}, L={Escape(l)}, ST={Escape(st)}, C={Escape(c)}";
        var fileName = $"{alias}.keystore";
        var workDirectory = CreatePrivateTempDirectory();
        var outputPath = Path.Combine(workDirectory, SanitizeFileName(fileName));

        try
        {
            var args = $"-genkeypair -v" +
                $" -keystore \"{outputPath}\"" +
                $" -alias \"{alias}\"" +
                $" -keyalg {keyAlg}" +
                $" -keysize {keySize}" +
                $" -validity {validityDays}" +
                $" -storetype {keystoreType}" +
                $" -storepass \"{keystorePassword}\"" +
                $" -keypass \"{keyPassword}\"" +
                $" -dname \"{dname}\"";

            _logger.LogInformation($"Creating keystore (alias: {alias})");

            var (exitCode, output) = await RunProcessAsync(keytool, args);
            if (exitCode != 0)
                throw new InvalidOperationException($"keytool failed (exit {exitCode}): {output}");

            var keystore = await AddKeystoreAsync(
                new AndroidKeystore(
                    Id: Guid.NewGuid().ToString(),
                    Alias: alias,
                    FilePath: fileName,
                    KeystoreType: keystoreType,
                    CreatedDate: DateTime.UtcNow),
                await File.ReadAllBytesAsync(outputPath),
                keystorePassword,
                keyPassword);

            _logger.LogInformation($"Keystore created in Local Vault: {alias}");
            return keystore;
        }
        finally
        {
            TryDeleteDirectory(workDirectory);
        }
    }

    public async Task<KeystoreSignatureInfo> GetSignatureHashesAsync(string keystorePath, string alias, string password)
    {
        var keytool = await GetKeytoolPathAsync()
            ?? throw new InvalidOperationException("keytool not found. Please configure OpenJDK path in Settings.");

        var (exitCode, output) = await RunProcessAsync(keytool,
            ["-J-Duser.language=en", "-J-Duser.country=US", "-list", "-v",
             "-keystore", keystorePath, "-alias", alias, "-storepass", password]);
        if (exitCode != 0)
            throw new InvalidOperationException($"keytool failed (exit {exitCode}): {output}");

        return ParseSignatureHashes(alias, output);
    }

    public async Task<KeystoreInfo> GetKeystoreInfoAsync(string keystorePath, string password)
    {
        var keytool = await GetKeytoolPathAsync()
            ?? throw new InvalidOperationException("keytool not found. Please configure OpenJDK path in Settings.");

        var (exitCode, output) = await RunProcessAsync(keytool,
            ["-J-Duser.language=en", "-J-Duser.country=US", "-list", "-v",
             "-keystore", keystorePath, "-storepass", password]);
        if (exitCode != 0)
            throw new InvalidOperationException($"keytool failed (exit {exitCode}): {output}");

        return ParseKeystoreInfo(output);
    }

    public async Task<bool> VerifyKeyPasswordAsync(string keystorePath, string alias, string keystorePassword, string keyPassword)
    {
        var keytool = await GetKeytoolPathAsync()
            ?? throw new InvalidOperationException("keytool not found. Please configure OpenJDK path in Settings.");

        // Generating a certificate request signs with the private key, so it fails when the
        // key password can't recover the key.
        var requestPath = Path.Combine(Path.GetTempPath(), $"sherpa-keycheck-{Guid.NewGuid():N}.csr");
        try
        {
            var (exitCode, output) = await RunProcessAsync(keytool,
                ["-J-Duser.language=en", "-J-Duser.country=US", "-certreq",
                 "-keystore", keystorePath, "-storepass", keystorePassword,
                 "-alias", alias, "-keypass", keyPassword, "-file", requestPath]);
            if (exitCode == 0)
                return true;
            if (output.Contains("UnrecoverableKeyException", StringComparison.Ordinal) ||
                output.Contains("Cannot recover key", StringComparison.Ordinal))
                return false;
            throw new InvalidOperationException($"keytool failed (exit {exitCode}): {output}");
        }
        finally
        {
            try { File.Delete(requestPath); } catch { }
        }
    }

    public async Task ExportPepkAsync(
        string keystorePath,
        string alias,
        string keystorePassword,
        string keyPassword,
        string encryptionKey,
        string outputPath)
    {
        var jdkPath = await _jdkSettings.GetEffectiveJdkPathAsync()
            ?? throw new InvalidOperationException("JDK not found. Please configure OpenJDK path in Settings.");

        var javaExe = Path.Combine(jdkPath, "bin",
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "java.exe" : "java");

        var pepkJar = GetPepkJarPath();
        if (!File.Exists(pepkJar))
            throw new InvalidOperationException($"pepk.jar not found at: {pepkJar}. Please download it from the Google Play Console.");

        var args = $"-jar \"{pepkJar}\"" +
            $" --keystore=\"{keystorePath}\"" +
            $" --alias=\"{alias}\"" +
            $" --output=\"{outputPath}\"" +
            $" --encryptionkey={encryptionKey}" +
            $" --keystorepass=\"{keystorePassword}\"" +
            $" --keypass=\"{keyPassword}\"" +
            $" --include-cert";

        _logger.LogInformation($"Exporting PEPK for alias: {alias}");

        var (exitCode, output) = await RunProcessAsync(javaExe, args);
        if (exitCode != 0)
            throw new InvalidOperationException($"PEPK export failed (exit {exitCode}): {output}");

        _logger.LogInformation($"PEPK exported to: {outputPath}");
    }

    // --- Storage ---

    public async Task<IReadOnlyList<AndroidKeystore>> ListKeystoresAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var legacy = await LoadLegacyKeystoresAsync();
            var vault = await GetVaultAsync(create: legacy.Count > 0);
            var stored = vault is null ? new List<AndroidKeystore>() : await ReadVaultKeystoresAsync(vault);

            if (vault is not null && legacy.Count > 0 && await MoveLegacyKeystoresAsync(vault, legacy, stored))
                stored = await ReadVaultKeystoresAsync(vault);

            return stored.Concat(legacy).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AndroidKeystore> AddKeystoreAsync(
        AndroidKeystore keystore,
        byte[] content,
        string? keystorePassword,
        string? keyPassword)
    {
        if (content.Length == 0)
            throw new ArgumentException("The keystore file is empty.", nameof(content));

        await _gate.WaitAsync();
        try
        {
            var vault = await RequireVaultAsync();
            var stored = await ReadVaultKeystoresAsync(vault);

            var conflict = stored.FirstOrDefault(k =>
                string.Equals(k.Alias, keystore.Alias, StringComparison.Ordinal) &&
                !string.Equals(k.Id, keystore.Id, StringComparison.Ordinal));
            if (conflict is not null)
            {
                throw new InvalidOperationException(
                    $"A keystore with alias '{keystore.Alias}' is already stored in the Local Vault. " +
                    "Remove it first, or use a keystore with a different alias.");
            }

            // The same keystore stored under its previous alias is replaced, not left behind.
            foreach (var renamed in stored.Where(k =>
                         string.Equals(k.Id, keystore.Id, StringComparison.Ordinal) &&
                         !string.Equals(k.Alias, keystore.Alias, StringComparison.Ordinal)))
            {
                await DeleteVaultArtifactsAsync(vault, renamed.Alias);
            }

            var fileName = Path.GetFileName(keystore.FilePath);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = $"{keystore.Alias}.keystore";

            await WriteVaultKeystoreAsync(
                vault,
                keystore.Alias,
                content,
                new KeystoreVaultMetadata(
                    keystore.Alias,
                    keystore.KeystoreType,
                    keystore.CreatedDate,
                    keystore.Notes,
                    DateTime.UtcNow,
                    keystore.Id,
                    fileName),
                keystorePassword,
                keyPassword);

            await RemoveLegacyKeystoreAsync(keystore.Id);
            return keystore with { FilePath = fileName, StoredInVault = true };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveKeystoreAsync(string id)
    {
        await _gate.WaitAsync();
        try
        {
            var vault = await GetVaultAsync(create: false);
            if (vault is not null)
            {
                foreach (var keystore in (await ReadVaultKeystoresAsync(vault)).Where(k => k.Id == id))
                    await DeleteVaultArtifactsAsync(vault, keystore.Alias);
            }

            await RemoveLegacyKeystoreAsync(id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<byte[]?> GetKeystoreContentAsync(string id)
    {
        var (vault, keystore) = await FindAsync(id);
        if (keystore is null)
            return null;
        if (vault is not null && keystore.StoredInVault)
            return await vault.GetSecretAsync(KeystoreVaultLayout.GetKey(keystore.Alias, KeystoreVaultLayout.FileSuffix));

        // An older keystore that could not be moved into the vault yet.
        return File.Exists(keystore.FilePath) ? await File.ReadAllBytesAsync(keystore.FilePath) : null;
    }

    public async Task<IKeystoreFileLease> OpenKeystoreFileAsync(string id)
    {
        var (_, keystore) = await FindAsync(id);
        var content = keystore is null ? null : await GetKeystoreContentAsync(id);
        if (keystore is null || content is null)
            throw new InvalidOperationException("The keystore is not stored in the Local Vault.");

        var directory = CreatePrivateTempDirectory();
        var path = Path.Combine(directory, SanitizeFileName(Path.GetFileName(keystore.FilePath), keystore.Alias));
        try
        {
            await File.WriteAllBytesAsync(path, content);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            TryDeleteDirectory(directory);
            throw;
        }

        return new KeystoreFileLease(path, directory);
    }

    public async Task ExportKeystoreFileAsync(string id, string destinationPath)
    {
        var content = await GetKeystoreContentAsync(id)
            ?? throw new InvalidOperationException("The keystore is not stored in the Local Vault.");
        await File.WriteAllBytesAsync(destinationPath, content);
    }

    public Task<string?> GetKeystorePasswordAsync(string id) =>
        GetPasswordAsync(id, KeystoreVaultLayout.PasswordSuffix, LegacyPasswordKeyPrefix);

    public Task SaveKeystorePasswordAsync(string id, string password) =>
        SavePasswordAsync(id, password, KeystoreVaultLayout.PasswordSuffix, LegacyPasswordKeyPrefix);

    public Task<string?> GetKeyPasswordAsync(string id) =>
        GetPasswordAsync(id, KeystoreVaultLayout.KeyPasswordSuffix, LegacyKeyPasswordKeyPrefix);

    public Task SaveKeyPasswordAsync(string id, string password) =>
        SavePasswordAsync(id, password, KeystoreVaultLayout.KeyPasswordSuffix, LegacyKeyPasswordKeyPrefix);

    private async Task<string?> GetPasswordAsync(string id, string suffix, string legacyPrefix)
    {
        var (vault, keystore) = await FindAsync(id);
        if (vault is not null && keystore is { StoredInVault: true })
        {
            var bytes = await vault.GetSecretAsync(KeystoreVaultLayout.GetKey(keystore.Alias, suffix));
            return bytes is null ? null : Encoding.UTF8.GetString(bytes);
        }

        return await _secureStorage.GetAsync(legacyPrefix + id);
    }

    private async Task SavePasswordAsync(string id, string password, string suffix, string legacyPrefix)
    {
        var (vault, keystore) = await FindAsync(id);
        if (vault is not null && keystore is { StoredInVault: true })
        {
            if (!await vault.StoreSecretAsync(KeystoreVaultLayout.GetKey(keystore.Alias, suffix), Encoding.UTF8.GetBytes(password)))
                throw new InvalidOperationException($"Could not save the password for '{keystore.Alias}' in the Local Vault.");
            return;
        }

        await _secureStorage.SetAsync(legacyPrefix + id, password);
    }

    private async Task<(ICloudSecretsProvider? Vault, AndroidKeystore? Keystore)> FindAsync(string id)
    {
        var keystore = (await ListKeystoresAsync()).FirstOrDefault(k => k.Id == id);
        var vault = keystore is { StoredInVault: true } ? await GetVaultAsync(create: false) : null;
        return (vault, keystore);
    }

    private async Task<ICloudSecretsProvider?> GetVaultAsync(bool create)
    {
        if (_providerRegistry is null)
            return null;
        if (create && _cloudSecrets is not null)
            await _cloudSecrets.EnableDefaultLocalProviderAsync(setActiveProvider: false);
        return await _providerRegistry.GetProviderAsync(CloudSecretsService.DefaultLocalProviderId);
    }

    private async Task<ICloudSecretsProvider> RequireVaultAsync() =>
        await GetVaultAsync(create: true)
        ?? throw new InvalidOperationException(
            "Keystores are stored in the Local Vault, which is not available. Turn on the Local Vault in Settings.");

    private static async Task<List<AndroidKeystore>> ReadVaultKeystoresAsync(ICloudSecretsProvider vault)
    {
        var keystores = new List<AndroidKeystore>();
        var keys = await vault.ListSecretsAsync(KeystoreVaultLayout.KeyPrefix);
        foreach (var key in keys)
        {
            if (!SecretItemAdapterHelper.TryExtractAffixedId(
                    key, KeystoreVaultLayout.KeyPrefix, "_" + KeystoreVaultLayout.FileSuffix, out var alias))
                continue;

            var metadata = KeystoreVaultLayout.DeserializeMetadata(
                await vault.GetSecretAsync(KeystoreVaultLayout.GetKey(alias, KeystoreVaultLayout.MetadataSuffix)));
            keystores.Add(new AndroidKeystore(
                Id: string.IsNullOrWhiteSpace(metadata?.Id) ? KeystoreVaultLayout.GetFallbackId(alias) : metadata.Id,
                Alias: alias,
                FilePath: string.IsNullOrWhiteSpace(metadata?.FileName) ? $"{alias}.keystore" : metadata.FileName,
                KeystoreType: string.IsNullOrWhiteSpace(metadata?.KeystoreType) ? "PKCS12" : metadata.KeystoreType,
                CreatedDate: metadata?.CreatedDate ?? DateTime.UnixEpoch,
                Notes: metadata?.Notes,
                StoredInVault: true));
        }

        return keystores.OrderBy(k => k.Alias, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static async Task WriteVaultKeystoreAsync(
        ICloudSecretsProvider vault,
        string alias,
        byte[] content,
        KeystoreVaultMetadata metadata,
        string? keystorePassword,
        string? keyPassword)
    {
        var artifacts = new List<SecretArtifact>
        {
            new(KeystoreVaultLayout.GetKey(alias, KeystoreVaultLayout.FileSuffix), content),
            new(KeystoreVaultLayout.GetKey(alias, KeystoreVaultLayout.MetadataSuffix), KeystoreVaultLayout.SerializeMetadata(metadata))
        };
        if (keystorePassword is not null)
            artifacts.Add(new(KeystoreVaultLayout.GetKey(alias, KeystoreVaultLayout.PasswordSuffix), Encoding.UTF8.GetBytes(keystorePassword)));
        if (keyPassword is not null)
            artifacts.Add(new(KeystoreVaultLayout.GetKey(alias, KeystoreVaultLayout.KeyPasswordSuffix), Encoding.UTF8.GetBytes(keyPassword)));

        if (!await SecretItemAdapterHelper.WriteArtifactsAsync(vault, artifacts, CancellationToken.None))
            throw new InvalidOperationException($"Could not store keystore '{alias}' in the Local Vault.");
    }

    private static async Task DeleteVaultArtifactsAsync(ICloudSecretsProvider vault, string alias)
    {
        foreach (var suffix in KeystoreVaultLayout.Suffixes)
            await vault.DeleteSecretAsync(KeystoreVaultLayout.GetKey(alias, suffix));
    }

    /// <summary>
    /// Moves older keystores into the vault: from their file when it is still on disk, or by
    /// adopting a vault copy of the same alias (for example one synced there earlier) when the
    /// file is gone. Keystores that can be neither stay in the legacy registry. Returns true
    /// when anything moved.
    /// </summary>
    private async Task<bool> MoveLegacyKeystoresAsync(
        ICloudSecretsProvider vault,
        List<AndroidKeystore> legacy,
        List<AndroidKeystore> stored)
    {
        var moved = false;
        foreach (var keystore in legacy.ToList())
        {
            try
            {
                var existing = stored.FirstOrDefault(k => string.Equals(k.Alias, keystore.Alias, StringComparison.Ordinal));
                if (existing is not null &&
                    existing.Id != keystore.Id &&
                    existing.Id != KeystoreVaultLayout.GetFallbackId(existing.Alias))
                {
                    _logger.LogWarning(
                        $"Keystore '{keystore.Alias}' was not moved into the Local Vault: a different keystore already uses that alias.");
                    continue;
                }

                var content = File.Exists(keystore.FilePath) ? await File.ReadAllBytesAsync(keystore.FilePath) : null;
                if (content is null)
                {
                    if (existing is null)
                        continue;
                    content = await vault.GetSecretAsync(KeystoreVaultLayout.GetKey(existing.Alias, KeystoreVaultLayout.FileSuffix));
                    if (content is null)
                        continue;
                }

                // Passwords already in the vault copy win over nothing; saved ones win over the vault copy.
                var keystorePassword = await _secureStorage.GetAsync(LegacyPasswordKeyPrefix + keystore.Id)
                    ?? await ReadVaultStringAsync(vault, keystore.Alias, KeystoreVaultLayout.PasswordSuffix);
                var keyPassword = await _secureStorage.GetAsync(LegacyKeyPasswordKeyPrefix + keystore.Id)
                    ?? await ReadVaultStringAsync(vault, keystore.Alias, KeystoreVaultLayout.KeyPasswordSuffix);

                await WriteVaultKeystoreAsync(
                    vault,
                    keystore.Alias,
                    content,
                    new KeystoreVaultMetadata(
                        keystore.Alias,
                        keystore.KeystoreType,
                        keystore.CreatedDate,
                        keystore.Notes,
                        DateTime.UtcNow,
                        keystore.Id,
                        Path.GetFileName(keystore.FilePath)),
                    keystorePassword,
                    keyPassword);

                await RemoveLegacyKeystoreAsync(keystore.Id);
                legacy.Remove(keystore);
                moved = true;
                _logger.LogInformation($"Moved keystore '{keystore.Alias}' into the Local Vault.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not move keystore '{keystore.Alias}' into the Local Vault: {ex.Message}");
            }
        }

        return moved;
    }

    private static async Task<string?> ReadVaultStringAsync(ICloudSecretsProvider vault, string alias, string suffix)
    {
        var bytes = await vault.GetSecretAsync(KeystoreVaultLayout.GetKey(alias, suffix));
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    // --- Keytool output parsing ---

    internal static KeystoreInfo ParseKeystoreInfo(string keytoolOutput)
    {
        string? keystoreType = null;
        string? alias = null;
        var aliases = new List<string>();

        foreach (var rawLine in keytoolOutput.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("Keystore type: ", StringComparison.Ordinal))
                keystoreType = line["Keystore type: ".Length..].Trim();
            else if (line.StartsWith("Alias name: ", StringComparison.Ordinal))
                alias = line["Alias name: ".Length..];
            else if (line.StartsWith("Entry type: ", StringComparison.Ordinal))
            {
                if (line["Entry type: ".Length..].Trim() == "PrivateKeyEntry" &&
                    !string.IsNullOrEmpty(alias))
                {
                    aliases.Add(alias);
                }
                alias = null;
            }
        }

        if (string.IsNullOrEmpty(keystoreType))
            throw new InvalidOperationException("Could not read the keystore type from keytool output.");
        if (aliases.Count == 0)
            throw new InvalidOperationException("The keystore does not contain any private key entries for Android signing.");

        return new KeystoreInfo(keystoreType, aliases.AsReadOnly());
    }

    internal static KeystoreSignatureInfo ParseSignatureHashes(string alias, string keytoolOutput)
    {
        string? md5Hex = null, sha1Hex = null, sha256Hex = null;

        var md5Match = Regex.Match(keytoolOutput, @"MD5:\s+([\dA-Fa-f:]+)");
        if (md5Match.Success)
            md5Hex = md5Match.Groups[1].Value.Trim();

        var sha1Match = Regex.Match(keytoolOutput, @"SHA1:\s+([\dA-Fa-f:]+)");
        if (sha1Match.Success)
            sha1Hex = sha1Match.Groups[1].Value.Trim();

        var sha256Match = Regex.Match(keytoolOutput, @"SHA256:\s+([\dA-Fa-f:]+)");
        if (sha256Match.Success)
            sha256Hex = sha256Match.Groups[1].Value.Trim();

        // Convert hex to Base64 (used by Facebook, some other services)
        var sha1Base64 = sha1Hex != null ? HexToBase64(sha1Hex) : null;
        var sha256Base64 = sha256Hex != null ? HexToBase64(sha256Hex) : null;

        return new KeystoreSignatureInfo(alias, md5Hex, sha1Hex, sha256Hex, sha1Base64, sha256Base64);
    }

    internal static string HexToBase64(string colonSeparatedHex)
    {
        var bytes = colonSeparatedHex
            .Split(':')
            .Select(h => Convert.ToByte(h, 16))
            .ToArray();
        return Convert.ToBase64String(bytes);
    }

    // --- Helpers ---

    private static string GetPepkJarPath()
    {
        var appData = AppDataPath.GetAppDataDirectory();
        return Path.Combine(appData, "tools", "pepk.jar");
    }

    private static string Escape(string value)
        => value.Replace("\"", "\\\"").Replace(",", "\\,");

    private static Task<(int ExitCode, string Output)> RunProcessAsync(string fileName, string arguments)
        => RunProcessAsync(new ProcessStartInfo { FileName = fileName, Arguments = arguments });

    private static Task<(int ExitCode, string Output)> RunProcessAsync(string fileName, string[] arguments)
    {
        var psi = new ProcessStartInfo { FileName = fileName };
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);
        return RunProcessAsync(psi);
    }

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(ProcessStartInfo psi)
    {
        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start: {psi.FileName}");

        // keytool prompts on stdin when a password is wrong or missing; give it nothing to wait on.
        process.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        var output = string.IsNullOrEmpty(stdout) ? stderr : stdout + "\n" + stderr;
        return (process.ExitCode, output.Trim());
    }

    private async Task<List<AndroidKeystore>> LoadLegacyKeystoresAsync()
    {
        if (!File.Exists(LegacyRegistryPath))
            return new();

        try
        {
            var json = await File.ReadAllTextAsync(LegacyRegistryPath);
            return (JsonSerializer.Deserialize<List<AndroidKeystore>>(json) ?? new())
                .Select(k => k with { StoredInVault = false })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to load keystores: {ex.Message}", ex);
            return new();
        }
    }

    private async Task RemoveLegacyKeystoreAsync(string id)
    {
        var legacy = await LoadLegacyKeystoresAsync();
        if (legacy.RemoveAll(k => k.Id == id) > 0)
        {
            var json = JsonSerializer.Serialize(legacy, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(LegacyRegistryPath, json);
        }

        await _secureStorage.RemoveAsync(LegacyPasswordKeyPrefix + id);
        await _secureStorage.RemoveAsync(LegacyKeyPasswordKeyPrefix + id);
    }

    private static string CreatePrivateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "sherpa-keystores", Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(path);
        else
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }

    private static string SanitizeFileName(string? fileName, string fallbackAlias = "keystore")
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or "..")
            fileName = $"{fallbackAlias}.keystore";
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(fileName.Select(c => invalid.Contains(c) ? '_' : c));
    }

    private sealed class KeystoreFileLease(string path, string directory) : IKeystoreFileLease
    {
        public string Path { get; } = path;

        public ValueTask DisposeAsync()
        {
            TryDeleteDirectory(directory);
            return ValueTask.CompletedTask;
        }
    }
}
