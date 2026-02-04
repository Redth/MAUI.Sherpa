using System.Diagnostics;
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
    
    // Cache of signing identities
    private List<LocalSigningIdentity>? _cachedIdentities;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public LocalCertificateService(ILoggingService logger, IPlatformService platform)
    {
        _logger = logger;
        _platform = platform;
    }

    public bool IsSupported => _platform.IsMacCatalyst;

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
                    identities.Add(identity);
                    _logger.LogDebug($"Found identity: {identity.CommonName} (Valid: {identity.IsValid})");
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
                "-k", "login.keychain-db"
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
            SerialNumber: null, // Would need additional query to get this
            ExpirationDate: null, // Would need additional query to get this
            IsValid: isValid
        );
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
