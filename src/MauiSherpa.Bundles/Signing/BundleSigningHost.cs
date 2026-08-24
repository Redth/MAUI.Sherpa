namespace MauiSherpa.Bundles;

/// <summary>
/// Swappable OS/filesystem entry points used by <see cref="BundleSigningSession"/>, so tests can
/// fully control platform detection and the provisioning profile install location without touching
/// the real host OS or the developer's actual Apple provisioning profile store.
/// </summary>
internal sealed class BundleSigningHost
{
    public Func<bool> IsMacOS { get; init; } = OperatingSystem.IsMacOS;

    public Func<string> GetProvisioningProfilesDirectory { get; init; } = DefaultGetProvisioningProfilesDirectory;

    private static string DefaultGetProvisioningProfilesDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "Developer",
            "Xcode",
            "UserData",
            "Provisioning Profiles");
}
