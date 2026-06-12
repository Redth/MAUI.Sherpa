namespace MauiSherpa.Bundle.Models;

// ── Android (spec §3.1) ──────────────────────────────────────────────────────

public sealed class AndroidPlatform
{
    public AndroidSetup? Setup { get; init; }
    public PlatformBuild? Build { get; init; }
    public List<DeployTarget>? Deploy { get; init; }
}

public sealed class AndroidSetup
{
    public List<Keystore>? Keystores { get; init; }
}

public sealed class Keystore
{
    /// <summary>Base64-encoded <c>.jks</c>/<c>.keystore</c> file.</summary>
    public string? Content { get; init; }
    public string? StorePassword { get; init; }
    public string? KeyAlias { get; init; }
    public string? KeyPassword { get; init; }
}

// ── iOS (spec §3.2) ──────────────────────────────────────────────────────────

public sealed class ApplePlatform
{
    public AppleSetup? Setup { get; init; }
    public PlatformBuild? Build { get; init; }
    public List<DeployTarget>? Deploy { get; init; }
}

public sealed class AppleSetup
{
    /// <summary>Provisioning profiles — array to support multi-target apps (main + widgets/extensions).</summary>
    public List<ProfileRef>? Profiles { get; init; }
    public List<CertificateRef>? Certificates { get; init; }
}

public sealed class ProfileRef
{
    /// <summary>Base64-encoded <c>.mobileprovision</c>.</summary>
    public string? Content { get; init; }
}

public sealed class CertificateRef
{
    /// <summary>Base64-encoded <c>.p12</c>.</summary>
    public string? Content { get; init; }
    public string? Password { get; init; }
}

// ── MacOS / MacCatalyst (spec §3.3, flat layout) ─────────────────────────────

public sealed class MacPlatform
{
    /// <summary>Base64-encoded <c>.provisionprofile</c>.</summary>
    public string? ProvisioningProfile { get; init; }
    /// <summary>Base64-encoded <c>.p12</c>.</summary>
    public string? Certificate { get; init; }
    public string? CertificatePassword { get; init; }
    public Dictionary<string, string>? Variables { get; init; }
}

// ── Windows (spec §3.4, flat layout) ─────────────────────────────────────────

public sealed class WindowsPlatform
{
    /// <summary>Base64-encoded <c>.pfx</c>.</summary>
    public string? Certificate { get; init; }
    public string? CertificatePassword { get; init; }
    public Dictionary<string, string>? Variables { get; init; }
}

// ── Shared (Android/iOS Build block) ─────────────────────────────────────────

public sealed class PlatformBuild
{
    public Dictionary<string, string>? MSBuildProperties { get; init; }
    public Dictionary<string, string>? ReplaceTokens { get; init; }
}
