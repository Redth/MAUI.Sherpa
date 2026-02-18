using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Service for syncing certificate private keys between local keychain and cloud storage
/// </summary>
public class CertificateSyncService : ICertificateSyncService
{
    private readonly ICloudSecretsService _cloudSecretsService;
    private readonly ILocalCertificateService _localCertificateService;
    private readonly ILoggingService _logger;
    
    private const string SecretPrefix = "CERT";

    public CertificateSyncService(
        ICloudSecretsService cloudSecretsService,
        ILocalCertificateService localCertificateService,
        ILoggingService logger)
    {
        _cloudSecretsService = cloudSecretsService;
        _localCertificateService = localCertificateService;
        _logger = logger;
    }

    public string GetCertificateSecretKey(string serialNumber)
        => $"{SecretPrefix}_{SanitizeSerialNumber(serialNumber)}_P12";

    public string GetCertificatePasswordKey(string serialNumber)
        => $"{SecretPrefix}_{SanitizeSerialNumber(serialNumber)}_PWD";

    public string GetCertificateMetadataKey(string serialNumber)
        => $"{SecretPrefix}_{SanitizeSerialNumber(serialNumber)}_META";

    public async Task<IReadOnlyList<CertificateSecretInfo>> GetCertificateStatusesAsync(
        IEnumerable<AppleCertificate> certificates,
        CancellationToken cancellationToken = default)
    {
        var results = new List<CertificateSecretInfo>();
        
        // Get all local signing identities
        // Note: security find-identity only returns identities with private keys,
        // so all returned identities have private keys by definition
        var localIdentities = _localCertificateService.IsSupported 
            ? await _localCertificateService.GetSigningIdentitiesAsync()
            : Array.Empty<LocalSigningIdentity>();
        
        var localSerials = localIdentities
            .Where(i => i.IsValid) // Only consider valid certificates
            .Select(i => SanitizeSerialNumber(i.SerialNumber ?? "")) // Sanitize local serials too!
            .Where(s => !string.IsNullOrEmpty(s))
            .ToHashSet();

        // Check cloud storage if a provider is configured
        var cloudSecrets = new HashSet<string>();
        if (_cloudSecretsService.ActiveProvider != null)
        {
            try
            {
                var secrets = await _cloudSecretsService.ListSecretsAsync($"{SecretPrefix}_", cancellationToken);
                foreach (var secret in secrets)
                {
                    // Extract serial number from key like "CERT_XXXX_P12"
                    var parts = secret.Split('_');
                    if (parts.Length >= 2)
                    {
                        cloudSecrets.Add(parts[1].ToUpperInvariant());
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not check cloud storage: {ex.Message}");
            }
        }

        foreach (var cert in certificates)
        {
            var serialUpper = cert.SerialNumber?.ToUpperInvariant() ?? "";
            var sanitizedSerial = SanitizeSerialNumber(serialUpper);
            
            var hasLocal = localSerials.Contains(sanitizedSerial);
            var hasCloud = cloudSecrets.Contains(sanitizedSerial);

            var location = (hasLocal, hasCloud) switch
            {
                (true, true) => SecretLocation.Both,
                (true, false) => SecretLocation.LocalOnly,
                (false, true) => SecretLocation.CloudOnly,
                _ => SecretLocation.None
            };

            results.Add(new CertificateSecretInfo(
                cert.Id ?? "",
                cert.SerialNumber ?? "",
                location,
                hasCloud ? _cloudSecretsService.ActiveProvider?.Id : null,
                hasCloud ? GetCertificateSecretKey(cert.SerialNumber ?? "") : null,
                null // Would need to track last sync time separately
            ));
        }

        return results.AsReadOnly();
    }

    public async Task<bool> UploadToCloudAsync(
        AppleCertificate certificate,
        byte[] p12Data,
        string password,
        CertificateSecretMetadata? metadata = null,
        CancellationToken cancellationToken = default)
    {
        if (_cloudSecretsService.ActiveProvider == null)
        {
            _logger.LogWarning("No active cloud secrets provider configured");
            return false;
        }

        var serialNumber = certificate.SerialNumber ?? "";
        
        try
        {
            // Store the P12 data
            var p12Key = GetCertificateSecretKey(serialNumber);
            if (!await _cloudSecretsService.StoreSecretAsync(p12Key, p12Data, null, cancellationToken))
            {
                _logger.LogError($"Failed to store P12 data for certificate {serialNumber}");
                return false;
            }

            // Store the password
            var pwdKey = GetCertificatePasswordKey(serialNumber);
            var pwdBytes = Encoding.UTF8.GetBytes(password);
            if (!await _cloudSecretsService.StoreSecretAsync(pwdKey, pwdBytes, null, cancellationToken))
            {
                _logger.LogError($"Failed to store password for certificate {serialNumber}");
                // Clean up the P12 we just stored
                await _cloudSecretsService.DeleteSecretAsync(p12Key, cancellationToken);
                return false;
            }

            // Store metadata if provided
            if (metadata != null)
            {
                var metaKey = GetCertificateMetadataKey(serialNumber);
                var metaJson = JsonSerializer.Serialize(metadata);
                var metaBytes = Encoding.UTF8.GetBytes(metaJson);
                await _cloudSecretsService.StoreSecretAsync(metaKey, metaBytes, null, cancellationToken);
            }

            _logger.LogInformation($"Uploaded certificate {serialNumber} to cloud storage");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to upload certificate to cloud: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> DownloadAndInstallAsync(string certificateId, CancellationToken cancellationToken = default)
    {
        if (_cloudSecretsService.ActiveProvider == null)
        {
            _logger.LogWarning("No active cloud secrets provider configured");
            return false;
        }

        // We need to find the serial number from certificate ID
        // For now, we'll need this to be passed differently or stored in metadata
        _logger.LogWarning("DownloadAndInstallAsync not yet implemented - needs serial number lookup");
        return false;
        
        // TODO: Implement full flow:
        // 1. Get P12 data from cloud
        // 2. Get password from cloud
        // 3. Import into local keychain using security import command
    }

    /// <summary>
    /// Downloads and installs a certificate from cloud using its serial number
    /// </summary>
    public async Task<bool> DownloadAndInstallBySerialAsync(
        string serialNumber,
        CancellationToken cancellationToken = default)
    {
        if (_cloudSecretsService.ActiveProvider == null)
        {
            _logger.LogWarning("No active cloud secrets provider configured");
            return false;
        }

        try
        {
            Console.WriteLine($"DownloadAndInstallBySerialAsync starting for serial: {serialNumber}");
            _logger.LogInformation($"DownloadAndInstallBySerialAsync starting for serial: {serialNumber}");
            
            // Get P12 data
            var p12Key = GetCertificateSecretKey(serialNumber);
            Console.WriteLine($"Looking for P12 with key: {p12Key}");
            _logger.LogInformation($"Looking for P12 with key: {p12Key}");
            var p12Data = await _cloudSecretsService.GetSecretAsync(p12Key, cancellationToken);
            if (p12Data == null)
            {
                Console.WriteLine($"P12 data not found in cloud for serial {serialNumber} (key: {p12Key})");
                _logger.LogError($"P12 data not found in cloud for serial {serialNumber} (key: {p12Key})");
                return false;
            }
            Console.WriteLine($"Got P12 data: {p12Data.Length} bytes");
            _logger.LogInformation($"Got P12 data: {p12Data.Length} bytes");

            // Get password
            var pwdKey = GetCertificatePasswordKey(serialNumber);
            Console.WriteLine($"Looking for password with key: {pwdKey}");
            _logger.LogInformation($"Looking for password with key: {pwdKey}");
            var pwdData = await _cloudSecretsService.GetSecretAsync(pwdKey, cancellationToken);
            if (pwdData == null)
            {
                Console.WriteLine($"Password not found in cloud for serial {serialNumber} (key: {pwdKey})");
                _logger.LogError($"Password not found in cloud for serial {serialNumber} (key: {pwdKey})");
                return false;
            }
            var password = Encoding.UTF8.GetString(pwdData);
            Console.WriteLine($"Got password: {password.Length} chars");
            _logger.LogInformation($"Got password: {password.Length} chars");

            // Import into local keychain
            Console.WriteLine("Importing P12 to keychain...");
            _logger.LogInformation("Importing P12 to keychain...");
            return await ImportP12ToKeychainAsync(p12Data, password, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to download and install certificate: {ex}");
            _logger.LogError($"Failed to download and install certificate: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    /// Gets certificate metadata from cloud storage
    /// </summary>
    public async Task<CertificateSecretMetadata?> GetCertificateMetadataAsync(
        string serialNumber,
        CancellationToken cancellationToken = default)
    {
        if (_cloudSecretsService.ActiveProvider == null)
            return null;

        try
        {
            var metaKey = GetCertificateMetadataKey(serialNumber);
            var metaData = await _cloudSecretsService.GetSecretAsync(metaKey, cancellationToken);
            if (metaData == null)
                return null;

            var metaJson = Encoding.UTF8.GetString(metaData);
            return JsonSerializer.Deserialize<CertificateSecretMetadata>(metaJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Could not get certificate metadata: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> DeleteFromCloudAsync(string serialNumber, CancellationToken cancellationToken = default)
    {
        if (_cloudSecretsService.ActiveProvider == null)
        {
            _logger.LogError("No cloud secrets provider configured");
            return false;
        }

        _logger.LogInformation($"Deleting certificate from cloud: {serialNumber}");

        var p12Key = GetCertificateSecretKey(serialNumber);
        var pwdKey = GetCertificatePasswordKey(serialNumber);
        var metaKey = GetCertificateMetadataKey(serialNumber);

        var success = true;

        // Delete P12 data
        try
        {
            if (!await _cloudSecretsService.DeleteSecretAsync(p12Key, cancellationToken))
            {
                _logger.LogWarning($"Failed to delete P12 secret: {p12Key}");
                success = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error deleting P12 secret: {ex.Message}");
        }

        // Delete password
        try
        {
            if (!await _cloudSecretsService.DeleteSecretAsync(pwdKey, cancellationToken))
            {
                _logger.LogWarning($"Failed to delete password secret: {pwdKey}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error deleting password secret: {ex.Message}");
        }

        // Delete metadata
        try
        {
            if (!await _cloudSecretsService.DeleteSecretAsync(metaKey, cancellationToken))
            {
                _logger.LogWarning($"Failed to delete metadata secret: {metaKey}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error deleting metadata secret: {ex.Message}");
        }

        if (success)
        {
            _logger.LogInformation($"Successfully deleted certificate from cloud: {serialNumber}");
        }

        return success;
    }

    #region Private Helpers

    private static string SanitizeSerialNumber(string serialNumber)
    {
        // Remove any non-alphanumeric characters and convert to uppercase
        var sb = new StringBuilder();
        foreach (var c in serialNumber)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToUpperInvariant(c));
        }
        // Strip leading zeros — local keychain may include them but API may not
        return sb.ToString().TrimStart('0');
    }

    private async Task<bool> ImportP12ToKeychainAsync(
        byte[] p12Data,
        string password,
        CancellationToken cancellationToken)
    {
        if (!_localCertificateService.IsSupported)
        {
            _logger.LogWarning("Cannot import P12 to keychain: not supported on this platform");
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return ImportP12ToWindowsStore(p12Data, password);
        }

        // macOS: use security CLI to import into login keychain
        var tempFile = Path.GetTempFileName() + ".p12";
        try
        {
            await File.WriteAllBytesAsync(tempFile, p12Data, cancellationToken);

            var loginKeychain = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library/Keychains/login.keychain-db");
            
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "security",
                    Arguments = $"import \"{tempFile}\" -k \"{loginKeychain}\" -P \"{password}\" -T /usr/bin/codesign -T /usr/bin/security",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync(cancellationToken);

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                _logger.LogError($"Failed to import P12: {error}");
                return false;
            }

            _logger.LogInformation("Successfully imported certificate to keychain");
            return true;
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    private bool ImportP12ToWindowsStore(byte[] p12Data, string password)
    {
        try
        {
            using var store = new System.Security.Cryptography.X509Certificates.X509Store(
                System.Security.Cryptography.X509Certificates.StoreName.My,
                System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser);
            store.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadWrite);

            // Import using X509Certificate2Collection to properly persist the cert + private key.
            // Loading X509Certificate2 from bytes and calling store.Add() can lose the private key
            // on Windows because the ephemeral key container is cleaned up on dispose.
            var collection = new System.Security.Cryptography.X509Certificates.X509Certificate2Collection();
            collection.Import(p12Data, password,
                System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.PersistKeySet
                | System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.UserKeySet
                | System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.Exportable);

            foreach (var cert in collection)
            {
                store.Add(cert);
                _logger.LogInformation($"Imported certificate: {cert.Subject} (serial: {cert.SerialNumber}, hasKey: {cert.HasPrivateKey})");
                cert.Dispose();
            }

            _logger.LogInformation("Successfully imported certificate to Windows certificate store");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to import P12 to Windows store: {ex.Message}", ex);
            return false;
        }
    }

    #endregion
}
