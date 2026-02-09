using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class KeystoreService : IKeystoreService
{
    private const string KeystoresFileName = "android-keystores.json";
    private const string PasswordKeyPrefix = "android_keystore_pwd_";

    private readonly IOpenJdkSettingsService _jdkSettings;
    private readonly ISecureStorageService _secureStorage;
    private readonly ILoggingService _logger;

    private List<AndroidKeystore>? _keystores;

    public KeystoreService(
        IOpenJdkSettingsService jdkSettings,
        ISecureStorageService secureStorage,
        ILoggingService logger)
    {
        _jdkSettings = jdkSettings;
        _secureStorage = secureStorage;
        _logger = logger;
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
        string outputPath,
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

        _logger.LogInformation($"Creating keystore: {outputPath} (alias: {alias})");

        // PickSaveFileAsync may create an empty file — keytool refuses to overwrite it
        if (File.Exists(outputPath) && new FileInfo(outputPath).Length == 0)
            File.Delete(outputPath);

        var (exitCode, output) = await RunProcessAsync(keytool, args);
        if (exitCode != 0)
            throw new InvalidOperationException($"keytool failed (exit {exitCode}): {output}");

        var keystore = new AndroidKeystore(
            Id: Guid.NewGuid().ToString(),
            Alias: alias,
            FilePath: outputPath,
            KeystoreType: keystoreType,
            CreatedDate: DateTime.UtcNow);

        await AddKeystoreAsync(keystore);

        // Store password securely
        await _secureStorage.SetAsync(PasswordKeyPrefix + keystore.Id, keystorePassword);

        _logger.LogInformation($"Keystore created: {outputPath}");
        return keystore;
    }

    public async Task<KeystoreSignatureInfo> GetSignatureHashesAsync(string keystorePath, string alias, string password)
    {
        var keytool = await GetKeytoolPathAsync()
            ?? throw new InvalidOperationException("keytool not found. Please configure OpenJDK path in Settings.");

        var args = $"-list -v -keystore \"{keystorePath}\" -alias \"{alias}\" -storepass \"{password}\"";

        var (exitCode, output) = await RunProcessAsync(keytool, args);
        if (exitCode != 0)
            throw new InvalidOperationException($"keytool failed (exit {exitCode}): {output}");

        return ParseSignatureHashes(alias, output);
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

    public async Task<IReadOnlyList<AndroidKeystore>> ListKeystoresAsync()
    {
        await EnsureKeystoresLoadedAsync();
        return _keystores!.AsReadOnly();
    }

    public async Task AddKeystoreAsync(AndroidKeystore keystore)
    {
        await EnsureKeystoresLoadedAsync();
        _keystores!.Add(keystore);
        await SaveKeystoresAsync();
    }

    public async Task RemoveKeystoreAsync(string id)
    {
        await EnsureKeystoresLoadedAsync();
        _keystores!.RemoveAll(k => k.Id == id);
        await _secureStorage.RemoveAsync(PasswordKeyPrefix + id);
        await SaveKeystoresAsync();
    }

    // --- Signature parsing ---

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

    private async Task<(int ExitCode, string Output)> RunProcessAsync(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start: {fileName}");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var output = string.IsNullOrEmpty(stdout) ? stderr : stdout + "\n" + stderr;
        return (process.ExitCode, output.Trim());
    }

    private async Task EnsureKeystoresLoadedAsync()
    {
        if (_keystores != null) return;

        var filePath = Path.Combine(AppDataPath.GetAppDataDirectory(), KeystoresFileName);
        if (File.Exists(filePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                _keystores = JsonSerializer.Deserialize<List<AndroidKeystore>>(json) ?? new();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to load keystores: {ex.Message}", ex);
                _keystores = new();
            }
        }
        else
        {
            _keystores = new();
        }
    }

    private async Task SaveKeystoresAsync()
    {
        var filePath = Path.Combine(AppDataPath.GetAppDataDirectory(), KeystoresFileName);
        var json = JsonSerializer.Serialize(_keystores, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(filePath, json);
    }
}
