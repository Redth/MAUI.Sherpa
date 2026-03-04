using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Pages.Modals;

/// <summary>
/// View model for configuring an Apple signing/distribution entry in a publish profile.
/// Used by both PublishProfileEditorModal (as a list item) and AppleConfigWizardModal (as the wizard state).
/// </summary>
public class AppleConfigVM
{
    public string Label { get; set; } = "";
    public string? IdentityId { get; set; }
    public ApplePlatformType? Platform { get; set; }
    public AppleDistributionType? DistributionType { get; set; }
    public string? CertificateSerialNumber { get; set; }
    public string? CertificateName { get; set; }
    public string? InstallerCertSerialNumber { get; set; }
    public string? InstallerCertName { get; set; }
    public string? ProfileId { get; set; }
    public string? ProfileUuid { get; set; }
    public string? ProfileName { get; set; }
    public bool IncludeNotarization { get; set; }
    public string? NotarizationAppleIdSecretKey { get; set; }
    public string? NotarizationPasswordSecretKey { get; set; }
    public string? NotarizationTeamIdSecretKey { get; set; }
    public string? NotarizationAppleIdManualValue { get; set; }
    public string? NotarizationPasswordManualValue { get; set; }
    public string? NotarizationTeamIdManualValue { get; set; }
    public bool NotarizationAppleIdUseManual { get; set; }
    public bool NotarizationPasswordUseManual { get; set; }
    public bool NotarizationTeamIdUseManual { get; set; }
    public Dictionary<string, List<string>> KeyMappings { get; set; } = new();

    // Wizard state
    public int WizardStep { get; set; } = 1;
    public bool IsEditing { get; set; }
    public bool IsComplete { get; set; }

    public bool NeedsInstallerStep =>
        DistributionType == AppleDistributionType.Direct &&
        (Platform == ApplePlatformType.MacCatalyst || Platform == ApplePlatformType.macOS);

    public bool NeedsProfileStep =>
        !(Platform == ApplePlatformType.macOS && DistributionType == AppleDistributionType.Direct);

    public bool NeedsNotarizationStep =>
        DistributionType == AppleDistributionType.Direct;

    public int TotalSteps
    {
        get
        {
            int steps = 4; // identity + platform + distribution + signing cert
            if (NeedsInstallerStep) steps++;
            if (NeedsProfileStep) steps++;
            if (NeedsNotarizationStep) steps++;
            return steps;
        }
    }

    public string GetStepLabel(int step)
    {
        int s = 0;
        s++; if (step == s) return "Identity";
        s++; if (step == s) return "Platform";
        s++; if (step == s) return "Distribution";
        s++; if (step == s) return "Certificate";
        if (NeedsInstallerStep) { s++; if (step == s) return "Installer"; }
        if (NeedsProfileStep) { s++; if (step == s) return "Profile"; }
        if (NeedsNotarizationStep) { s++; if (step == s) return "Notarization"; }
        return "";
    }

    public string PlatformLabel => Platform switch
    {
        ApplePlatformType.iOS => "iOS",
        ApplePlatformType.MacCatalyst => "Mac Catalyst",
        ApplePlatformType.macOS => "macOS",
        _ => ""
    };

    public string DistributionLabel => DistributionType switch
    {
        AppleDistributionType.Development => "Development",
        AppleDistributionType.AdHoc => "Ad Hoc",
        AppleDistributionType.AppStore => "App Store",
        AppleDistributionType.Direct => "Direct",
        _ => ""
    };

    public List<string> GetDefaultKeys()
    {
        var keys = new List<string>();
        var platLabel = Platform switch
        {
            ApplePlatformType.iOS => "IOS",
            ApplePlatformType.MacCatalyst => "MACCATALYST",
            ApplePlatformType.macOS => "MACOS",
            _ => Label.ToUpperInvariant().Replace(' ', '_').Replace('-', '_')
        };
        var distLabel = DistributionType switch
        {
            AppleDistributionType.Development => "DEV",
            AppleDistributionType.AdHoc => "ADHOC",
            AppleDistributionType.AppStore => "APPSTORE",
            AppleDistributionType.Direct => "DIRECT",
            _ => ""
        };
        var prefix = $"APPLE_{platLabel}_{distLabel}";

        if (!string.IsNullOrEmpty(CertificateSerialNumber))
        {
            keys.Add($"{prefix}_CERTIFICATE_P12");
            keys.Add($"{prefix}_CERTIFICATE_PASSWORD");
        }
        if (!string.IsNullOrEmpty(InstallerCertSerialNumber))
        {
            keys.Add($"{prefix}_INSTALLER_P12");
            keys.Add($"{prefix}_INSTALLER_PASSWORD");
        }
        if (!string.IsNullOrEmpty(ProfileId))
            keys.Add($"{prefix}_PROFILE");
        if (IncludeNotarization)
        {
            keys.Add("APPLE_NOTARIZATION_APPLE_ID");
            keys.Add("APPLE_NOTARIZATION_PASSWORD");
            keys.Add("APPLE_NOTARIZATION_TEAM_ID");
        }

        foreach (var key in keys)
        {
            if (!KeyMappings.ContainsKey(key))
                KeyMappings[key] = new List<string> { key };
        }
        foreach (var k in KeyMappings.Keys.ToList())
        {
            if (!keys.Contains(k))
                KeyMappings.Remove(k);
        }
        return keys;
    }
}
