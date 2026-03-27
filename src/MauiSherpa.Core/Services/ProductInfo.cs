namespace MauiSherpa.Core.Services;

public enum ProductEdition
{
    MauiSherpa,
    MobileSherpa
}

public sealed record ProductCapabilities(
    bool HasMauiDoctor,
    bool HasMobileDoctor,
    bool HasAppInspector,
    bool HasProfiling,
    bool HasAndroidTooling,
    bool HasAppleTooling,
    bool HasSecretsPublishing,
    bool HasPushTesting,
    bool HasCopilot
);

public static class ProductInfo
{
    public static ProductEdition Edition =>
#if MOBILE_SHERPA
        ProductEdition.MobileSherpa;
#else
        ProductEdition.MauiSherpa;
#endif

    public static bool IsMobileSherpa => Edition == ProductEdition.MobileSherpa;

    public static string ApplicationTitle => IsMobileSherpa ? "Mobile Sherpa" : "MAUI Sherpa";

    public static string ApplicationId => IsMobileSherpa ? "codes.redth.mobilesherpa" : "codes.redth.mauisherpa";

    public static string AppDataDirectoryName => IsMobileSherpa ? "MobileSherpa" : "MauiSherpa";

    public static string DashboardWelcomeMessage => IsMobileSherpa
        ? "Let Mobile Sherpa guide your mobile development environment needs!"
        : "Let .NET MAUI Sherpa guide your development environment needs!";

    public static string DoctorRoute => IsMobileSherpa ? "/mobile-doctor" : "/doctor";

    public static string DoctorNavHref => IsMobileSherpa ? "mobile-doctor" : "doctor";

    public static string DoctorTitle => IsMobileSherpa ? "Mobile Doctor" : "Environment Doctor";

    public static string CopilotAssistantTitle => IsMobileSherpa ? "Mobile Sherpa Assistant" : "MAUI Sherpa Assistant";

    public static string CopilotAssistantDescription => IsMobileSherpa
        ? "an expert assistant for mobile app development."
        : "an expert assistant for .NET MAUI mobile app development.";

    public static ProductCapabilities Capabilities { get; } = CreateCapabilities();

    private static ProductCapabilities CreateCapabilities() => IsMobileSherpa
        ? new ProductCapabilities(
            HasMauiDoctor: false,
            HasMobileDoctor: true,
            HasAppInspector: false,
            HasProfiling: false,
            HasAndroidTooling: true,
            HasAppleTooling: true,
            HasSecretsPublishing: true,
            HasPushTesting: true,
            HasCopilot: true)
        : new ProductCapabilities(
            HasMauiDoctor: true,
            HasMobileDoctor: false,
            HasAppInspector: true,
            HasProfiling: true,
            HasAndroidTooling: true,
            HasAppleTooling: true,
            HasSecretsPublishing: true,
            HasPushTesting: true,
            HasCopilot: true);
}
