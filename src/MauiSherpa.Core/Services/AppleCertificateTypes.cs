namespace MauiSherpa.Core.Services;

public sealed record AppleCertificateTypeOption(
    string Name,
    string Value,
    string Category,
    string Description,
    string? PortalReason = null)
{
    public bool CanCreateInApp => PortalReason is null;
    public string PickerLabel => CanCreateInApp ? Name : $"{Name} (Apple Developer)";
}

public static class AppleCertificateTypes
{
    public const string PortalUrl = "https://developer.apple.com/account/resources/certificates/add";

    private const string PortalOnly = "Apple's public API does not expose this certificate type.";
    private const string PassTypeRequired = "Requires a Pass Type ID selection, which Sherpa does not yet support in-app.";
    private const string MerchantRequired = "Requires a Merchant ID selection and a service-specific certificate request, which Sherpa does not yet support in-app.";

    public static IReadOnlyList<AppleCertificateTypeOption> All { get; } = Array.AsReadOnly<AppleCertificateTypeOption>(
    [
        new("Apple Development", "DEVELOPMENT", "Software",
            "Sign development versions of your iOS, iPadOS, macOS, tvOS, watchOS, and visionOS apps."),
        new("Apple Distribution", "DISTRIBUTION", "Software",
            "Sign apps for Ad Hoc release testing or submission to App Store Connect."),
        new("iOS App Development", "IOS_DEVELOPMENT", "Software",
            "Sign development versions of your iOS app."),
        new("iOS Distribution (App Store Connect and Ad Hoc)", "IOS_DISTRIBUTION", "Software",
            "Sign iOS, iPadOS, watchOS, and visionOS apps for App Store Connect or Ad Hoc distribution."),
        new("Mac Development", "MAC_APP_DEVELOPMENT", "Software",
            "Sign development versions of your Mac app."),
        new("Mac App Distribution", "MAC_APP_DISTRIBUTION", "Software",
            "Sign your Mac app and configure a distribution provisioning profile for the Mac App Store."),
        new("Mac Installer Distribution", "MAC_INSTALLER_DISTRIBUTION", "Software",
            "Sign your app's installer package for submission to the Mac App Store."),
        new("Developer ID Installer", "DEVELOPER_ID_INSTALLER", "Software",
            "Sign your app's installer package for distribution outside the Mac App Store.", PortalOnly),
        new("Developer ID Application", "DEVELOPER_ID_APPLICATION", "Software",
            "Sign your app for distribution outside the Mac App Store."),
        new("Apple Push Notification service SSL (Sandbox)", "APNS_DEVELOPMENT", "Services",
            "Connect your notification server to the APNs sandbox. A separate certificate is required for each app.", PortalOnly),
        new("Apple Push Notification service SSL (Sandbox & Production)", "APNS_PRODUCTION", "Services",
            "Connect your notification server to APNs sandbox and production environments for an app.", PortalOnly),
        new("Pass Type ID Certificate", "PASS_TYPE_ID", "Services",
            "Sign and send updates to passes in Wallet.", PassTypeRequired),
        new("Order Type ID Certificate", "ORDER_TYPE_ID", "Services",
            "Sign and send updates to order information in Apple Wallet.", PortalOnly),
        new("Website Push ID Certificate", "WEBSITE_PUSH_ID", "Services",
            "Sign and send website push updates.", PortalOnly),
        new("Swift Package Collection Certificate", "SWIFT_PACKAGE_COLLECTION", "Services",
            "Sign Swift package collections for distribution.", PortalOnly),
        new("Swift Package Certificate", "SWIFT_PACKAGE", "Services",
            "Sign Swift packages and package collections for Swift Package Manager 5.9 or later.", PortalOnly),
        new("WatchKit Services Certificate", "WATCHKIT_SERVICES", "Services",
            "Connect to APNs sandbox and production environments to update ClockKit complication data.", PortalOnly),
        new("VoIP Services Certificate", "VOIP_SERVICES", "Services",
            "Connect to APNs sandbox and production environments to alert background VoIP apps of incoming calls.", PortalOnly),
        new("Apple Pay Payment Processing Certificate", "APPLE_PAY", "Services",
            "Decrypt app transaction data sent by Apple to a merchant or developer.", MerchantRequired),
        new("Apple Pay Merchant Identity Certificate", "APPLE_PAY_MERCHANT_IDENTITY", "Services",
            "Authenticate to Apple Pay payment processing servers using a client TLS certificate.", MerchantRequired),
        new("Developer ID Kernel Extension", "DEVELOPER_ID_KEXT", "Software",
            "Sign kernel extensions for distribution outside the Mac App Store."),
        new("Pass Type ID with NFC", "PASS_TYPE_ID_WITH_NFC", "Services",
            "Sign NFC-enabled passes in Wallet.", PassTypeRequired),
    ]);

    public static AppleCertificateTypeOption? Find(string value) =>
        All.FirstOrDefault(option => string.Equals(option.Value, value?.Trim(), StringComparison.OrdinalIgnoreCase));
}
