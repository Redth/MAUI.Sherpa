using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Service for managing Apple root and intermediate certificates on macOS.
/// Downloads and caches certificates to extract serial numbers for accurate detection.
/// Caching starts automatically on construction and runs in the background.
/// </summary>
public class AppleRootCertService : IAppleRootCertService
{
    private readonly ILoggingService _logger;
    private readonly IPlatformService _platform;
    private readonly HttpClient _httpClient;
    private readonly string _cacheDir;
    
    // Cached cert info with serial numbers
    private Dictionary<string, CachedCertInfo>? _cachedCerts;
    
    // Background caching task - started on construction, awaited when needed
    private Task<bool>? _cachingTask;
    private readonly object _cacheLock = new();

    // Apple's official certificate URLs from https://www.apple.com/certificateauthority/
    private static readonly List<AppleRootCertInfo> _availableCerts = new()
    {
        // Root Certificates
        new("Apple Inc. Root", "https://www.apple.com/appleca/AppleIncRootCertificate.cer", "Root", "Apple Inc. Root Certificate"),
        new("Apple Root CA - G2", "https://www.apple.com/certificateauthority/AppleRootCA-G2.cer", "Root", "Apple Root CA G2"),
        new("Apple Root CA - G3", "https://www.apple.com/certificateauthority/AppleRootCA-G3.cer", "Root", "Apple Root CA G3"),
        
        // Intermediate Certificates - Developer Related
        new("WWDR - G2", "https://www.apple.com/certificateauthority/AppleWWDRCAG2.cer", "Intermediate", "Worldwide Developer Relations G2 (Expires 2029)"),
        new("WWDR - G3", "https://www.apple.com/certificateauthority/AppleWWDRCAG3.cer", "Intermediate", "Worldwide Developer Relations G3 (Expires 2030)"),
        new("WWDR - G4", "https://www.apple.com/certificateauthority/AppleWWDRCAG4.cer", "Intermediate", "Worldwide Developer Relations G4 (Expires 2030)"),
        new("WWDR - G5", "https://www.apple.com/certificateauthority/AppleWWDRCAG5.cer", "Intermediate", "Worldwide Developer Relations G5 (Expires 2030)"),
        new("WWDR - G6", "https://www.apple.com/certificateauthority/AppleWWDRCAG6.cer", "Intermediate", "Worldwide Developer Relations G6 (Expires 2036)"),
        new("WWDR MP CA 1 - G1", "https://www.apple.com/certificateauthority/AppleWWDRMPCA1G1.cer", "Intermediate", "WWDR Managed Profiles CA (Expires 2038)"),
        
        // Developer ID
        new("Developer ID - G1", "https://www.apple.com/certificateauthority/DeveloperIDCA.cer", "Intermediate", "Developer ID G1 (Expires 2027)"),
        new("Developer ID - G2", "https://www.apple.com/certificateauthority/DeveloperIDG2CA.cer", "Intermediate", "Developer ID G2 (Expires 2031)"),
        
        // Other Intermediates
        new("Developer Authentication", "https://www.apple.com/certificateauthority/DevAuthCA.cer", "Intermediate", "Developer Authentication CA"),
        new("Application Integration", "https://www.apple.com/certificateauthority/AppleAAICA.cer", "Intermediate", "Application Integration"),
        new("Application Integration 2", "https://www.apple.com/certificateauthority/AppleAAI2CA.cer", "Intermediate", "Application Integration 2"),
        new("Application Integration - G3", "https://www.apple.com/certificateauthority/AppleAAICAG3.cer", "Intermediate", "Application Integration G3"),
    };

    public AppleRootCertService(ILoggingService logger, IPlatformService platform)
    {
        _logger = logger;
        _platform = platform;
        _httpClient = new HttpClient();
        
        // Cache in app's local data folder
        _cacheDir = Path.Combine(AppDataPath.GetAppDataDirectory(), "AppleCerts");
        
        // Start caching immediately in the background (only on macOS)
        if (IsSupported)
        {
            StartCachingInBackground();
        }
    }

    public bool IsSupported => _platform.IsMacCatalyst;

    public IReadOnlyList<AppleRootCertInfo> GetAvailableCertificates() => _availableCerts.AsReadOnly();

    /// <summary>
    /// Starts the caching task in the background if not already running.
    /// </summary>
    private void StartCachingInBackground()
    {
        lock (_cacheLock)
        {
            if (_cachingTask == null)
            {
                _cachingTask = Task.Run(() => CacheCertsInternalAsync());
            }
        }
    }

    /// <summary>
    /// Waits for the certificate cache to be ready. Returns immediately if already cached.
    /// Call this on page load to ensure certs are available for detection.
    /// </summary>
    public async Task<bool> EnsureCertsCachedAsync(IProgress<string>? progress = null)
    {
        if (_cachedCerts != null)
            return true;

        // If caching hasn't started, start it now
        lock (_cacheLock)
        {
            if (_cachingTask == null)
            {
                _cachingTask = CacheCertsInternalAsync(progress);
                return _cachingTask.Result;
            }
        }

        // If a progress reporter is provided and caching is in progress, report waiting
        progress?.Report("Waiting for certificate cache...");
        
        // Await the existing task
        return await _cachingTask;
    }

    /// <summary>
    /// Internal method that does the actual caching work.
    /// </summary>
    private async Task<bool> CacheCertsInternalAsync(IProgress<string>? progress = null)
    {
        try
        {
            Directory.CreateDirectory(_cacheDir);
            var cached = new Dictionary<string, CachedCertInfo>(StringComparer.OrdinalIgnoreCase);

            var total = _availableCerts.Count;
            var current = 0;

            foreach (var cert in _availableCerts)
            {
                current++;
                var fileName = Path.GetFileName(new Uri(cert.Url).LocalPath);
                var cachePath = Path.Combine(_cacheDir, fileName);

                try
                {
                    // Download if not cached
                    if (!File.Exists(cachePath))
                    {
                        progress?.Report($"Downloading {cert.Name} ({current}/{total})...");
                        _logger.LogInformation($"Downloading certificate: {cert.Name}");
                        var data = await _httpClient.GetByteArrayAsync(cert.Url);
                        await File.WriteAllBytesAsync(cachePath, data);
                    }

                    // Extract serial number from cached cert
                    var serial = ExtractSerialNumber(cachePath);
                    var subject = ExtractSubjectName(cachePath);
                    
                    cached[cert.Name] = new CachedCertInfo(
                        cert.Name,
                        cachePath,
                        serial,
                        subject
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to cache {cert.Name}: {ex.Message}");
                }
            }

            _cachedCerts = cached;
            progress?.Report("Certificate cache ready");
            _logger.LogInformation($"Certificate cache initialized with {cached.Count} certificates");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to initialize cert cache: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    /// Gets cached certificate info including serial numbers.
    /// Returns null if cache not initialized - call EnsureCertsCachedAsync first.
    /// </summary>
    public IReadOnlyDictionary<string, CachedCertInfo>? GetCachedCerts() => _cachedCerts;

    /// <summary>
    /// Returns true if the cache is ready (all certs downloaded and parsed).
    /// </summary>
    public bool IsCacheReady => _cachedCerts != null;

    private string ExtractSerialNumber(string certPath)
    {
        try
        {
            var cert = new X509Certificate2(certPath);
            return cert.SerialNumber;
        }
        catch
        {
            return "";
        }
    }

    private string ExtractSubjectName(string certPath)
    {
        try
        {
            var cert = new X509Certificate2(certPath);
            return cert.Subject;
        }
        catch
        {
            return "";
        }
    }

    public async Task<IReadOnlyList<InstalledCertInfo>> GetInstalledAppleCertsAsync()
    {
        if (!IsSupported)
            return Array.Empty<InstalledCertInfo>();

        var results = new List<InstalledCertInfo>();
        var seenCerts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var loginKeychain = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Keychains/login.keychain-db");
            var searchPatterns = new[] { "Apple", "WWDR", "Developer ID", "Worldwide Developer" };
            
            foreach (var pattern in searchPatterns)
            {
                if (File.Exists(loginKeychain))
                {
                    var output = await RunSecurityCommandAsync("find-certificate", "-a", "-c", pattern, loginKeychain);
                    ExtractCertificates(output, results, seenCerts);
                }
                
                var output2 = await RunSecurityCommandAsync("find-certificate", "-a", "-c", pattern, "/Library/Keychains/System.keychain");
                ExtractCertificates(output2, results, seenCerts);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to get installed certificates: {ex.Message}", ex);
        }

        return results.OrderBy(r => r.SubjectName).ToList();
    }

    private void ExtractCertificates(string output, List<InstalledCertInfo> results, HashSet<string> seenCerts)
    {
        var certBlocks = output.Split(new[] { "keychain:" }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var block in certBlocks)
        {
            var nameMatch = Regex.Match(block, "\"labl\"<blob>=\"([^\"]+)\"");
            var serialMatch = Regex.Match(block, "\"snbr\"<blob>=0x([0-9A-Fa-f]+)");
            
            if (!nameMatch.Success) continue;
            
            var name = nameMatch.Groups[1].Value;
            var serial = serialMatch.Success ? serialMatch.Groups[1].Value.ToUpperInvariant() : "";
            
            // Filter to only CA/Authority certs
            if (!name.Contains("CA") && !name.Contains("Authority") && !name.Contains("Root") && 
                !(name.Contains("Apple") && !name.Contains("Development:") && !name.Contains("Distribution:")))
                continue;
            
            var key = $"{name}|{serial}";
            if (seenCerts.Contains(key)) continue;
            seenCerts.Add(key);
            
            // InstalledCertInfo(SubjectName, IssuerName, SerialNumber, ExpirationDate, IsAppleCert)
            results.Add(new InstalledCertInfo(name, "", serial, null, true));
        }
    }

    /// <summary>
    /// Checks if a certificate is installed using the cached serial number for precise matching.
    /// </summary>
    public async Task<bool> IsCertificateInstalledAsync(string certName)
    {
        if (!IsSupported)
            return false;

        // Get installed serial numbers
        var installed = await GetInstalledAppleCertsAsync();
        var installedSerials = installed
            .Where(c => !string.IsNullOrEmpty(c.SerialNumber))
            .Select(c => c.SerialNumber!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // If we have cached cert info with serial, use that for precise matching
        if (_cachedCerts != null && _cachedCerts.TryGetValue(certName, out var cached) && !string.IsNullOrEmpty(cached.SerialNumber))
        {
            return installedSerials.Contains(cached.SerialNumber);
        }

        // Fall back to name matching
        var installedNames = installed.Select(c => c.SubjectName.ToLowerInvariant()).ToHashSet();
        return installedNames.Any(n => n.Contains(certName.ToLowerInvariant()));
    }

    public async Task<bool> InstallCertificateAsync(AppleRootCertInfo cert, IProgress<string>? progress = null)
    {
        if (!IsSupported)
        {
            _logger.LogError("Certificate installation is only supported on macOS");
            return false;
        }

        try
        {
            // Use cached cert if available, otherwise download
            string certPath;
            if (_cachedCerts != null && _cachedCerts.TryGetValue(cert.Name, out var cached) && File.Exists(cached.FilePath))
            {
                certPath = cached.FilePath;
                progress?.Report($"Using cached {cert.Name}...");
            }
            else
            {
                progress?.Report($"Downloading {cert.Name}...");
                var certData = await _httpClient.GetByteArrayAsync(cert.Url);
                certPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.cer");
                await File.WriteAllBytesAsync(certPath, certData);
            }

            progress?.Report($"Installing {cert.Name} to keychain...");
            
            var loginKeychain = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
                "Library/Keychains/login.keychain-db");

            var output = await RunSecurityCommandAsync("add-certificates", "-k", loginKeychain, certPath);
            
            if (output.Contains("Error") && !output.Contains("already exists"))
            {
                _logger.LogError($"Failed to install certificate: {output}");
                progress?.Report($"Failed: {output}");
                return false;
            }

            progress?.Report($"Successfully installed {cert.Name}");
            _logger.LogInformation($"Installed certificate: {cert.Name}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to install certificate {cert.Name}: {ex.Message}", ex);
            progress?.Report($"Error: {ex.Message}");
            return false;
        }
    }

    private async Task<string> RunSecurityCommandAsync(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/usr/bin/security",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        if (process == null)
            return "";

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return string.IsNullOrEmpty(error) ? output : $"{output}\n{error}";
    }
}

/// <summary>
/// Cached certificate info with extracted metadata.
/// </summary>
public record CachedCertInfo(
    string Name,
    string FilePath,
    string SerialNumber,
    string SubjectName
);
