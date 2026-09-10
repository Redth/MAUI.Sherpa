using Shiny.Mediator;
using Shiny.Mediator.Caching;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Apple;

namespace MauiSherpa.Core.Handlers.Apple;

/// <summary>
/// Handler for GetCertificatesRequest with 5 minute caching and offline support
/// </summary>
public partial class GetCertificatesHandler : IRequestHandler<GetCertificatesRequest, IReadOnlyList<AppleCertificate>>
{
    internal const string DeveloperIdInstallerType = "DEVELOPER_ID_INSTALLER";
    private const string DeveloperIdInstallerPrefix = "Developer ID Installer:";

    private readonly IAppleConnectService _appleService;
    private readonly ILocalCertificateService _localCertificates;
    private readonly ILoggingService _logger;

    public GetCertificatesHandler(
        IAppleConnectService appleService,
        ILocalCertificateService localCertificates,
        ILoggingService logger)
    {
        _appleService = appleService;
        _localCertificates = localCertificates;
        _logger = logger;
    }

    [Cache(AbsoluteExpirationSeconds = 300)] // 5 min cache
    [OfflineAvailable]
    public async Task<IReadOnlyList<AppleCertificate>> Handle(
        GetCertificatesRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        var certificates = await _appleService.GetCertificatesAsync();
        return await AddKeychainOnlyInstallerCertificatesAsync(certificates);
    }

    /// <summary>
    /// App Store Connect does not return Developer ID Installer certificates from
    /// /v1/certificates, so the only place they exist is the local keychain. Without this
    /// they are invisible everywhere in the app, including the installer certificate
    /// picker on publish profiles.
    /// </summary>
    async Task<IReadOnlyList<AppleCertificate>> AddKeychainOnlyInstallerCertificatesAsync(
        IReadOnlyList<AppleCertificate> certificates)
    {
        if (!_localCertificates.IsSupported)
            return certificates;

        try
        {
            var identities = await _localCertificates.GetSigningIdentitiesAsync();
            var knownSerials = certificates
                .Select(certificate => NormalizeSerial(certificate.SerialNumber))
                .Where(serial => serial.Length > 0)
                .ToHashSet(StringComparer.Ordinal);

            var installerCertificates = identities
                .Where(IsDeveloperIdInstaller)
                .Where(identity => !string.IsNullOrWhiteSpace(identity.SerialNumber))
                .GroupBy(identity => NormalizeSerial(identity.SerialNumber), StringComparer.Ordinal)
                .Where(group => group.Key.Length > 0 && !knownSerials.Contains(group.Key))
                .Select(group => ToCertificate(group.First()))
                .ToList();

            if (installerCertificates.Count == 0)
                return certificates;

            _logger.LogInformation(
                $"Added {installerCertificates.Count} keychain-only Developer ID Installer certificate(s)");
            return certificates.Concat(installerCertificates).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Could not read Developer ID Installer certificates from the keychain: {ex.Message}");
            return certificates;
        }
    }

    static bool IsDeveloperIdInstaller(LocalSigningIdentity identity) =>
        identity.CommonName.StartsWith(DeveloperIdInstallerPrefix, StringComparison.OrdinalIgnoreCase) ||
        identity.Identity.Contains(DeveloperIdInstallerPrefix, StringComparison.OrdinalIgnoreCase);

    static AppleCertificate ToCertificate(LocalSigningIdentity identity) => new(
        Id: $"keychain:{identity.SerialNumber}",
        Name: identity.CommonName,
        CertificateType: DeveloperIdInstallerType,
        Platform: "MAC_OS",
        ExpirationDate: identity.ExpirationDate ?? DateTime.UtcNow.AddYears(1),
        SerialNumber: identity.SerialNumber ?? "")
    {
        IsLocalOnly = true
    };

    static string NormalizeSerial(string? serialNumber) =>
        new string((serialNumber ?? "")
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray())
        .TrimStart('0');
}
