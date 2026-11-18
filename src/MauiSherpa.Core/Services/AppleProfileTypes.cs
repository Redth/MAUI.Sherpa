namespace MauiSherpa.Core.Services;

/// <summary>
/// Display names and platform rules for App Store Connect provisioning profile types.
/// </summary>
public static class AppleProfileTypes
{
    public static string GetDisplayName(string? profileType) => profileType?.ToUpperInvariant() switch
    {
        "IOS_APP_DEVELOPMENT" => "iOS Development",
        "IOS_APP_ADHOC" => "iOS Ad Hoc",
        "IOS_APP_STORE" => "iOS App Store",
        "IOS_APP_INHOUSE" => "iOS In House",
        "TVOS_APP_DEVELOPMENT" => "tvOS Development",
        "TVOS_APP_ADHOC" => "tvOS Ad Hoc",
        "TVOS_APP_STORE" => "tvOS App Store",
        "TVOS_APP_INHOUSE" => "tvOS In House",
        "MAC_APP_DEVELOPMENT" => "macOS Development",
        "MAC_APP_STORE" => "Mac App Store",
        "MAC_APP_DIRECT" => "macOS Direct Distribution",
        "MAC_CATALYST_APP_DEVELOPMENT" => "Mac Catalyst Development",
        "MAC_CATALYST_APP_STORE" => "Mac Catalyst App Store",
        "MAC_CATALYST_APP_DIRECT" => "Mac Catalyst Direct",
        null or "" => "-",
        _ => profileType.Replace("_", " ")
    };

    /// <summary>
    /// The platform a profile targets. App Store Connect reports tvOS profiles under the
    /// IOS bundle ID platform, so the profile type wins over the reported platform.
    /// </summary>
    public static string GetPlatform(string? profileType, string? reportedPlatform)
    {
        var type = profileType?.ToUpperInvariant() ?? "";
        if (type.StartsWith("TVOS_"))
            return "TVOS";
        if (type.StartsWith("MAC_APP_"))
            return "MAC_OS";
        if (type.StartsWith("IOS_"))
            return "IOS";
        return reportedPlatform?.ToUpperInvariant() ?? "";
    }

    public static string GetPlatformLabel(string? platform) => platform?.ToUpperInvariant() switch
    {
        "IOS" => "iOS",
        "TVOS" or "TV_OS" => "tvOS",
        "MAC_OS" or "MACOS" => "macOS",
        "UNIVERSAL" => "Universal",
        _ => platform ?? ""
    };

    /// <summary>
    /// Whether a registered device can be included in a profile of the given type.
    /// Apple TVs register under the IOS platform, so the device class separates them from iPhones and iPads.
    /// </summary>
    public static bool IsDeviceCompatible(string? profileType, string? devicePlatform, string? deviceClass)
    {
        var type = profileType?.ToUpperInvariant() ?? "";
        var platform = devicePlatform?.ToUpperInvariant();
        var deviceKind = deviceClass?.ToUpperInvariant();

        if (type.StartsWith("TVOS_"))
            return deviceKind == "APPLE_TV";
        if (type.StartsWith("IOS_"))
            return deviceKind is "IPHONE" or "IPAD" or "IPOD" ||
                (platform == "IOS" && deviceKind is not ("APPLE_TV" or "APPLE_WATCH" or "MAC"));
        if (type.StartsWith("MAC_CATALYST_") || type.StartsWith("MAC_APP_"))
            return platform == "MAC_OS" || deviceKind == "MAC";
        return true;
    }
}
