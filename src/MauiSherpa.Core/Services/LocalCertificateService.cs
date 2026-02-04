using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Service for managing local signing identities in the macOS keychain.
/// Uses the 'security' command-line tool to query and export certificates.
/// </summary>
public partial class LocalCertificateService : ILocalCertificateService
{
    private readonly ILoggingService _logger;
    private readonly IPlatformService _platform;
    
    // Cache of signing identities (in-memory, expires after 5 minutes)
    private List<LocalSigningIdentity>? _cachedIdentities;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    
    // Persistent cache of hash -> serial number mappings (on disk)
    private Dictionary<string, string>? _serialNumberCache;
    private readonly string _serialCachePath;

    public LocalCertificateService(ILoggingService logger, IPlatformService platform)
    {
        _logger = logger;
        _platform = platform;
        
        // Set up persistent cache path
        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".maui-sherpa", "cache");
        Directory.CreateDirectory(cacheDir);
        _serialCachePath = Path.Combine(cacheDir, "cert-serials.json");
    }

    public bool IsSupported => _platform.IsMacCatalyst;
    
    public void InvalidateCache()
    {
        _cachedIdentities = null;
        _cacheExpiry = DateTime.MinValue;
        _logger.LogInformation("Local certificate cache invalidated");
    }

    public async Task<IReadOnlyList<LocalSigningIdentity>> GetSigningIdentitiesAsync()
    {
        if (!IsSupported)
        {
            _logger.LogWarning("LocalCertificateService is only supported on macOS");
            return Array.Empty<LocalSigningIdentity>();
        }

        // Return cached results if still valid
        if (_cachedIdentities != null && DateTime.UtcNow < _cacheExpiry)
        {
            return _cachedIdentities.AsReadOnly();
        }

        _logger.LogInformation("Querying local keychain for signing identities...");
        
        var identities = new List<LocalSigningIdentity>();
        
        try
        {
            // Run: security find-identity -v -p codesigning
            var result = await RunSecurityCommandAsync("find-identity", "-v", "-p", "codesigning");
            
            if (result.ExitCode != 0)
            {
                _logger.LogError($"security find-identity failed with exit code {result.ExitCode}");
                return identities.AsReadOnly();
            }

            // Parse output - each line looks like:
            // 1) HASH "Identity String"
            // or with CSSMERR_TP_CERT_EXPIRED for invalid certs
            var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                var identity = ParseIdentityLine(line);
                if (identity != null)
                {
                    // Look up the serial number for this identity
                    if (!string.IsNullOrEmpty(identity.Hash))
                    {
                        var serialNumber = await GetCertificateSerialNumberAsync(identity.Hash);
                        if (!string.IsNullOrEmpty(serialNumber))
                        {
                            identity = identity with { SerialNumber = serialNumber };
                            _logger.LogDebug($"Found identity: {identity.CommonName}, Serial: {serialNumber} (Valid: {identity.IsValid})");
                        }
                        else
                        {
                            _logger.LogDebug($"Found identity: {identity.CommonName}, Serial: (not found) (Valid: {identity.IsValid})");
                        }
                    }
                    
                    identities.Add(identity);
                }
            }

            _logger.LogInformation($"Found {identities.Count} signing identities in keychain");
            
            // Cache the results
            _cachedIdentities = identities;
            _cacheExpiry = DateTime.UtcNow + CacheDuration;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to query signing identities: {ex.Message}", ex);
        }

        return identities.AsReadOnly();
    }

    public async Task<bool> HasPrivateKeyAsync(string serialNumber)
    {
        if (!IsSupported || string.IsNullOrEmpty(serialNumber))
            return false;

        var identities = await GetSigningIdentitiesAsync();
        
        // Check if any local identity matches the serial number
        return identities.Any(i => 
            i.SerialNumber != null && 
            i.SerialNumber.Equals(serialNumber, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<byte[]> ExportP12Async(string identity, string password)
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("P12 export is only supported on macOS");

        if (string.IsNullOrEmpty(identity))
            throw new ArgumentException("Identity cannot be empty", nameof(identity));

        _logger.LogInformation($"Exporting P12 for identity: {identity}");

        var tempFile = Path.GetTempFileName();
        var loginKeychain = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library/Keychains/login.keychain-db");
        
        try
        {
            // Use security command to export the identity
            // security export -t identities -f pkcs12 -P password -o output.p12
            var result = await RunSecurityCommandAsync(
                "export",
                "-t", "identities",
                "-f", "pkcs12",
                "-P", password,
                "-o", tempFile,
                "-k", loginKeychain
            );

            if (result.ExitCode != 0)
            {
                _logger.LogError($"P12 export failed: {result.Error}");
                throw new InvalidOperationException($"Failed to export P12: {result.Error}");
            }

            // Read the exported file
            var p12Data = await File.ReadAllBytesAsync(tempFile);
            _logger.LogInformation($"Exported P12: {p12Data.Length} bytes");
            
            return p12Data;
        }
        finally
        {
            // Clean up temp file
            try { File.Delete(tempFile); } catch { }
        }
    }

    public async Task<byte[]> ExportCertificateAsync(string serialNumber)
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("Certificate export is only supported on macOS");

        if (string.IsNullOrEmpty(serialNumber))
            throw new ArgumentException("Serial number cannot be empty", nameof(serialNumber));

        _logger.LogInformation($"Exporting certificate for serial: {serialNumber}");

        // Find the certificate with this serial number from the keychain
        var result = await RunSecurityCommandAsync("find-certificate", "-a", "-Z", "-p");
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to list certificates: {result.Error}");
        }

        // Parse output to find the certificate with matching serial
        var lines = result.Output.Split('\n');
        string? currentPem = null;
        var inCert = false;
        var pemBuilder = new System.Text.StringBuilder();

        foreach (var line in lines)
        {
            if (line.StartsWith("-----BEGIN CERTIFICATE-----"))
            {
                inCert = true;
                pemBuilder.Clear();
                pemBuilder.AppendLine(line);
            }
            else if (line.StartsWith("-----END CERTIFICATE-----"))
            {
                pemBuilder.AppendLine(line);
                currentPem = pemBuilder.ToString();
                inCert = false;

                // Check if this cert has our serial number
                var serial = await GetSerialFromPemAsync(currentPem);
                if (serial?.Equals(serialNumber, StringComparison.OrdinalIgnoreCase) == true)
                {
                    // Convert PEM to DER
                    var base64 = currentPem
                        .Replace("-----BEGIN CERTIFICATE-----", "")
                        .Replace("-----END CERTIFICATE-----", "")
                        .Replace("\n", "")
                        .Replace("\r", "")
                        .Trim();
                    return Convert.FromBase64String(base64);
                }
            }
            else if (inCert)
            {
                pemBuilder.AppendLine(line);
            }
        }

        throw new InvalidOperationException($"Certificate with serial {serialNumber} not found in keychain");
    }

    private async Task<string?> GetSerialFromPemAsync(string pem)
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, pem);
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "openssl",
                    Arguments = $"x509 -serial -noout -in \"{tempFile}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            // Output is like "serial=3561ADFB67EF2B1DF51F4B1B29299BB5"
            if (output.StartsWith("serial=", StringComparison.OrdinalIgnoreCase))
            {
                return output.Substring(7).Trim();
            }
            return null;
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    public async Task DeleteCertificateAsync(string identity)
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("Certificate deletion is only supported on macOS");

        if (string.IsNullOrEmpty(identity))
            throw new ArgumentException("Identity cannot be empty", nameof(identity));

        _logger.LogInformation($"Deleting certificate: {identity}");

        // Delete the identity (certificate + private key) from the keychain
        // security delete-identity -c "Common Name" deletes by common name
        // We'll use the hash to be more precise
        
        // First, try to delete the private key
        var deleteKeyResult = await RunSecurityCommandAsync(
            "delete-identity",
            "-t", // delete the identity (cert + key)
            "-c", identity // common name from the identity string
        );

        if (deleteKeyResult.ExitCode != 0)
        {
            _logger.LogWarning($"Failed to delete identity, trying certificate only: {deleteKeyResult.Error}");
            
            // Try to delete just the certificate
            var deleteCertResult = await RunSecurityCommandAsync(
                "delete-certificate",
                "-c", identity
            );
            
            if (deleteCertResult.ExitCode != 0)
            {
                throw new InvalidOperationException($"Failed to delete certificate: {deleteCertResult.Error}");
            }
        }

        _logger.LogInformation($"Certificate deleted successfully: {identity}");
    }

    /// <summary>
    /// Matches a local identity to an API certificate by finding common attributes
    /// </summary>
    public LocalSigningIdentity? FindMatchingIdentity(
        IReadOnlyList<LocalSigningIdentity> localIdentities,
        AppleCertificate apiCertificate)
    {
        // Try to match by serial number first (most reliable)
        if (!string.IsNullOrEmpty(apiCertificate.SerialNumber))
        {
            var bySerial = localIdentities.FirstOrDefault(i =>
                i.SerialNumber?.Equals(apiCertificate.SerialNumber, StringComparison.OrdinalIgnoreCase) == true);
            
            if (bySerial != null)
                return bySerial;
        }

        // Fall back to name matching (less reliable but useful)
        var byName = localIdentities.FirstOrDefault(i =>
            i.CommonName.Contains(apiCertificate.Name, StringComparison.OrdinalIgnoreCase) ||
            apiCertificate.Name.Contains(i.CommonName, StringComparison.OrdinalIgnoreCase));

        return byName;
    }

    private LocalSigningIdentity? ParseIdentityLine(string line)
    {
        // Example lines:
        // 1) ABC123... "Apple Development: John Doe (TEAMID)"
        // 2) DEF456... "Developer ID Application: Company (TEAMID)" (CSSMERR_TP_CERT_EXPIRED)
        
        var match = IdentityLineRegex().Match(line);
        if (!match.Success)
            return null;

        var hash = match.Groups["hash"].Value;
        var identityString = match.Groups["identity"].Value;
        var isValid = !line.Contains("CSSMERR_TP_CERT_EXPIRED") && 
                      !line.Contains("CSSMERR_TP_CERT_REVOKED") &&
                      !line.Contains("CSSMERR_TP_NOT_TRUSTED");

        // Extract team ID from identity string
        var teamIdMatch = TeamIdRegex().Match(identityString);
        var teamId = teamIdMatch.Success ? teamIdMatch.Groups[1].Value : null;

        // Extract common name (everything before the team ID part)
        var commonName = identityString;
        if (teamIdMatch.Success)
        {
            var parenIndex = identityString.LastIndexOf('(');
            if (parenIndex > 0)
                commonName = identityString.Substring(0, parenIndex).Trim();
        }

        return new LocalSigningIdentity(
            Identity: identityString,
            CommonName: commonName,
            TeamId: teamId,
            SerialNumber: null, // Will be populated by GetCertificateSerialNumber
            ExpirationDate: null,
            IsValid: isValid,
            Hash: hash // Store the hash for later serial number lookup
        );
    }
    
    /// <summary>
    /// Gets the serial number for a certificate using its SHA-1 hash.
    /// Uses persistent cache to avoid repeated openssl calls.
    /// </summary>
    private async Task<string?> GetCertificateSerialNumberAsync(string hash)
    {
        // Check persistent cache first
        await LoadSerialCacheAsync();
        if (_serialNumberCache!.TryGetValue(hash.ToUpperInvariant(), out var cachedSerial))
        {
            return cachedSerial;
        }
        
        try
        {
            var result = await RunSecurityCommandAsync("find-certificate", "-a", "-Z", "-p");
            if (result.ExitCode != 0)
                return null;
            
            // The output contains certificate info blocks. Find the one matching our hash.
            var lines = result.Output.Split('\n');
            var foundHash = false;
            var certPem = new System.Text.StringBuilder();
            var inCert = false;
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                
                if (line.StartsWith("SHA-1 hash:", StringComparison.OrdinalIgnoreCase))
                {
                    var certHash = line.Substring("SHA-1 hash:".Length).Trim();
                    foundHash = certHash.Equals(hash, StringComparison.OrdinalIgnoreCase);
                    certPem.Clear();
                    inCert = false;
                }
                else if (foundHash && line.Contains("BEGIN CERTIFICATE"))
                {
                    inCert = true;
                    certPem.AppendLine(line);
                }
                else if (inCert)
                {
                    certPem.AppendLine(line);
                    if (line.Contains("END CERTIFICATE"))
                    {
                        // We have the full certificate - extract serial using openssl
                        var serial = await ExtractSerialFromPemAsync(certPem.ToString());
                        
                        // Cache the result persistently
                        if (!string.IsNullOrEmpty(serial))
                        {
                            _serialNumberCache[hash.ToUpperInvariant()] = serial;
                            await SaveSerialCacheAsync();
                        }
                        
                        return serial;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to get certificate serial number: {ex.Message}");
        }
        
        return null;
    }
    
    private async Task LoadSerialCacheAsync()
    {
        if (_serialNumberCache != null)
            return;
        
        try
        {
            if (File.Exists(_serialCachePath))
            {
                var json = await File.ReadAllTextAsync(_serialCachePath);
                _serialNumberCache = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to load serial cache: {ex.Message}");
        }
        
        _serialNumberCache = new();
    }
    
    private async Task SaveSerialCacheAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_serialNumberCache, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_serialCachePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to save serial cache: {ex.Message}");
        }
    }
    
    private async Task<string?> ExtractSerialFromPemAsync(string pemCert)
    {
        try
        {
            // Write PEM to temp file
            var tempFile = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(tempFile, pemCert);
                
                // Use openssl to extract serial
                var psi = new ProcessStartInfo
                {
                    FileName = "openssl",
                    ArgumentList = { "x509", "-in", tempFile, "-serial", "-noout" },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process == null)
                    return null;
                
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                if (process.ExitCode != 0)
                    return null;
                
                // Output format: serial=3561ADFB67EF2B1DF51F4B1B29299BB5
                var serialMatch = Regex.Match(output, @"serial=([A-Fa-f0-9]+)");
                if (serialMatch.Success)
                {
                    return serialMatch.Groups[1].Value.ToUpperInvariant();
                }
            }
            finally
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to extract serial from PEM: {ex.Message}");
        }
        
        return null;
    }

    private async Task<(int ExitCode, string Output, string Error)> RunSecurityCommandAsync(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "security",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi);
        if (process == null)
            return (-1, "", "Failed to start security process");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, output, error);
    }

    // Regex to parse identity lines from security find-identity output
    [GeneratedRegex(@"^\s*\d+\)\s+(?<hash>[A-F0-9]+)\s+""(?<identity>[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex IdentityLineRegex();

    // Regex to extract team ID from identity string (usually in parentheses at the end)
    [GeneratedRegex(@"\(([A-Z0-9]{10})\)\s*$")]
    private static partial Regex TeamIdRegex();
}
