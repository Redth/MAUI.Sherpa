using System.Security.Cryptography.X509Certificates;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Services;

/// <summary>
/// Windows implementation of ILocalCertificateService using the Windows Certificate Store.
/// Stores certificates in CurrentUser\My (Personal) store — no admin required.
/// </summary>
public class WindowsCertificateService : ILocalCertificateService
{
    private readonly ILoggingService _logger;

    private List<LocalSigningIdentity>? _cachedIdentities;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public WindowsCertificateService(ILoggingService logger)
    {
        _logger = logger;
    }

    public bool IsSupported => OperatingSystem.IsWindows();

    public void InvalidateCache()
    {
        _cachedIdentities = null;
        _cacheExpiry = DateTime.MinValue;
        _logger.LogInformation("Windows certificate cache invalidated");
    }

    public Task<IReadOnlyList<LocalSigningIdentity>> GetSigningIdentitiesAsync()
    {
        if (!IsSupported)
            return Task.FromResult<IReadOnlyList<LocalSigningIdentity>>(Array.Empty<LocalSigningIdentity>());

        if (_cachedIdentities != null && DateTime.UtcNow < _cacheExpiry)
            return Task.FromResult<IReadOnlyList<LocalSigningIdentity>>(_cachedIdentities);

        return Task.Run<IReadOnlyList<LocalSigningIdentity>>(() =>
        {
            var identities = new List<LocalSigningIdentity>();

            try
            {
                using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadOnly);

                foreach (var cert in store.Certificates)
                {
                    try
                    {
                        if (!IsAppleCertificate(cert))
                            continue;

                        var identity = BuildIdentityString(cert);
                        var commonName = GetCommonName(cert);
                        var teamId = ExtractTeamId(cert);
                        var serial = NormalizeSerial(cert.SerialNumber);

                        identities.Add(new LocalSigningIdentity(
                            Identity: identity,
                            CommonName: commonName,
                            TeamId: teamId,
                            SerialNumber: serial,
                            ExpirationDate: cert.NotAfter,
                            IsValid: cert.HasPrivateKey && cert.NotAfter > DateTime.UtcNow,
                            Hash: cert.Thumbprint
                        ));
                    }
                    finally
                    {
                        cert.Dispose();
                    }
                }

                _cachedIdentities = identities;
                _cacheExpiry = DateTime.UtcNow + CacheDuration;
                _logger.LogInformation($"Found {identities.Count} Apple certificate(s) in Windows store");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to enumerate Windows certificate store: {ex.Message}", ex);
            }

            return identities;
        });
    }

    public Task<bool> HasPrivateKeyAsync(string serialNumber)
    {
        if (!IsSupported || string.IsNullOrEmpty(serialNumber))
            return Task.FromResult(false);

        return Task.Run(() =>
        {
            try
            {
                using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadOnly);

                var cert = FindCertBySerial(store, serialNumber);
                if (cert != null)
                {
                    var hasKey = cert.HasPrivateKey;
                    cert.Dispose();
                    return hasKey;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to check private key for {serialNumber}: {ex.Message}", ex);
            }

            return false;
        });
    }

    public Task<byte[]> ExportP12Async(string identity, string password)
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("Certificate export not supported on this platform");

        return Task.Run(() =>
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);

            var cert = FindCertByIdentity(store, identity);
            if (cert == null)
                throw new InvalidOperationException($"Certificate not found: {identity}");

            try
            {
                if (!cert.HasPrivateKey)
                    throw new InvalidOperationException($"Certificate has no private key: {identity}");

                var p12 = cert.Export(X509ContentType.Pfx, password);
                _logger.LogInformation($"Exported P12 for: {identity}");
                return p12;
            }
            finally
            {
                cert.Dispose();
            }
        });
    }

    public Task<byte[]> ExportCertificateAsync(string serialNumber)
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("Certificate export not supported on this platform");

        return Task.Run(() =>
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);

            var cert = FindCertBySerial(store, serialNumber);
            if (cert == null)
                throw new InvalidOperationException($"Certificate not found with serial: {serialNumber}");

            try
            {
                var der = cert.Export(X509ContentType.Cert);
                _logger.LogInformation($"Exported certificate (DER) for serial: {serialNumber}");
                return der;
            }
            finally
            {
                cert.Dispose();
            }
        });
    }

    public Task DeleteCertificateAsync(string identity)
    {
        if (!IsSupported)
            return Task.CompletedTask;

        return Task.Run(() =>
        {
            try
            {
                using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadWrite);

                var cert = FindCertByIdentity(store, identity);
                if (cert != null)
                {
                    store.Remove(cert);
                    cert.Dispose();
                    InvalidateCache();
                    _logger.LogInformation($"Deleted certificate: {identity}");
                }
                else
                {
                    _logger.LogWarning($"Certificate not found for deletion: {identity}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to delete certificate {identity}: {ex.Message}", ex);
            }
        });
    }

    #region Helpers

    private static bool IsAppleCertificate(X509Certificate2 cert)
    {
        var issuer = cert.Issuer;
        // Apple certificates are issued by Apple CAs
        return issuer.Contains("Apple", StringComparison.OrdinalIgnoreCase)
            && (issuer.Contains("Worldwide Developer Relations", StringComparison.OrdinalIgnoreCase)
                || issuer.Contains("Apple Root CA", StringComparison.OrdinalIgnoreCase)
                || issuer.Contains("Developer ID", StringComparison.OrdinalIgnoreCase)
                || issuer.Contains("Developer Authentication", StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildIdentityString(X509Certificate2 cert)
    {
        // Build a string similar to macOS: "Apple Development: Name (TEAMID)"
        var cn = GetCommonName(cert);
        return cn;
    }

    private static string GetCommonName(X509Certificate2 cert)
    {
        var subject = cert.Subject;
        // Parse CN= from the subject DN
        var cnStart = subject.IndexOf("CN=", StringComparison.OrdinalIgnoreCase);
        if (cnStart < 0)
            return subject;

        cnStart += 3;
        // Handle quoted CN values
        if (cnStart < subject.Length && subject[cnStart] == '"')
        {
            cnStart++;
            var cnEnd = subject.IndexOf('"', cnStart);
            return cnEnd > cnStart ? subject[cnStart..cnEnd] : subject[cnStart..];
        }
        else
        {
            var cnEnd = subject.IndexOf(',', cnStart);
            return cnEnd > cnStart ? subject[cnStart..cnEnd].Trim() : subject[cnStart..].Trim();
        }
    }

    private static string? ExtractTeamId(X509Certificate2 cert)
    {
        // Apple certs typically have OU=<TeamID> in the subject
        var subject = cert.Subject;
        var ouStart = subject.IndexOf("OU=", StringComparison.OrdinalIgnoreCase);
        if (ouStart < 0) return null;

        ouStart += 3;
        var ouEnd = subject.IndexOf(',', ouStart);
        var ou = ouEnd > ouStart ? subject[ouStart..ouEnd].Trim() : subject[ouStart..].Trim();

        // Team IDs are typically 10-char alphanumeric
        return ou.Length >= 8 && ou.Length <= 12 ? ou : null;
    }

    private static string NormalizeSerial(string serial)
    {
        // Windows returns uppercase hex, Apple API may have leading zeros stripped
        // Normalize: uppercase, strip leading zeros
        return serial.TrimStart('0').ToUpperInvariant();
    }

    private X509Certificate2? FindCertBySerial(X509Store store, string serialNumber)
    {
        var normalizedTarget = NormalizeSerial(serialNumber);
        foreach (var cert in store.Certificates)
        {
            if (NormalizeSerial(cert.SerialNumber) == normalizedTarget)
                return cert;
            cert.Dispose();
        }
        return null;
    }

    private X509Certificate2? FindCertByIdentity(X509Store store, string identity)
    {
        // Try matching by common name (identity string) or by serial number
        foreach (var cert in store.Certificates)
        {
            if (!IsAppleCertificate(cert))
            {
                cert.Dispose();
                continue;
            }

            var cn = GetCommonName(cert);
            if (cn.Equals(identity, StringComparison.OrdinalIgnoreCase)
                || cert.Thumbprint.Equals(identity, StringComparison.OrdinalIgnoreCase)
                || NormalizeSerial(cert.SerialNumber).Equals(NormalizeSerial(identity), StringComparison.OrdinalIgnoreCase))
            {
                return cert;
            }
            cert.Dispose();
        }
        return null;
    }

    #endregion
}
