using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Core.Services;
using MauiSherpa.Workloads.Models;

namespace MauiSherpa.Core.Screenshots;

internal sealed partial class ScreenshotServices : IOpenJdkService
{
    private ScreenshotFixtures Fixtures => fixtures;
    private bool IsAppleHost => platform.IsMacOS || platform.IsMacCatalyst;
    private readonly ScreenshotFixtures fixtures;
    private MauiSherpaSettings settings;
    private AppleIdentity? appleIdentity;
    private GoogleIdentity? googleIdentity;
    private GoogleIdentity? firebaseIdentity;
    private string? lastAppleIdentityId;
    private string? lastGoogleIdentityId;
    private string activeProviderId = ScreenshotFixtures.LocalProviderId;
    private event Action? SettingsChanged;
    private event Action? AppleSelectionChanged;
    private event Action? GoogleSelectionChanged;
    private event Action? ActiveProviderChanged;

    private static Task<T> Read<T>(T value) => Task.FromResult(value);
    private static Task<IReadOnlyList<T>> List<T>(IEnumerable<T> values) => Read<IReadOnlyList<T>>(values.ToArray());

    Task<MauiSherpaSettings> IEncryptedSettingsService.GetSettingsAsync() => Read(settings);
    Task IEncryptedSettingsService.SaveSettingsAsync(MauiSherpaSettings value)
    {
        settings = value with { LastModified = ScreenshotFixtures.Now };
        SettingsChanged?.Invoke();
        return Task.CompletedTask;
    }
    Task IEncryptedSettingsService.UpdateSettingsAsync(Func<MauiSherpaSettings, MauiSherpaSettings> transform) =>
        ((IEncryptedSettingsService)this).SaveSettingsAsync(transform(settings));
    Task<bool> IEncryptedSettingsService.SettingsExistAsync() => Read(true);
    event Action? IEncryptedSettingsService.OnSettingsChanged { add => SettingsChanged += value; remove => SettingsChanged -= value; }
    Task<bool> ISettingsMigrationService.NeedsMigrationAsync() => Read(false);
    Task ISettingsMigrationService.MigrateAsync() => throw mode.Blocked("settings migration");
    LocalVaultAccessState ILocalVaultAccessService.GetState() => LocalVaultAccessState.Available;
    LocalVaultIntroductionState ILocalVaultIntroductionService.GetState() =>
        new(LocalVaultIntroductionState.CurrentVersion, LocalVaultIntroductionDecision.Enabled, ScreenshotFixtures.Now);
    bool IBackgroundTaskService.IsPersistent => false;
    IReadOnlyList<BackgroundTaskInfo> IBackgroundTaskService.Tasks => [];
    Task IBackgroundTaskService.InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    string IUpdateService.GetCurrentVersion() => "0.19.0";
    UpdateCheckResult? IUpdateService.CachedResult => new(false, "0.19.0", null);
    string? IUpdateService.DismissedVersion => null;
    Task<UpdateCheckResult> IUpdateService.CheckForUpdateAsync(CancellationToken cancellationToken) =>
        Read(new UpdateCheckResult(false, "0.19.0", null));
    Task IDialogService.ShowLoadingAsync(string message) => Task.CompletedTask;
    Task IDialogService.HideLoadingAsync() => Task.CompletedTask;
    Task IDialogService.CopyToClipboardAsync(string text) => throw mode.Blocked("dialog clipboard copy");

    Task<IReadOnlyList<AppleIdentity>> IAppleIdentityService.GetIdentitiesAsync() => Read(Fixtures.AppleIdentities);
    Task<AppleIdentity?> IAppleIdentityService.GetIdentityAsync(string id) => Read(Fixtures.AppleIdentities.SingleOrDefault(i => i.Id == id));
    AppleIdentity? IAppleIdentityStateService.SelectedIdentity => appleIdentity;
    string? IAppleIdentityStateService.LastSelectedIdentityId => lastAppleIdentityId;
    void IAppleIdentityStateService.SetSelectedIdentity(AppleIdentity? identity)
    {
        appleIdentity = identity is null ? null : Fixtures.AppleIdentities.Single(i => i.Id == identity.Id);
        lastAppleIdentityId = appleIdentity?.Id;
        AppleSelectionChanged?.Invoke();
    }
    event Action? IAppleIdentityStateService.OnSelectionChanged { add => AppleSelectionChanged += value; remove => AppleSelectionChanged -= value; }
    Task<IReadOnlyList<GoogleIdentity>> IGoogleIdentityService.GetIdentitiesAsync() => Read(Fixtures.GoogleIdentities);
    Task<GoogleIdentity?> IGoogleIdentityService.GetIdentityAsync(string id) => Read(Fixtures.GoogleIdentities.SingleOrDefault(i => i.Id == id));
    GoogleIdentity? IGoogleIdentityStateService.SelectedIdentity => googleIdentity;
    string? IGoogleIdentityStateService.LastSelectedIdentityId => lastGoogleIdentityId;
    void IGoogleIdentityStateService.SetSelectedIdentity(GoogleIdentity? identity)
    {
        googleIdentity = identity is null ? null : Fixtures.GoogleIdentities.Single(i => i.Id == identity.Id);
        lastGoogleIdentityId = googleIdentity?.Id;
        GoogleSelectionChanged?.Invoke();
    }
    event Action? IGoogleIdentityStateService.OnSelectionChanged { add => GoogleSelectionChanged += value; remove => GoogleSelectionChanged -= value; }
    string? IIdentitySelectionStore.GetLastAppleIdentityId() => lastAppleIdentityId;
    string? IIdentitySelectionStore.GetLastGoogleIdentityId() => lastGoogleIdentityId;
    void IIdentitySelectionStore.SetLastAppleIdentityId(string id) => lastAppleIdentityId = id;
    void IIdentitySelectionStore.SetLastGoogleIdentityId(string id) => lastGoogleIdentityId = id;

    string? IAndroidSdkService.SdkPath => Fixtures.AndroidRoot;
    bool IAndroidSdkService.IsSdkInstalled => true;
    Task<bool> IAndroidSdkService.DetectSdkAsync() => Read(true);
    Task<string?> IAndroidSdkService.GetDefaultSdkPathAsync() => Read<string?>(Fixtures.AndroidRoot);
    Task<IReadOnlyList<SdkPackageInfo>> IAndroidSdkService.GetInstalledPackagesAsync() => List(Fixtures.Packages.Where(p => p.IsInstalled));
    Task<IReadOnlyList<SdkPackageInfo>> IAndroidSdkService.GetAvailablePackagesAsync() => Read(Fixtures.Packages);
    Task<IReadOnlyList<DeviceInfo>> IAndroidSdkService.GetDevicesAsync() => Read(Fixtures.AndroidDevices);
    Task<IReadOnlyList<AvdInfo>> IAndroidSdkService.GetAvdsAsync() => Read(Fixtures.Avds);
    Task<IReadOnlyList<AvdDeviceDefinition>> IAndroidSdkService.GetAvdDeviceDefinitionsAsync() =>
        List<AvdDeviceDefinition>([new("pixel_9", "Pixel 9", "Google", 1), new("pixel_tablet", "Pixel Tablet", "Google", 2)]);
    Task<IReadOnlyList<string>> IAndroidSdkService.GetSystemImagesAsync() =>
        List(Fixtures.Packages.Where(p => p.Path.StartsWith("system-images;", StringComparison.Ordinal)).Select(p => p.Path));
    Task<IReadOnlyList<string>> IAndroidSdkService.GetAvdSkinsAsync() => List<string>(["pixel_9", "pixel_tablet"]);
    Task<AndroidAdbSnapshot> IAndroidAdbService.GetSnapshotAsync(CancellationToken ct) =>
        Read(new AndroidAdbSnapshot(Fixtures.AndroidDevices, [new("emulator-5554", "Trail_Notes_Pixel_9")]));
    IReadOnlyList<DeviceInfo> IAdbDeviceWatcherService.Devices => Fixtures.AndroidDevices;
    bool IAdbDeviceWatcherService.IsWatching => false;
    Task IAdbDeviceWatcherService.StartAsync() => Task.CompletedTask;
    void IAdbDeviceWatcherService.Stop() { }
    Task IDeviceMonitorService.StartAsync() => Task.CompletedTask;
    void IDeviceMonitorService.Stop() { }
    ConnectedDevicesSnapshot IDeviceMonitorService.Current => new(
        Fixtures.AndroidDevices.Where(d => !d.IsEmulator).ToArray(), Fixtures.AndroidDevices.Where(d => d.IsEmulator).ToArray(),
        IsAppleHost
            ? Fixtures.PhysicalDevices.Select(d => new AppleDeviceInfo(d.Identifier, d.Name, d.Model, "iOS", "arm64", d.OsVersion, false, true, d.TransportType, null)).ToArray()
            : [],
        IsAppleHost
            ? [new(Fixtures.Simulators[0].Udid, "iPhone 17 Pro", "iPhone 17 Pro", "iOS", "arm64", "26.3", true, true, null, "Booted")]
            : []);
    string? IAndroidSdkSettingsService.CustomSdkPath => null;
    bool IAndroidSdkService.IsBusy => false;
    event Action? IAndroidSdkService.BusyChanged { add { } remove { } }
    Task<IReadOnlyList<AndroidSdkLocation>> IAndroidSdkSettingsService.GetKnownLocationsAsync() =>
        Read<IReadOnlyList<AndroidSdkLocation>>([new(Fixtures.AndroidRoot, "Android Studio / MAUI", AndroidSdkLocationStatus.Installed, true)]);
    Task<string?> IAndroidSdkSettingsService.GetEffectiveSdkPathAsync() => Read<string?>(Fixtures.AndroidRoot);
    Task IAndroidSdkSettingsService.InitializeAsync() => Task.CompletedTask;
    string? IOpenJdkSettingsService.CustomJdkPath => null;
    Task<string?> IOpenJdkSettingsService.GetEffectiveJdkPathAsync() => Read<string?>(Fixtures.JdkRoot);
    Task IOpenJdkSettingsService.InitializeAsync() => Task.CompletedTask;
    IReadOnlyList<OpenJdkUpdate> IOpenJdkService.AvailableUpdates => [];
    string? IOpenJdkService.UpdateCheckError => null;
    event Action? IOpenJdkService.UpdatesChanged { add { } remove { } }
    void IOpenJdkService.StartUpdateChecks() { }
    Task IOpenJdkService.CheckForUpdatesAsync(CancellationToken ct) => Task.CompletedTask;
    Task<IReadOnlyList<OpenJdkInstallation>> IOpenJdkService.GetInstallationsAsync(CancellationToken ct) =>
        Read<IReadOnlyList<OpenJdkInstallation>>([new(Fixtures.JdkRoot, "21.0.8", true, false, true)]);
    Task<IReadOnlyList<OpenJdkRelease>> IOpenJdkService.GetReleasesAsync(bool forceRefresh, CancellationToken ct) =>
        Read<IReadOnlyList<OpenJdkRelease>>([
            new(25, "25.0.1", new("https://aka.ms/download-jdk/microsoft-jdk-25.0.1-macos-aarch64.tar.gz")),
            new(21, "21.0.8", new("https://aka.ms/download-jdk/microsoft-jdk-21.0.8-macos-aarch64.tar.gz")),
            new(17, "17.0.16", new("https://aka.ms/download-jdk/microsoft-jdk-17.0.16-macos-aarch64.tar.gz"))]);
    Task<string> IOpenJdkService.InstallAsync(int majorVersion, IProgress<string>? progress, CancellationToken ct) =>
        throw mode.Blocked("IOpenJdkService.InstallAsync");
    Task IOpenJdkService.UninstallAsync(string path, CancellationToken ct) => throw mode.Blocked("IOpenJdkService.UninstallAsync");

    Task<DoctorContext> IDoctorService.GetContextAsync(string? workingDirectory) => Read(Fixtures.DoctorContext);
    Task<DoctorReport> IDoctorService.RunDoctorAsync(DoctorContext? context, IProgress<string>? progress) => Read(Fixtures.Doctor);
    Task<IReadOnlyList<string>> IDoctorService.GetAvailableWorkloadSetVersionsAsync(string featureBand, bool includePrerelease) =>
        List<string>(["10.0.400", "10.0.401"]);
    string IDoctorService.GetDotNetExecutablePath() => Path.Combine(Fixtures.SdkRoot, platform.IsWindows ? "dotnet.exe" : "dotnet");
    string IDotnetUpService.ToolDirectory => Path.Combine(Fixtures.Home, ".dotnetup", "bin");
    string IDotnetUpService.ExecutablePath => Path.Combine(Fixtures.Home, ".dotnetup", "bin", "dotnetup");
    bool IDotnetUpService.IsInstalled => true;
    Task<DotnetUpToolInfo?> IDotnetUpService.GetToolInfoAsync(CancellationToken ct) => Read<DotnetUpToolInfo?>(new() { Version = "0.5.0", Architecture = Fixtures.Architecture });
    Task<DotnetUpToolUpdateInfo?> IDotnetUpService.GetToolUpdateInfoAsync(DotnetUpToolInfo? installedInfo, CancellationToken ct) =>
        Read<DotnetUpToolUpdateInfo?>(new() { InstalledVersion = "0.5.0", AvailableVersion = "0.5.0", UpdateAvailable = false });
    Task<DotnetUpListResult?> IDotnetUpService.GetListAsync(CancellationToken ct) => Read<DotnetUpListResult?>(Fixtures.DotnetList);
    Task<IReadOnlyList<DotnetUpdatePreview>> IDotnetUpService.GetUpdatePreviewAsync(DotnetUpListResult list, CancellationToken ct) =>
        List(list.InstallSpecs.Select(s => new DotnetUpdatePreview
        {
            Component = s.Component, Channel = s.VersionOrChannel,
            InstalledVersion = s.Component == DotnetUpComponent.Sdk ? s.VersionOrChannel.StartsWith("10.", StringComparison.Ordinal) ? "10.0.401" : "9.0.311" : "10.0.5",
            AvailableVersion = s.Component == DotnetUpComponent.Sdk ? s.VersionOrChannel.StartsWith("10.", StringComparison.Ordinal) ? "10.0.402" : "9.0.311" : "10.0.5",
            UpdateAvailable = s.VersionOrChannel == "10.0.4xx"
        }));
    private IReadOnlyList<DotnetWorkloadInventory> WorkloadInventories => Fixtures.DotnetList.Installations
        .Where(i => i.Component == DotnetUpComponent.Sdk).Select(i => new DotnetWorkloadInventory
        {
            Target = new() { InstallRoot = i.InstallRoot, DotnetPath = Path.Combine(i.InstallRoot, platform.IsWindows ? "dotnet.exe" : "dotnet"), Architecture = Fixtures.Architecture,
                FeatureBand = new SdkFeatureBand(i.Version), RepresentativeSdkVersion = i.Version, IsManagedByDotnetUp = true, CanWrite = true },
            ActiveWorkloadVersion = i.Version.StartsWith("10.", StringComparison.Ordinal) ? "10.0.400" : "9.0.300",
            UpdateMode = DotnetWorkloadUpdateMode.WorkloadSet, VersionSource = DotnetWorkloadVersionSource.MachineDefault,
            InstalledWorkloads = [new() { Id = "maui", ManifestVersion = i.Version.StartsWith("10.", StringComparison.Ordinal) ? "10.0.60" : "9.0.120", InstallationSource = "SDK" }],
            EffectiveInstalledWorkloads = [new() { Id = "maui", Kind = DotnetWorkloadInstallKind.Explicit,
                InstallationRecord = new() { Id = "maui", ManifestVersion = i.Version.StartsWith("10.", StringComparison.Ordinal) ? "10.0.60" : "9.0.120" } }],
            AvailableSetVersions = i.Version.StartsWith("10.", StringComparison.Ordinal)
                ? [new() { Version = "10.0.400" }, new() { Version = "10.0.401" }]
                : [new() { Version = "9.0.300" }],
            Capabilities = new() { MachineReadableList = true, WorkloadVersion = true }
        }).ToArray();
    Task<IReadOnlyList<DotnetWorkloadTarget>> IDotnetWorkloadService.GetTargetsAsync(DotnetUpListResult? list, CancellationToken ct) => List(WorkloadInventories.Select(i => i.Target));
    Task<IReadOnlyList<DotnetWorkloadInventory>> IDotnetWorkloadService.GetInventoriesAsync(DotnetUpListResult? list, bool forceRefresh, CancellationToken ct) => Read(WorkloadInventories);
    Task<DotnetWorkloadInventory> IDotnetWorkloadService.GetInventoryAsync(DotnetWorkloadTarget target, string? workingDirectory, bool forceRefresh, CancellationToken ct) =>
        Read(WorkloadInventories.Single(i => i.Target.Key == target.Key));

    Task<IReadOnlyList<AppleBundleId>> IAppleConnectService.GetBundleIdsAsync() => Read(Fixtures.BundleIds);
    Task<IReadOnlyList<AppleDevice>> IAppleConnectService.GetDevicesAsync() => Read(Fixtures.AppleDevices);
    Task<IReadOnlyList<AppleCertificate>> IAppleConnectService.GetCertificatesAsync() => Read(Fixtures.Certificates);
    Task<IReadOnlyList<AppleProfile>> IAppleConnectService.GetProfilesAsync() => Read(Fixtures.Profiles);
    Task<IReadOnlyList<AppleProfile>> IAppleConnectService.GetProfilesForIdentityAsync(string identityId) => Read(Fixtures.Profiles);
    Task<IReadOnlyList<AppleBundleIdCapability>> IAppleConnectService.GetBundleIdCapabilitiesAsync(string bundleIdResourceId) =>
        List(Fixtures.BundleIds.Single(b => b.Id == bundleIdResourceId).Capabilities ?? []);
    Task<IReadOnlyList<string>> IAppleConnectService.GetAvailableCapabilityTypesAsync() => List(CapabilityCategories.Categories.Values.SelectMany(c => c));
    Task<IReadOnlyList<string>> IAppleConnectService.GetProvisioningProfilesDirectoriesAsync() =>
        List<string>([Path.Combine(Fixtures.Home, "Library", "Developer", "Xcode", "UserData", "Provisioning Profiles")]);
    Task<IReadOnlyList<string>> IAppleConnectService.FindInstalledProfilePathsAsync(string uuid) => List<string>([]);
    private IReadOnlyList<LocalSigningIdentity> SigningIdentities => IsAppleHost
        ? Fixtures.Certificates.Select(c => new LocalSigningIdentity(c.Name, c.Name, ScreenshotFixtures.TeamId, c.SerialNumber, c.ExpirationDate, true, c.SerialNumber)).ToArray()
        : [];
    bool ILocalCertificateService.IsSupported => IsAppleHost;
    Task<IReadOnlyList<LocalSigningIdentity>> ILocalCertificateService.GetSigningIdentitiesAsync() => Read(SigningIdentities);
    Task<LocalSigningIdentityDiscoveryResult> ILocalCertificateService.GetSigningIdentityDiscoveryAsync() => Read(new LocalSigningIdentityDiscoveryResult(SigningIdentities, []));
    Task<bool> ILocalCertificateService.HasPrivateKeyAsync(string serialNumber) => Read(IsAppleHost && Fixtures.Certificates.Any(c => c.SerialNumber == serialNumber));
    Task<bool> ILocalCertificateService.HasInstalledIdentityAsync(string serialNumber) => ((ILocalCertificateService)this).HasPrivateKeyAsync(serialNumber);
    void ILocalCertificateService.InvalidateCache() { }
    bool IAppleDownloadAuthService.IsAuthenticated => false;
    string? IAppleDownloadAuthService.CurrentAppleId => null;
    string? IAppleDownloadAuthService.SessionValidationMessage => null;
    AppleAuthSession? IAppleDownloadAuthService.GetSession() => null;

    bool IXcodeService.IsSupported => IsAppleHost;
    Task<IReadOnlyList<XcodeInstallation>> IXcodeService.GetInstalledXcodesAsync() =>
        List<XcodeInstallation>([new("/Applications/Xcode_26.3.app", "26.3", "17C100", true, true), new("/Applications/Xcode_26.2.app", "26.2", "17B102", false)]);
    Task<string?> IXcodeService.GetSelectedXcodePathAsync() => Read<string?>("/Applications/Xcode_26.3.app/Contents/Developer");
    Task<IReadOnlyList<XcodeRelease>> IXcodeService.GetAvailableReleasesAsync() =>
        List<XcodeRelease>([new("26.3", "17C100", ScreenshotFixtures.Now.AddDays(-20), false, "15.6", "https://example.invalid/xcode.xip", null, 8_100_000_000,
            [new("iOS", "26.3"), new("macOS", "26.3"), new("watchOS", "26.3")], [new("Swift", "6.2")]),
            new("26.2", "17B102", ScreenshotFixtures.Now.AddMonths(-2), false, "15.6", "https://example.invalid/xcode-26.2.xip", null, 8_000_000_000,
                [new("iOS", "26.2"), new("macOS", "26.2")], [new("Swift", "6.2")])]);
    Task<XcodeNormalizationPlan> IXcodeService.GetNormalizationPlanAsync() => Read(new XcodeNormalizationPlan("_", [], null));
    bool ISimulatorService.IsSupported => IsAppleHost;
    Task<IReadOnlyList<SimulatorDevice>> ISimulatorService.GetSimulatorsAsync() => Read(Fixtures.Simulators);
    Task<IReadOnlyList<SimulatorDeviceType>> ISimulatorService.GetDeviceTypesAsync() =>
        List(Fixtures.Simulators.Select(s => new SimulatorDeviceType(s.DeviceTypeIdentifier, s.Name, s.ProductFamily!)));
    Task<IReadOnlyList<SimulatorRuntime>> ISimulatorService.GetRuntimesAsync() =>
        List<SimulatorRuntime>([new("com.apple.CoreSimulator.SimRuntime.iOS-26-3", "iOS 26.3", "26.3", "iOS", true)]);
    Task<IReadOnlyList<SimulatorRuntimeStorage>> ISimulatorService.GetRuntimeStorageAsync() =>
        List<SimulatorRuntimeStorage>([new("com.apple.CoreSimulator.SimRuntime.iOS-26-3", "runtime-ios-26-3", "23D100", "com.apple.platform.iphonesimulator", "Ready", 9_600_000_000, true, ScreenshotFixtures.Now.AddHours(-1), "/Library/Developer/CoreSimulator/Volumes/iOS_26.3")]);
    Task<IReadOnlyList<DownloadableSimulatorRuntime>> ISimulatorService.GetDownloadableRuntimesAsync() =>
        List<DownloadableSimulatorRuntime>([new("runtime-ios-26-3", "iOS 26.3", "26.3", "23D100", "com.apple.platform.iphonesimulator", "dmg", 8_300_000_000, ["arm64", "x86_64"], false, false, "Apple")]);
    bool ISimulatorService.IsPlayingRoute => false;
    bool IPhysicalDeviceService.IsSupported => IsAppleHost;
    Task<IReadOnlyList<PhysicalDevice>> IPhysicalDeviceService.GetDevicesAsync() => Read(Fixtures.PhysicalDevices);
    bool IAppleDevicePairingService.IsSupported => IsAppleHost;
    bool IAppleDevicePairingService.IsWirelessSupported => IsAppleHost;
    Task<IReadOnlyList<PhysicalDevice>> IAppleDevicePairingService.GetDevicesAsync(CancellationToken ct) => Read(Fixtures.PhysicalDevices);

    Task<IReadOnlyList<AndroidKeystore>> IKeystoreService.ListKeystoresAsync() => Read(Fixtures.Keystores);
    Task<string?> IKeystoreService.GetKeytoolPathAsync() => Read<string?>(Path.Combine(Fixtures.JdkRoot, "bin", "keytool"));
    Task<string?> IKeystoreService.GetKeystorePasswordAsync(string id) => Read<string?>(Fixtures.Keystores.Any(k => k.Id == id) ? "screenshot-only-password" : null);
    Task<string?> IKeystoreService.GetKeyPasswordAsync(string id) => ((IKeystoreService)this).GetKeystorePasswordAsync(id);
    Task<KeystoreSignatureInfo> IKeystoreService.GetSignatureHashesAsync(string keystorePath, string alias, string password) =>
        Read(new KeystoreSignatureInfo(alias, "00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF",
            "00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33",
            "00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF",
            "ABEiM0RVZneImaq7zN3u/wARIjM=", "ABEiM0RVZneImaq7zN3u/wARIjNEVWZ3iJmqu8zd7v8="));
    Task<IReadOnlyList<KeystoreSyncStatus>> IKeystoreSyncService.GetKeystoreStatusesAsync(CancellationToken ct) =>
        List(Fixtures.Keystores.Select(k => new KeystoreSyncStatus(k.Id, k.Alias, k.FilePath, $"keystore/{k.Alias}", true, true)));

    Task<IReadOnlyList<CloudSecretsProviderConfig>> ICloudSecretsService.GetProvidersAsync() => Read(Fixtures.Providers);
    Task ICloudSecretsService.InitializeAsync() => Task.CompletedTask;
    CloudSecretsProviderConfig? ICloudSecretsService.ActiveProvider => Fixtures.Providers.Single(p => p.Id == activeProviderId);
    Task ICloudSecretsService.SetActiveProviderAsync(string? id)
    {
        activeProviderId = Fixtures.Providers.Single(p => p.Id == id).Id;
        ActiveProviderChanged?.Invoke();
        return Task.CompletedTask;
    }
    event Action? ICloudSecretsService.OnActiveProviderChanged { add => ActiveProviderChanged += value; remove => ActiveProviderChanged -= value; }
    Task<IReadOnlyList<CloudSecretsProviderConfig>> ISecretsProviderRegistry.GetProvidersAsync() => Read(Fixtures.Providers);
    Task<CloudSecretsProviderConfig?> ISecretsProviderRegistry.GetProviderConfigAsync(string id) => Read(Fixtures.Providers.SingleOrDefault(p => p.Id == id));
    Task<ICloudSecretsProvider?> ISecretsProviderRegistry.GetProviderAsync(string id) =>
        Read<ICloudSecretsProvider?>(new ScreenshotCloudProvider(mode, Fixtures.Providers.Single(p => p.Id == id), Fixtures));
    ICloudSecretsProvider ICloudSecretsProviderFactory.CreateProvider(CloudSecretsProviderConfig config) =>
        new ScreenshotCloudProvider(mode, Fixtures.Providers.Single(p => p.Id == config.Id), Fixtures);
    IReadOnlyList<CloudSecretsProviderType> ICloudSecretsProviderFactory.SupportedProviders => [CloudSecretsProviderType.Local, CloudSecretsProviderType.AzureKeyVault];
    string ICloudSecretsProviderFactory.GetProviderDisplayName(CloudSecretsProviderType type) => type switch
    {
        CloudSecretsProviderType.Local => "Local Vault",
        CloudSecretsProviderType.AzureKeyVault => "Azure Key Vault",
        _ => throw mode.Blocked($"unconfigured provider {type}")
    };
    Task<IReadOnlyList<ManagedSecret>> IManagedSecretsService.ListAsync(CancellationToken ct) => Read(Fixtures.Secrets);
    Task<ManagedSecretListing> IManagedSecretsService.ListWithStatusAsync(CancellationToken ct) => Read(new ManagedSecretListing(Fixtures.Secrets, []));
    Task<IReadOnlyList<ManagedSecretFolder>> IManagedSecretsService.ListFoldersAsync(CancellationToken ct) =>
        List<ManagedSecretFolder>([new("/Production", "Production", ScreenshotFixtures.Now.AddMonths(-2)), new("/Development", "Development", ScreenshotFixtures.Now.AddMonths(-3))]);
    Task<ManagedSecret?> IManagedSecretsService.GetAsync(string key, CancellationToken ct) => Read(Fixtures.Secrets.SingleOrDefault(s => s.Key == key));
    Task<byte[]?> IManagedSecretsService.GetValueAsync(string key, CancellationToken ct) => Read(Fixtures.GetSecretValue(key));
    Task<byte[]?> ICloudSecretsService.GetSecretAsync(string key, CancellationToken ct) => Read(Fixtures.GetSecretValue(key));
    Task<bool> ICloudSecretsService.SecretExistsAsync(string key, CancellationToken ct) => Read(Fixtures.Secrets.Any(s => s.Key == key));
    Task<IReadOnlyList<string>> ICloudSecretsService.ListSecretsAsync(string? prefix, CancellationToken ct) =>
        List(Fixtures.Secrets.Where(s => prefix is null || s.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(s => s.Key));
    private SecretItemSyncState Placement(SecretItemRef item) =>
        new(item, Fixtures.Providers.Select(p => new ProviderPlacementState(p.Id, true, true, SecretPlacementStatus.Synced, 1, "screenshot-v1")).ToArray(), false);
    Task<SecretItemSyncState> ISecretSyncCoordinator.GetStateAsync(SecretItemRef item, CancellationToken ct) => Read(Placement(item));
    Task<SecretItemSyncState> ISecretSyncCoordinator.GetStateProgressivelyAsync(SecretItemRef item, IProgress<ProviderPlacementState>? progress, CancellationToken ct)
    {
        var state = Placement(item);
        foreach (var provider in state.Providers)
            progress?.Report(provider);
        return Read(state);
    }
    Task<IReadOnlyList<CertificateSecretInfo>> ICertificateSyncService.GetCertificateStatusesAsync(IEnumerable<AppleCertificate> certificates, CancellationToken ct) =>
        List(certificates.Select(c => new CertificateSecretInfo(c.Id, c.SerialNumber, IsAppleHost ? SecretLocation.Both : SecretLocation.CloudOnly,
            ScreenshotFixtures.LocalProviderId, $"certificate/{c.SerialNumber}", ScreenshotFixtures.Now.AddHours(-2))));

    Task<IReadOnlyList<SecretsPublisherConfig>> ISecretsPublisherService.GetPublishersAsync() => Read(Fixtures.Publishers);
    Task<SecretsPublisherConfig?> ISecretsPublisherService.GetPublisherAsync(string id) => Read(Fixtures.Publishers.SingleOrDefault(p => p.Id == id));
    Task<IReadOnlyList<PublishProfile>> IPublishProfileService.GetProfilesAsync() => Read(Fixtures.PublishProfiles);
    Task<IReadOnlyList<PublishProfile>> IPublishProfileService.GetProfilesAsync(IProgress<IReadOnlyList<PublishProfile>>? progress)
        => Read(Fixtures.PublishProfiles);
    Task<IReadOnlyList<PublishProfile>> IPublishProfileService.RefreshProfilesAsync(IProgress<IReadOnlyList<PublishProfile>>? progress) =>
        ((IPublishProfileService)this).GetProfilesAsync(progress);
    Task<PublishProfile?> IPublishProfileService.GetProfileAsync(string id) => Read(Fixtures.PublishProfiles.SingleOrDefault(p => p.Id == id));
    Task<IReadOnlyList<PushProject>> IPushProjectService.GetProjectsAsync(PushProjectPlatform? platform) =>
        List(Fixtures.PushProjects.Where(p => platform is null || p.Platform == platform));
    Task<PushProject?> IPushProjectService.GetProjectAsync(string id) => Read(Fixtures.PushProjects.SingleOrDefault(p => p.Id == id));
    Task<PushProject?> IPushProjectService.MigrateFromLegacyAsync() => Read<PushProject?>(null);
    Task<string?> IFirebasePushService.GetProjectIdAsync() => Read(firebaseIdentity?.ProjectId);
    Task<bool> IFirebasePushService.HasCredentialsAsync() => Read(firebaseIdentity is not null);
    Task IFirebasePushService.SaveServiceAccountAsync(string json)
    {
        firebaseIdentity = Fixtures.GoogleIdentities.SingleOrDefault(i => i.ServiceAccountJson == json)
            ?? throw mode.Blocked("importing non-fixture Firebase credentials");
        return Task.CompletedTask;
    }
    Task IFirebasePushService.ClearCredentialsAsync()
    {
        firebaseIdentity = null;
        return Task.CompletedTask;
    }
    Task<IReadOnlyList<ProfilingSessionManifest>> IProfilingSessionStorageService.GetSessionsAsync(CancellationToken ct) => Read(Fixtures.ProfilingSessions);
    Task<ProfilingSessionManifest?> IProfilingSessionStorageService.GetSessionAsync(string id, CancellationToken ct) => Read(Fixtures.ProfilingSessions.SingleOrDefault(s => s.Id == id));
    Task<MauiCliToolStatus> IMauiCliToolService.GetStatusAsync(CancellationToken ct) => Read(new MauiCliToolStatus(MauiCliToolState.Available, "/tools/maui", "1.0.0"));
    Task<MauiCliToolUpdateInfo> IMauiCliToolService.GetUpdateInfoAsync(MauiCliToolStatus status, CancellationToken ct) => Read(new MauiCliToolUpdateInfo("1.0.0", "1.0.0"));
    MauiProfileRunState IMauiProfilingCliService.State => MauiProfileRunState.Idle;
    bool ICopilotService.IsConnected => false;
    string? ICopilotService.CurrentSessionId => null;
    IReadOnlyList<CopilotChatMessage> ICopilotService.Messages => [];
    CopilotAvailability? ICopilotService.CachedAvailability => new(false, false, null, null, "Copilot is disabled in Screenshot Mode.");
    Task<CopilotAvailability> ICopilotService.CheckAvailabilityAsync(bool forceRefresh) =>
        Read(new CopilotAvailability(false, false, null, null, "Copilot is disabled in Screenshot Mode."));
}
