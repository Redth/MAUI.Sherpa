using System.Text;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Workloads.Models;

namespace MauiSherpa.Core.Screenshots;

internal sealed class ScreenshotFixtures
{
    public const string BundleId = "invalid.example.summit.trailnotes";
    public const string TeamId = "SUMMIT0001";
    public const string LocalProviderId = "screenshot-local";
    public const string CloudProviderId = "screenshot-cloud";
    public static DateTime Now => ScreenshotMode.ReferenceDate;

    public ScreenshotFixtures(IPlatformService platform)
    {
        Home = platform.IsWindows ? @"C:\Users\Summit" : platform.IsMacOS || platform.IsMacCatalyst ? "/Users/summit" : "/home/summit";
        Architecture = platform.IsMacOS || platform.IsMacCatalyst ? "arm64" : "x64";
        SdkRoot = Path.Combine(Home, ".dotnet");
        AndroidRoot = Path.Combine(Home, "Android", "Sdk");
        JdkRoot = Path.Combine(Home, ".jdk", "microsoft-21");
        Packages =
        [
            new("platform-tools", "Android SDK Platform-Tools", "36.0.2", AndroidRoot, true),
            new("emulator", "Android Emulator", "36.3.10", AndroidRoot, true),
            new("cmdline-tools;latest", "Android SDK Command-line Tools", "19.0", AndroidRoot, true),
            new("build-tools;36.0.0", "Android SDK Build-Tools 36", "36.0.0", AndroidRoot, true),
            new("platforms;android-36", "Android SDK Platform 36", "2", AndroidRoot, true),
            new("platforms;android-35", "Android SDK Platform 35", "2", AndroidRoot, true),
            new("system-images;android-36;google_apis;arm64-v8a", "Google APIs ARM 64 v8a System Image", "8", AndroidRoot, true),
            new("sources;android-35", "Sources for Android 35", "1", AndroidRoot, true),
            new("sources;android-36", "Sources for Android 36", "1", null, false),
            new("platforms;android-34", "Android SDK Platform 34", "3", null, false),
            new("build-tools;35.0.0", "Android SDK Build-Tools 35", "35.0.0", null, false)
        ];
        Avds =
        [
            new("Trail_Notes_Pixel_9", "pixel_9", Path.Combine(Home, ".android", "avd", "Trail_Notes_Pixel_9.avd"),
                "Google APIs", "Android 16", new() { ["hw.device.name"] = "pixel_9", ["hw.ramSize"] = "4096", ["image.sysdir.1"] = "system-images/android-36/google_apis/arm64-v8a/", ["skin.name"] = "pixel_9", ["disk.dataPartition.size"] = "8G" }),
            new("Trail_Notes_Tablet", "pixel_tablet", Path.Combine(Home, ".android", "avd", "Trail_Notes_Tablet.avd"),
                "Google APIs", "Android 15", new() { ["hw.device.name"] = "pixel_tablet", ["hw.ramSize"] = "4096", ["image.sysdir.1"] = "system-images/android-35/google_apis/arm64-v8a/" })
        ];
        DotnetList = new()
        {
            InstallSpecs =
            [
                new() { Component = DotnetUpComponent.Sdk, ComponentRaw = "SDK", VersionOrChannel = "10.0.4xx", InstallRoot = SdkRoot, Architecture = Architecture },
                new() { Component = DotnetUpComponent.Sdk, ComponentRaw = "SDK", VersionOrChannel = "9.0.3xx", InstallRoot = SdkRoot, Architecture = Architecture },
                new() { Component = DotnetUpComponent.Runtime, ComponentRaw = "Runtime", VersionOrChannel = "10.0", InstallRoot = SdkRoot, Architecture = Architecture }
            ],
            Installations =
            [
                new() { Component = DotnetUpComponent.Sdk, ComponentRaw = "SDK", Version = "10.0.401", InstallRoot = SdkRoot, Architecture = Architecture },
                new() { Component = DotnetUpComponent.Sdk, ComponentRaw = "SDK", Version = "9.0.311", InstallRoot = SdkRoot, Architecture = Architecture },
                new() { Component = DotnetUpComponent.Runtime, ComponentRaw = "Runtime", Version = "10.0.5", InstallRoot = SdkRoot, Architecture = Architecture, FrameworkName = "Microsoft.NETCore.App" },
                new() { Component = DotnetUpComponent.AspNetCore, ComponentRaw = "ASPNETCore", Version = "10.0.5", InstallRoot = SdkRoot, Architecture = Architecture, FrameworkName = "Microsoft.AspNetCore.App" }
            ]
        };
        DoctorContext = new(Path.Combine(Home, "Projects", "TrailNotes"), Path.Combine(SdkRoot, "sdk", "10.0.401"),
            Path.Combine(Home, "Projects", "TrailNotes", "global.json"), "10.0.401", "10.0.400", "10.0.400",
            ActiveSdkVersion: "10.0.401", RollForwardPolicy: "latestFeature", ResolvedSdkVersion: "10.0.401",
            DotnetUpInstalled: true, DotnetUpVersion: "0.5.0", DotnetUpManagedInstallRoot: SdkRoot,
            DotNetArchitecture: Architecture, UsesDotnetUpManagedSdk: true);
        var dependencies = new List<DependencyStatus>
        {
            new(".NET SDK", DependencyCategory.DotNetSdk, "10.0", "10.0.401", "10.0.401", DependencyStatusType.Ok, "Ready for .NET MAUI development", false),
            new(".NET MAUI", DependencyCategory.Workload, "10.0", "10.0.60", "10.0.60", DependencyStatusType.Ok, "MAUI workloads installed", false),
            new("JDK", DependencyCategory.Jdk, "21", "21.0.8", "21.0.8", DependencyStatusType.Ok, "Microsoft Build of OpenJDK", false),
            new("Android SDK", DependencyCategory.AndroidSdk, null, null, AndroidRoot, DependencyStatusType.Ok, "SDK location configured", false),
            new("Android Platform", DependencyCategory.AndroidSdk, "36", "36", "36", DependencyStatusType.Ok, "Android 16", false),
            new("Build Tools", DependencyCategory.AndroidSdk, "36.0.0", "36.0.0", "36.0.0", DependencyStatusType.Ok, "Android build tools are ready", false),
            new("Android SDK Sources", DependencyCategory.AndroidSdk, null, "36", "35", DependencyStatusType.Warning, "Newer sources are available for source-level debugging", true, "install-android-sources")
        };
        if (platform.IsMacOS || platform.IsMacCatalyst)
            dependencies.Add(new("Xcode", DependencyCategory.Xcode, "26.0", "26.3", "26.3", DependencyStatusType.Ok, "Selected developer tools are ready", false));
        if (platform.IsWindows)
        {
            dependencies.Add(new("Windows App SDK", DependencyCategory.WindowsAppSdk, "1.8", "1.8", "1.8", DependencyStatusType.Ok, "Windows development is ready", false));
            dependencies.Add(new("WebView2", DependencyCategory.WebView2, null, null, "142.0", DependencyStatusType.Ok, "Evergreen Runtime installed", false));
        }
        Doctor = new(DoctorContext, [new("10.0.401", "10.0.400", 10, 0, false), new("9.0.311", "9.0.300", 9, 0, false)],
            [new("10.0.402", "10.0.400", 10, 0, false)], "10.0.400", ["10.0.400", "10.0.401"],
            [new("microsoft.net.sdk.maui", "10.0.60", ".NET MAUI", 5, 24), new("microsoft.net.sdk.android", "36.0.50", "Android", 1, 8)],
            dependencies, Now);
    }

    public string Home { get; }
    public string Architecture { get; }
    public string SdkRoot { get; }
    public string AndroidRoot { get; }
    public string JdkRoot { get; }
    public IReadOnlyList<SdkPackageInfo> Packages { get; }
    public IReadOnlyList<AvdInfo> Avds { get; }
    public DotnetUpListResult DotnetList { get; }
    public DoctorContext DoctorContext { get; }
    public DoctorReport Doctor { get; }

    public IReadOnlyList<AppleIdentity> AppleIdentities { get; } =
    [
        new("summit-apple", "Summit Labs", "SUMMITKEY1", "11111111-2222-4333-8444-555555555555", null, "SCREENSHOT DATA - NOT A SIGNING KEY"),
        new("summit-sandbox", "Summit Labs Sandbox", "SUMMITKEY2", "66666666-7777-4888-8999-000000000000", null, "SCREENSHOT DATA - NOT A SIGNING KEY")
    ];
    public IReadOnlyList<GoogleIdentity> GoogleIdentities { get; } =
    [
        new("summit-google", "Summit Labs", "trail-notes-example", "notifications@trail-notes.example.invalid", null,
            ServiceAccountJson: """{"type":"service_account","project_id":"trail-notes-example","client_email":"notifications@trail-notes.example.invalid","private_key":"SCREENSHOT DATA - NOT A PRIVATE KEY"}""")
    ];
    public IReadOnlyList<DeviceInfo> AndroidDevices { get; } =
    [
        new("SUMMIT-PIXEL-0001", "device", "Pixel 9", false),
        new("emulator-5554", "device", "Trail Notes Pixel 9", true)
    ];
    public IReadOnlyList<AppleBundleId> BundleIds { get; } =
    [
        new("bundle-trailnotes", BundleId, "Trail Notes", "IOS", TeamId,
            [new("cap-push", "PUSH_NOTIFICATIONS"), new("cap-signin", "SIGN_IN_WITH_APPLE"), new("cap-domains", "ASSOCIATED_DOMAINS")]),
        new("bundle-widget", $"{BundleId}.widget", "Trail Notes Widget", "IOS", TeamId, [new("cap-groups", "APP_GROUPS")]),
        new("bundle-macos", $"{BundleId}.desktop", "Trail Notes Desktop", "MAC_OS", TeamId, [new("cap-cloud", "ICLOUD")]),
        new("bundle-compass", "invalid.example.summit.compass", "Summit Compass", "UNIVERSAL", TeamId)
    ];
    public IReadOnlyList<AppleDevice> AppleDevices { get; } =
    [
        new("apple-phone", "00008130-0011223344550001", "Trail Notes iPhone", "IOS", "IPHONE", "ENABLED", "iPhone 17 Pro"),
        new("apple-tablet", "00008120-0011223344550002", "Summit iPad", "IOS", "IPAD", "ENABLED", "iPad Pro"),
        new("apple-mac", "11111111-2222-4333-8444-555555555556", "Summit Mac", "MAC_OS", "MAC", "ENABLED", "MacBook Pro")
    ];
    public IReadOnlyList<AppleCertificate> Certificates { get; } =
    [
        new("cert-development", "Apple Development: Summit Labs", "DEVELOPMENT", "IOS", Now.AddDays(240), "00112233445566778899AABBCCDDEEFF"),
        new("cert-distribution", "Apple Distribution: Summit Labs", "DISTRIBUTION", "IOS", Now.AddDays(300), "102132435465768798A9BACBDCEDFE0F"),
        new("cert-developer-id", "Developer ID Application: Summit Labs", "DEVELOPER_ID_APPLICATION", "MAC_OS", Now.AddDays(540), "2031425364758697A8B9CADBECFD0E1F"),
        new("cert-expiring", "Apple Development: Summit Sandbox", "DEVELOPMENT", "IOS", Now.AddDays(21), "30415263748596A7B8C9DAEBFC0D1E2F")
    ];
    public IReadOnlyList<AppleProfile> Profiles { get; } =
    [
        new("profile-development", "Trail Notes Development", "IOS_APP_DEVELOPMENT", "IOS", "ACTIVE", Now.AddDays(240), BundleId,
            "11111111-1111-4111-8111-111111111111", ["cert-development"]),
        new("profile-release", "Trail Notes App Store", "IOS_APP_STORE", "IOS", "ACTIVE", Now.AddDays(300), BundleId,
            "22222222-2222-4222-8222-222222222222", ["cert-distribution"]),
        new("profile-adhoc", "Trail Notes QA", "IOS_APP_ADHOC", "IOS", "ACTIVE", Now.AddDays(180), BundleId,
            "33333333-3333-4333-8333-333333333333", ["cert-distribution"]),
        new("profile-widget", "Trail Notes Widget", "IOS_APP_STORE", "IOS", "ACTIVE", Now.AddDays(300), $"{BundleId}.widget",
            "44444444-4444-4444-8444-444444444444", ["cert-distribution"])
    ];
    public IReadOnlyList<AndroidKeystore> Keystores { get; } =
    [
        new("keystore-release", "trail-notes-release", "trail-notes-release.p12", "PKCS12", Now.AddMonths(-8), "Trail Notes production signing", true),
        new("keystore-qa", "trail-notes-qa", "trail-notes-qa.p12", "PKCS12", Now.AddMonths(-3), "Internal testing and nightly builds", true),
        new("keystore-compass", "summit-compass", "summit-compass.p12", "PKCS12", Now.AddMonths(-6), "Summit Compass release signing", true)
    ];
    public IReadOnlyList<CloudSecretsProviderConfig> Providers { get; } =
    [
        new(LocalProviderId, "Local Vault", CloudSecretsProviderType.Local, []),
        new(CloudProviderId, "Summit Team Vault", CloudSecretsProviderType.AzureKeyVault, new() { ["VaultUri"] = "https://summit.example.invalid" })
    ];
    public IReadOnlyList<ManagedSecret> Secrets { get; } =
    [
        new("API_BASE_URL", ManagedSecretType.String, "Trail Notes API endpoint", null, Now.AddMonths(-4), Now.AddDays(-2)),
        new("MAPS_API_KEY", ManagedSecretType.String, "Map tiles for trail previews", null, Now.AddMonths(-3), Now.AddDays(-7)),
        new("SENTRY_DSN", ManagedSecretType.String, "Application diagnostics", null, Now.AddMonths(-4), Now.AddDays(-14)),
        new("NOTARIZATION_PASSWORD", ManagedSecretType.String, "Example app-specific password", null, Now.AddMonths(-2), Now.AddDays(-9)),
        new("Production/DEPLOY_TOKEN", ManagedSecretType.String, "CI deployment token", null, Now.AddMonths(-2), Now.AddDays(-3)),
        new("Production/google-services.json", ManagedSecretType.File, "Firebase client configuration", "google-services.json", Now.AddMonths(-2), Now.AddDays(-3))
    ];
    public byte[]? GetSecretValue(string key) => Secrets.Any(s => s.Key == key)
        ? Encoding.UTF8.GetBytes(key == "API_BASE_URL" ? "https://api.trail-notes.example.invalid" : $"screenshot-only-{key.ToLowerInvariant()}")
        : null;
    public IReadOnlyList<SecretsPublisherConfig> Publishers { get; } =
    [
        new("publisher-github", "github", "Summit GitHub Actions", new() { ["Token"] = "SCREENSHOT-ONLY-NOT-A-TOKEN" })
    ];
    public IReadOnlyList<PublishProfile> PublishProfiles { get; } =
    [
        new("publish-release", "Trail Notes Release", "App Store and Google Play signing for the release pipeline",
            "publisher-github", "summit-labs/trail-notes", "summit-labs/trail-notes",
            [new("iOS App Store", "summit-apple", ApplePlatformType.iOS, AppleDistributionType.AppStore,
                "102132435465768798A9BACBDCEDFE0F", null, "profile-release", "22222222-2222-4222-8222-222222222222",
                false, null, null, null, null, null, null, new() { ["certificate"] = ["IOS_CERTIFICATE"] })],
            [new("Google Play", "keystore-release", new() { ["keystore"] = ["ANDROID_KEYSTORE"] })],
            [new("API_BASE_URL", ["API_BASE_URL"]), new("MAPS_API_KEY", ["MAPS_API_KEY"])], Now.AddMonths(-4), Now.AddDays(-2))
        {
            Publishers = [new("publisher-github", "Summit GitHub Actions", "github", "summit-labs/trail-notes", "summit-labs/trail-notes")],
            AppleIdentities = [new("App Store Connect", "summit-apple", new() { ["key"] = ["APPSTORE_API_KEY"] })],
            GoogleIdentities = [new("Firebase", "summit-google", new() { ["json"] = ["GOOGLE_SERVICE_ACCOUNT"] })]
        },
        new("publish-qa", "Trail Notes Nightly", "Internal testing builds for the Summit team",
            "publisher-github", "summit-labs/trail-notes", "summit-labs/trail-notes", [],
            [new("Android QA", "keystore-qa", new() { ["keystore"] = ["QA_KEYSTORE"] })],
            [new("API_BASE_URL", ["API_BASE_URL"])], Now.AddMonths(-2), Now.AddDays(-1))
        {
            Publishers = [new("publisher-github", "Summit GitHub Actions", "github", "summit-labs/trail-notes", "summit-labs/trail-notes")]
        }
    ];
    public IReadOnlyList<PushProject> PushProjects { get; } =
    [
        new()
        {
            Id = "push-apns", Name = "Trail Notes iOS", Description = "Trail reminders and shared adventures",
            Platform = PushProjectPlatform.Apns, CreatedAt = Now.AddMonths(-2), LastModified = Now.AddDays(-1),
            ApnsConfig = new()
            {
                SelectedIdentityId = "summit-apple", TeamId = TeamId, BundleId = BundleId,
                DeviceToken = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff",
                JsonPayload = "{\n  \"aps\": {\n    \"alert\": {\n      \"title\": \"The trail is calling\",\n      \"body\": \"Your next adventure is ready to explore.\"\n    },\n    \"sound\": \"default\",\n    \"badge\": 1\n  },\n  \"trail_id\": \"alpine-loop\"\n}"
            }
        },
        new()
        {
            Id = "push-fcm", Name = "Trail Notes Android", Description = "Firebase Cloud Messaging sandbox",
            Platform = PushProjectPlatform.Fcm, CreatedAt = Now.AddMonths(-2), LastModified = Now.AddDays(-1),
            FcmConfig = new()
            {
                SelectedGoogleIdentityId = "summit-google", UseToken = false, Topic = "trail-updates",
                Title = "The trail is calling", Body = "Your next adventure is ready to explore.",
                DataEntries = [new("trail_id", "alpine-loop"), new("screen", "trail-details")]
            }
        }
    ];
    public IReadOnlyList<SimulatorDevice> Simulators { get; } =
    [
        new("AAAAAAAA-1111-4111-8111-111111111111", "iPhone 17 Pro", "Booted", true,
            "com.apple.CoreSimulator.SimDeviceType.iPhone-17-Pro", "com.apple.CoreSimulator.SimRuntime.iOS-26-3", "iOS 26.3", "iPhone", "2026-11-20T11:00:00Z"),
        new("AAAAAAAA-2222-4222-8222-222222222222", "iPhone 17", "Shutdown", true,
            "com.apple.CoreSimulator.SimDeviceType.iPhone-17", "com.apple.CoreSimulator.SimRuntime.iOS-26-3", "iOS 26.3", "iPhone", null),
        new("AAAAAAAA-3333-4333-8333-333333333333", "iPad Pro 13-inch", "Shutdown", true,
            "com.apple.CoreSimulator.SimDeviceType.iPad-Pro-13-inch-M5", "com.apple.CoreSimulator.SimRuntime.iOS-26-3", "iOS 26.3", "iPad", null)
    ];
    public IReadOnlyList<PhysicalDevice> PhysicalDevices { get; } =
    [
        new("apple-phone", "00008130-0011223344550001", "Trail Notes iPhone", "iPhone 17 Pro", "iOS", "iPhone", "26.3", "usb", "connected", "connected", "paired", "enabled", "full"),
        new("apple-tablet", "00008120-0011223344550002", "Summit iPad", "iPad Pro", "iOS", "iPad", "26.3", "wifi", "connected", "connected", "paired", "enabled", "full")
    ];
    public IReadOnlyList<ProfilingSessionManifest> ProfilingSessions { get; } =
        new[] { ("profile-startup", "Trail Notes cold start", ProfilingScenarioKind.Launch, 8),
            ("profile-scroll", "Trail feed scrolling", ProfilingScenarioKind.Scrolling, 30),
            ("profile-memory", "Map preview allocations", ProfilingScenarioKind.MemoryInvestigation, 45) }
        .Select((item, index) => new ProfilingSessionManifest
        {
            Id = item.Item1, Name = item.Item2, Status = ProfilingSessionStatus.Completed,
            CreatedAt = new DateTimeOffset(Now.AddHours(-2 - index)), CompletedAt = new DateTimeOffset(Now.AddHours(-2 - index).AddSeconds(item.Item4)),
            Target = new() { Platform = ProfilingTargetPlatform.Android, Kind = ProfilingTargetKind.PhysicalDevice, Identifier = "SUMMIT-PIXEL-0001", DisplayName = "Pixel 9" },
            Project = new() { Path = "/projects/TrailNotes/TrailNotes.csproj", Name = "TrailNotes", Configuration = "Release", TargetFramework = "net10.0-android" },
            CaptureKinds = [ProfilingCaptureKind.Cpu, ProfilingCaptureKind.Memory],
            Options = new() { Scenario = item.Item3 },
            Pipeline = new() { Success = true, Duration = TimeSpan.FromSeconds(item.Item4) },
            Artifacts = [new() { FileName = "trace.speedscope.json", Kind = ProfilingArtifactKind.Trace, SizeBytes = 2_400_000 + index * 700_000, DisplayName = "CPU trace" }]
        }).ToArray();

    public MauiSherpaSettings CreateSettings() => new()
    {
        Preferences = new() { Theme = "Dark", FontScale = 1, DemoMode = false, AutoBackupEnabled = false, AndroidSdkPath = AndroidRoot },
        AppleIdentities = AppleIdentities.Select(i => new AppleIdentityData(i.Id, i.Name, i.KeyId, i.IssuerId, i.P8KeyContent!, Now.AddMonths(-6))).ToList(),
        GoogleIdentities = GoogleIdentities.Select(i => new GoogleIdentityData(i.Id, i.Name, i.ProjectId, i.ClientEmail, i.ServiceAccountJson)).ToList(),
        CloudProviders = Providers.Select(p => new CloudProviderData(p.Id, p.Name, p.ProviderType, p.Settings, p.Id == LocalProviderId)).ToList(),
        ActiveCloudProviderId = LocalProviderId,
        SecretsPublishers = Publishers.Select(p => new SecretsPublisherData(p.Id, p.ProviderId, p.Name, p.Settings)).ToList(),
        PushProjects = PushProjects.ToList(), LastModified = Now,
        PushTesting = new()
        {
            SelectedIdentityId = AppleIdentities[0].Id, TeamId = TeamId, BundleId = BundleId,
            DeviceToken = PushProjects[0].ApnsConfig!.DeviceToken,
            JsonPayload = PushProjects[0].ApnsConfig!.JsonPayload
        }
    };
}
