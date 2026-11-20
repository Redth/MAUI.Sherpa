using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Screenshots;
internal sealed partial class ScreenshotServices : IDialogService, IFileSystemService, ISecureStorageService, ILegacySecureStorageService, ILocalVaultKeyStore, ILocalSecretsKeyStore, ILocalVaultAccessService, ILocalVaultIntroductionService, ILocalVaultStore, IAndroidSdkService, IAndroidAdbService, ILogcatService, IAdbDeviceWatcherService, IAndroidWirelessPairingService, IDeviceMonitorService, IDeviceFileService, IDeviceShellService, IScreenCaptureService, IProfilingCatalogService, IMauiCliToolService, IMauiProfilingCliService, IProfilingArtifactLibraryService, IProfilingArtifactAnalysisService, IGcDumpReportService, IProfilingArtifactConverterService, IProfilingSessionStorageService, IAndroidSdkSettingsService, IOpenJdkSettingsService, IFirebasePushService, IAppleIdentityService, IAppleIdentityStateService, IGoogleIdentityService, IGoogleIdentityStateService, IIdentitySelectionStore, IAppleConnectService, ICertificateRenewalService, IProfileDeviceService, IAppleRootCertService, IXcodeService, IAppleDownloadAuthService, ISimulatorService, IAndroidDeviceToolsService, IAppleDevicePairingService, IPhysicalDeviceService, IPhysicalDeviceLogService, ISimulatorLogService, ILocalCertificateService, IDoctorService, IDotnetWorkloadService, IDotnetUpService, IProcessExecutionService, ICopilotService, ICopilotAgentService, ICopilotToolsService, IProfilingContextService, ICloudSecretsProvider, ICloudSecretsProviderFactory, ISecretsProviderRegistry, ISecretItemAdapter, ISecretSyncCoordinator, IBackgroundTaskHandler, IBackgroundTaskService, IManagedSecretsService, ICloudSecretsService, ICertificateSyncService, ISecretsPublisher, ISecretsPublisherFactory, ISecretsPublisherService, IPublishProfileService, IPublishProfileHealthService, IPushProjectService, IEncryptedSettingsService, IBackupService, ISettingsMigrationService, IUpdateService, IKeystoreService, IKeystoreSyncService, IApnsPushService, IDeepLinkValidationService
{
    private readonly ScreenshotMode mode;
    private readonly IPlatformService platform;
    public ScreenshotServices(ScreenshotMode mode, IPlatformService platform)
    {
        this.mode = mode;
        this.platform = platform;
        fixtures = new ScreenshotFixtures(platform);
        settings = fixtures.CreateSettings();
        appleIdentity = fixtures.AppleIdentities[0];
        googleIdentity = fixtures.GoogleIdentities[0];
        firebaseIdentity = googleIdentity;
        lastAppleIdentityId = appleIdentity.Id;
        lastGoogleIdentityId = googleIdentity.Id;
    }

    Task<string?> IDialogService.ShowInputDialogAsync(string title, string message, string placeholder) => throw mode.Blocked("IDialogService.ShowInputDialogAsync");
    Task<string?> IDialogService.ShowFileDialogAsync(string title, bool isSave, string[]? filters, string? defaultFileName) => throw mode.Blocked("IDialogService.ShowFileDialogAsync");
    Task<string?> IDialogService.PickFolderAsync(string title) => throw mode.Blocked("IDialogService.PickFolderAsync");
    Task<string?> IDialogService.PickOpenFileAsync(string title, string[]? extensions) => throw mode.Blocked("IDialogService.PickOpenFileAsync");
    Task<string?> IDialogService.PickSaveFileAsync(string title, string suggestedName, string extension, bool startOnDesktop) => throw mode.Blocked("IDialogService.PickSaveFileAsync");
    Task<string?> IFileSystemService.ReadFileAsync(string path) => throw mode.Blocked("IFileSystemService.ReadFileAsync");
    Task IFileSystemService.WriteFileAsync(string path, string content) => throw mode.Blocked("IFileSystemService.WriteFileAsync");
    Task<bool> IFileSystemService.FileExistsAsync(string path) => throw mode.Blocked("IFileSystemService.FileExistsAsync");
    Task<bool> IFileSystemService.DirectoryExistsAsync(string path) => throw mode.Blocked("IFileSystemService.DirectoryExistsAsync");
    Task<IReadOnlyList<string>> IFileSystemService.GetFilesAsync(string path, string searchPattern) => throw mode.Blocked("IFileSystemService.GetFilesAsync");
    Task IFileSystemService.CreateDirectoryAsync(string path) => throw mode.Blocked("IFileSystemService.CreateDirectoryAsync");
    Task IFileSystemService.DeleteFileAsync(string path) => throw mode.Blocked("IFileSystemService.DeleteFileAsync");
    Task IFileSystemService.DeleteDirectoryAsync(string path) => throw mode.Blocked("IFileSystemService.DeleteDirectoryAsync");
    void IFileSystemService.RevealInFileManager(string path) => throw mode.Blocked("IFileSystemService.RevealInFileManager");
    Task<string?> ISecureStorageService.GetAsync(string key) => throw mode.Blocked("ISecureStorageService.GetAsync");
    Task ISecureStorageService.SetAsync(string key, string value) => throw mode.Blocked("ISecureStorageService.SetAsync");
    Task ISecureStorageService.RemoveAsync(string key) => throw mode.Blocked("ISecureStorageService.RemoveAsync");
    Task<string> ILocalVaultKeyStore.GetOrCreateKeyAsync(CancellationToken cancellationToken) => throw mode.Blocked("ILocalVaultKeyStore.GetOrCreateKeyAsync");
    Task<LocalVaultAccessState> ILocalVaultAccessService.RequestAccessAsync(CancellationToken cancellationToken) => throw mode.Blocked("ILocalVaultAccessService.RequestAccessAsync");
    event Action? ILocalVaultAccessService.StateChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    Task ILocalVaultIntroductionService.MarkEnabledAsync(CancellationToken cancellationToken) => throw mode.Blocked("ILocalVaultIntroductionService.MarkEnabledAsync");
    Task ILocalVaultIntroductionService.MarkDeclinedAsync(CancellationToken cancellationToken) => throw mode.Blocked("ILocalVaultIntroductionService.MarkDeclinedAsync");
    event Action? ILocalVaultIntroductionService.StateChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    string ILocalVaultStore.DatabasePath { get => throw mode.Blocked("ILocalVaultStore.DatabasePath"); }

    Task<LocalVaultItem> ILocalVaultStore.PutAsync(string scope, string path, string key, byte[] value, string contentType, Dictionary<string, string>? metadata, CancellationToken cancellationToken) => throw mode.Blocked("ILocalVaultStore.PutAsync");
    Task<LocalVaultItem?> ILocalVaultStore.GetAsync(string scope, string path, string key, CancellationToken cancellationToken) => throw mode.Blocked("ILocalVaultStore.GetAsync");
    Task<bool> ILocalVaultStore.RemoveAsync(string scope, string path, string key, CancellationToken cancellationToken) => throw mode.Blocked("ILocalVaultStore.RemoveAsync");
    Task<bool> ILocalVaultStore.ExistsAsync(string scope, string path, string key, CancellationToken cancellationToken) => throw mode.Blocked("ILocalVaultStore.ExistsAsync");
    Task<IReadOnlyList<LocalVaultItem>> ILocalVaultStore.ListAsync(string scope, string? path, string? keyPrefix, CancellationToken cancellationToken) => throw mode.Blocked("ILocalVaultStore.ListAsync");
    Task<bool> IAndroidSdkService.SetSdkPathAsync(string path) => throw mode.Blocked("IAndroidSdkService.SetSdkPathAsync");
    Task<bool> IAndroidSdkService.InstallPackageAsync(string packagePath, IProgress<string>? progress) => throw mode.Blocked("IAndroidSdkService.InstallPackageAsync");
    Task<bool> IAndroidSdkService.UninstallPackageAsync(string packagePath) => throw mode.Blocked("IAndroidSdkService.UninstallPackageAsync");
    Task<bool> IAndroidSdkService.AcquireSdkAsync(string? targetPath, IProgress<string>? progress) => throw mode.Blocked("IAndroidSdkService.AcquireSdkAsync");
    Task<bool> IAndroidSdkService.CreateAvdAsync(string name, string systemImage, EmulatorCreateOptions? options, IProgress<string>? progress) => throw mode.Blocked("IAndroidSdkService.CreateAvdAsync");
    Task<bool> IAndroidSdkService.DeleteAvdAsync(string name) => throw mode.Blocked("IAndroidSdkService.DeleteAvdAsync");
    Task<bool> IAndroidSdkService.StartEmulatorAsync(string avdName, bool coldBoot, IProgress<string>? progress) => throw mode.Blocked("IAndroidSdkService.StartEmulatorAsync");
    Task<bool> IAndroidSdkService.StopEmulatorAsync(string serial) => throw mode.Blocked("IAndroidSdkService.StopEmulatorAsync");
    event Action? IAndroidSdkService.SdkPathChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    Task IAndroidAdbService.PairAsync(string endpoint, string pairingCode, CancellationToken ct) => throw mode.Blocked("IAndroidAdbService.PairAsync");
    Task IAndroidAdbService.ConnectAsync(string endpoint, CancellationToken ct) => throw mode.Blocked("IAndroidAdbService.ConnectAsync");
    bool ILogcatService.IsRunning { get => throw mode.Blocked("ILogcatService.IsRunning"); }

    IReadOnlyList<LogcatEntry> ILogcatService.Entries { get => throw mode.Blocked("ILogcatService.Entries"); }

    Task ILogcatService.StartAsync(string serial, CancellationToken ct) => throw mode.Blocked("ILogcatService.StartAsync");
    void ILogcatService.Stop() => throw mode.Blocked("ILogcatService.Stop");
    void ILogcatService.Clear() => throw mode.Blocked("ILogcatService.Clear");
    IAsyncEnumerable<LogcatEntry> ILogcatService.StreamAsync(CancellationToken ct) => throw mode.Blocked("ILogcatService.StreamAsync");
    event Action? ILogcatService.OnCleared
    {
        add
        {
        }

        remove
        {
        }
    }

    event Action<IReadOnlyList<DeviceInfo>>? IAdbDeviceWatcherService.DevicesChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    AndroidWirelessQrSession IAndroidWirelessPairingService.CreateQrPairingSession() => throw mode.Blocked("IAndroidWirelessPairingService.CreateQrPairingSession");
    Task<AndroidWirelessOperationResult> IAndroidWirelessPairingService.PairQrAsync(AndroidWirelessQrSession session, CancellationToken ct) => throw mode.Blocked("IAndroidWirelessPairingService.PairQrAsync");
    Task<AndroidWirelessOperationResult> IAndroidWirelessPairingService.PairAsync(string endpoint, string pairingCode, CancellationToken ct) => throw mode.Blocked("IAndroidWirelessPairingService.PairAsync");
    Task<AndroidWirelessOperationResult> IAndroidWirelessPairingService.ConnectAsync(string endpoint, CancellationToken ct) => throw mode.Blocked("IAndroidWirelessPairingService.ConnectAsync");
    Task<AndroidWirelessDiscoveryResult> IAndroidWirelessPairingService.DiscoverAsync(CancellationToken ct) => throw mode.Blocked("IAndroidWirelessPairingService.DiscoverAsync");
    Task IAndroidWirelessPairingService.StopDiscoveryAsync() => throw mode.Blocked("IAndroidWirelessPairingService.StopDiscoveryAsync");
    Task<bool> IAndroidWirelessPairingService.RestartAdbServerAsync(CancellationToken ct) => throw mode.Blocked("IAndroidWirelessPairingService.RestartAdbServerAsync");
    event Action<ConnectedDevicesSnapshot>? IDeviceMonitorService.Changed
    {
        add
        {
        }

        remove
        {
        }
    }

    Task<IReadOnlyList<DeviceFileEntry>> IDeviceFileService.ListAsync(string serial, string path, CancellationToken ct) => throw mode.Blocked("IDeviceFileService.ListAsync");
    Task IDeviceFileService.PullAsync(string serial, string remotePath, string localPath, IProgress<string>? progress, CancellationToken ct) => throw mode.Blocked("IDeviceFileService.PullAsync");
    Task IDeviceFileService.PushAsync(string serial, string localPath, string remotePath, IProgress<string>? progress, CancellationToken ct) => throw mode.Blocked("IDeviceFileService.PushAsync");
    Task IDeviceFileService.DeleteAsync(string serial, string remotePath, CancellationToken ct) => throw mode.Blocked("IDeviceFileService.DeleteAsync");
    bool IDeviceShellService.IsRunning { get => throw mode.Blocked("IDeviceShellService.IsRunning"); }

    string? IDeviceShellService.ActiveSerial { get => throw mode.Blocked("IDeviceShellService.ActiveSerial"); }

    Task IDeviceShellService.StartAsync(string serial, CancellationToken ct) => throw mode.Blocked("IDeviceShellService.StartAsync");
    Task IDeviceShellService.SendCommandAsync(string command, CancellationToken ct) => throw mode.Blocked("IDeviceShellService.SendCommandAsync");
    void IDeviceShellService.Stop() => throw mode.Blocked("IDeviceShellService.Stop");
    IAsyncEnumerable<string> IDeviceShellService.OutputStreamAsync(CancellationToken ct) => throw mode.Blocked("IDeviceShellService.OutputStreamAsync");
    bool IScreenCaptureService.IsRecording { get => throw mode.Blocked("IScreenCaptureService.IsRecording"); }

    Task<byte[]> IScreenCaptureService.CaptureScreenshotAsync(string serial, CancellationToken ct) => throw mode.Blocked("IScreenCaptureService.CaptureScreenshotAsync");
    Task IScreenCaptureService.StartRecordingAsync(string serial, int maxSeconds, CancellationToken ct) => throw mode.Blocked("IScreenCaptureService.StartRecordingAsync");
    Task<byte[]?> IScreenCaptureService.StopRecordingAsync(CancellationToken ct) => throw mode.Blocked("IScreenCaptureService.StopRecordingAsync");
    Task<ProfilingCatalog> IProfilingCatalogService.GetCatalogAsync(CancellationToken ct) => throw mode.Blocked("IProfilingCatalogService.GetCatalogAsync");
    Task<ProfilingPlatformCapabilities> IProfilingCatalogService.GetCapabilitiesAsync(ProfilingTargetPlatform platform, CancellationToken ct) => throw mode.Blocked("IProfilingCatalogService.GetCapabilitiesAsync");
    ProfilingSessionDefinition IProfilingCatalogService.CreateSessionDefinition(ProfilingTarget target, ProfilingScenarioKind scenario, string? name, IReadOnlyList<ProfilingCaptureKind>? captureKinds, string? appId, TimeSpan? duration, IReadOnlyDictionary<string, string>? tags) => throw mode.Blocked("IProfilingCatalogService.CreateSessionDefinition");
    ProfilingSessionValidationResult IProfilingCatalogService.ValidateSessionDefinition(ProfilingSessionDefinition definition, ProfilingPlatformCapabilities capabilities) => throw mode.Blocked("IProfilingCatalogService.ValidateSessionDefinition");
    Task<ProcessResult> IMauiCliToolService.InstallAsync(string? version, CancellationToken ct) => throw mode.Blocked("IMauiCliToolService.InstallAsync");
    Task<ProcessResult> IMauiCliToolService.UpdateAsync(string? version, CancellationToken ct) => throw mode.Blocked("IMauiCliToolService.UpdateAsync");
    Task<IReadOnlyList<MauiCliDevice>> IMauiCliToolService.GetDevicesAsync(CancellationToken ct) => throw mode.Blocked("IMauiCliToolService.GetDevicesAsync");
    event EventHandler<MauiProfileStateChangedEventArgs>? IMauiProfilingCliService.StateChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    event EventHandler<MauiCliMessageEventArgs>? IMauiProfilingCliService.MessageReceived
    {
        add
        {
        }

        remove
        {
        }
    }

    Task<MauiProfileExecutionResult> IMauiProfilingCliService.RunAsync(MauiProfileRequest request, CancellationToken ct) => throw mode.Blocked("IMauiProfilingCliService.RunAsync");
    Task IMauiProfilingCliService.BeginRecordingAsync(CancellationToken ct) => throw mode.Blocked("IMauiProfilingCliService.BeginRecordingAsync");
    Task IMauiProfilingCliService.StopRecordingAsync(CancellationToken ct) => throw mode.Blocked("IMauiProfilingCliService.StopRecordingAsync");
    void IMauiProfilingCliService.Cancel() => throw mode.Blocked("IMauiProfilingCliService.Cancel");
    Task<IReadOnlyList<ProfilingArtifactLibraryEntry>> IProfilingArtifactLibraryService.GetArtifactsAsync(ProfilingArtifactLibraryQuery? query, CancellationToken ct) => throw mode.Blocked("IProfilingArtifactLibraryService.GetArtifactsAsync");
    Task<ProfilingArtifactLibraryEntry?> IProfilingArtifactLibraryService.GetArtifactAsync(string artifactId, CancellationToken ct) => throw mode.Blocked("IProfilingArtifactLibraryService.GetArtifactAsync");
    Task<ProfilingArtifactLibraryEntry> IProfilingArtifactLibraryService.SaveArtifactAsync(ProfilingArtifactLibrarySaveRequest request, CancellationToken ct) => throw mode.Blocked("IProfilingArtifactLibraryService.SaveArtifactAsync");
    Task IProfilingArtifactLibraryService.DeleteArtifactAsync(string artifactId, bool deleteFile, CancellationToken ct) => throw mode.Blocked("IProfilingArtifactLibraryService.DeleteArtifactAsync");
    Task<string?> IProfilingArtifactLibraryService.GetArtifactPathAsync(string artifactId, CancellationToken ct) => throw mode.Blocked("IProfilingArtifactLibraryService.GetArtifactPathAsync");
    string IProfilingArtifactLibraryService.GetDefaultArtifactDirectory(string sessionId) => throw mode.Blocked("IProfilingArtifactLibraryService.GetDefaultArtifactDirectory");
    event Action? IProfilingArtifactLibraryService.OnArtifactsChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    Task<ProfilingArtifactAnalysisResult> IProfilingArtifactAnalysisService.AnalyzeArtifactAsync(string artifactId, CancellationToken ct) => throw mode.Blocked("IProfilingArtifactAnalysisService.AnalyzeArtifactAsync");
    Task<IReadOnlyList<ProfilingArtifactAnalysis>> IProfilingArtifactAnalysisService.AnalyzeArtifactsAsync(ProfilingArtifactLibraryQuery? query, CancellationToken ct) => throw mode.Blocked("IProfilingArtifactAnalysisService.AnalyzeArtifactsAsync");
    Task<GcDumpReport?> IGcDumpReportService.GetReportAsync(string gcdumpPath, CancellationToken ct) => throw mode.Blocked("IGcDumpReportService.GetReportAsync");
    Task<string?> IProfilingArtifactConverterService.ConvertToSpeedscopeAsync(string nettracePath, CancellationToken ct) => throw mode.Blocked("IProfilingArtifactConverterService.ConvertToSpeedscopeAsync");
    Task IProfilingSessionStorageService.SaveSessionAsync(ProfilingSessionManifest manifest, CancellationToken ct) => throw mode.Blocked("IProfilingSessionStorageService.SaveSessionAsync");
    Task IProfilingSessionStorageService.DeleteSessionAsync(string sessionId, CancellationToken ct) => throw mode.Blocked("IProfilingSessionStorageService.DeleteSessionAsync");
    Task<ProfilingSessionManifest> IProfilingSessionStorageService.SaveMauiProfileSessionAsync(string sessionId, MauiProfileRequest request, MauiProfileResult result, string? cliVersion, CancellationToken ct) => throw mode.Blocked("IProfilingSessionStorageService.SaveMauiProfileSessionAsync");
    Task<ProfilingSessionManifest> IProfilingSessionStorageService.ImportArtifactAsync(string artifactPath, CancellationToken ct) => throw mode.Blocked("IProfilingSessionStorageService.ImportArtifactAsync");
    string IProfilingSessionStorageService.GetSessionDirectoryPath(string sessionId) => throw mode.Blocked("IProfilingSessionStorageService.GetSessionDirectoryPath");
    string IProfilingSessionStorageService.GenerateSessionId(string? projectName) => throw mode.Blocked("IProfilingSessionStorageService.GenerateSessionId");
    Task IProfilingSessionStorageService.ExportSessionAsync(string sessionId, string outputZipPath, CancellationToken ct) => throw mode.Blocked("IProfilingSessionStorageService.ExportSessionAsync");
    Task<ProfilingSessionManifest?> IProfilingSessionStorageService.ImportSessionAsync(string zipPath, CancellationToken ct) => throw mode.Blocked("IProfilingSessionStorageService.ImportSessionAsync");
    Task IAndroidSdkSettingsService.SetCustomSdkPathAsync(string? path) => throw mode.Blocked("IAndroidSdkSettingsService.SetCustomSdkPathAsync");
    Task IAndroidSdkSettingsService.ResetToDefaultAsync() => throw mode.Blocked("IAndroidSdkSettingsService.ResetToDefaultAsync");
    event Action? IAndroidSdkSettingsService.SdkPathChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    Task IOpenJdkSettingsService.SetCustomJdkPathAsync(string? path) => throw mode.Blocked("IOpenJdkSettingsService.SetCustomJdkPathAsync");
    Task IOpenJdkSettingsService.ResetToDefaultAsync() => throw mode.Blocked("IOpenJdkSettingsService.ResetToDefaultAsync");
    event Action? IOpenJdkSettingsService.JdkPathChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    Task<FcmPushResponse> IFirebasePushService.SendPushAsync(FcmPushRequest request) => throw mode.Blocked("IFirebasePushService.SendPushAsync");
    Task<FcmPushResponse> IFirebasePushService.SendRawJsonAsync(string messageJson) => throw mode.Blocked("IFirebasePushService.SendRawJsonAsync");
    Task IAppleIdentityService.SaveIdentityAsync(AppleIdentity identity) => throw mode.Blocked("IAppleIdentityService.SaveIdentityAsync");
    Task IAppleIdentityService.DeleteIdentityAsync(string id) => throw mode.Blocked("IAppleIdentityService.DeleteIdentityAsync");
    Task<bool> IAppleIdentityService.TestConnectionAsync(AppleIdentity identity) => throw mode.Blocked("IAppleIdentityService.TestConnectionAsync");
    Task IGoogleIdentityService.SaveIdentityAsync(GoogleIdentity identity) => throw mode.Blocked("IGoogleIdentityService.SaveIdentityAsync");
    Task IGoogleIdentityService.DeleteIdentityAsync(string id) => throw mode.Blocked("IGoogleIdentityService.DeleteIdentityAsync");
    Task<bool> IGoogleIdentityService.TestConnectionAsync(GoogleIdentity identity) => throw mode.Blocked("IGoogleIdentityService.TestConnectionAsync");
    Task<AppleBundleId> IAppleConnectService.CreateBundleIdAsync(string identifier, string name, string platform, string? seedId) => throw mode.Blocked("IAppleConnectService.CreateBundleIdAsync");
    Task IAppleConnectService.DeleteBundleIdAsync(string id) => throw mode.Blocked("IAppleConnectService.DeleteBundleIdAsync");
    Task IAppleConnectService.EnableCapabilityAsync(string bundleIdResourceId, string capabilityType) => throw mode.Blocked("IAppleConnectService.EnableCapabilityAsync");
    Task IAppleConnectService.DisableCapabilityAsync(string capabilityId) => throw mode.Blocked("IAppleConnectService.DisableCapabilityAsync");
    Task<AppleDevice> IAppleConnectService.RegisterDeviceAsync(string udid, string name, string platform) => throw mode.Blocked("IAppleConnectService.RegisterDeviceAsync");
    Task IAppleConnectService.UpdateDeviceStatusAsync(string id, bool enabled) => throw mode.Blocked("IAppleConnectService.UpdateDeviceStatusAsync");
    Task<AppleCertificateCreateResult> IAppleConnectService.CreateCertificateAsync(string certificateType, string? commonName, string? passphrase) => throw mode.Blocked("IAppleConnectService.CreateCertificateAsync");
    Task IAppleConnectService.RevokeCertificateAsync(string id) => throw mode.Blocked("IAppleConnectService.RevokeCertificateAsync");
    Task<AppleProfile> IAppleConnectService.CreateProfileAsync(AppleProfileCreateRequest request) => throw mode.Blocked("IAppleConnectService.CreateProfileAsync");
    Task<AppleProfile> IAppleConnectService.RegenerateProfileAsync(string existingProfileId, AppleProfileCreateRequest request) => throw mode.Blocked("IAppleConnectService.RegenerateProfileAsync");
    Task<byte[]> IAppleConnectService.DownloadProfileAsync(string id) => throw mode.Blocked("IAppleConnectService.DownloadProfileAsync");
    Task<byte[]> IAppleConnectService.DownloadProfileForIdentityAsync(string id, string identityId) => throw mode.Blocked("IAppleConnectService.DownloadProfileForIdentityAsync");
    Task IAppleConnectService.DeleteProfileAsync(string id) => throw mode.Blocked("IAppleConnectService.DeleteProfileAsync");
    Task<string> IAppleConnectService.InstallProfileAsync(string id) => throw mode.Blocked("IAppleConnectService.InstallProfileAsync");
    Task<int> IAppleConnectService.InstallProfilesAsync(IEnumerable<string> ids, IProgress<string>? progress) => throw mode.Blocked("IAppleConnectService.InstallProfilesAsync");
    Task<AppleProfileDetails> IAppleConnectService.GetProfileDetailsAsync(string profileId) => throw mode.Blocked("IAppleConnectService.GetProfileDetailsAsync");
    IReadOnlyList<AppleProfile> ICertificateRenewalService.FindProfilesUsingCertificate(IEnumerable<AppleProfile> profiles, string certificateId) => throw mode.Blocked("ICertificateRenewalService.FindProfilesUsingCertificate");
    IReadOnlyList<AppleCertificate> ICertificateRenewalService.FindReplacementCandidates(IEnumerable<AppleCertificate> certificates, AppleCertificate certificate, DateTime? now) => throw mode.Blocked("ICertificateRenewalService.FindReplacementCandidates");
    Task<ProfileRenewalResult> ICertificateRenewalService.RepointProfileAsync(AppleProfile profile, string oldCertificateId, string newCertificateId, bool removeOldInstalledCopies, IProgress<string>? progress, CancellationToken cancellationToken) => throw mode.Blocked("ICertificateRenewalService.RepointProfileAsync");
    IReadOnlyList<AppleProfile> IProfileDeviceService.FindProfilesForDevice(IEnumerable<AppleProfile> profiles, AppleDevice device) => throw mode.Blocked("IProfileDeviceService.FindProfilesForDevice");
    Task<ProfileDeviceResult> IProfileDeviceService.AddDeviceToProfileAsync(AppleProfile profile, AppleDevice device, bool removeOldInstalledCopies, IProgress<string>? progress, CancellationToken cancellationToken) => throw mode.Blocked("IProfileDeviceService.AddDeviceToProfileAsync");
    IReadOnlyList<AppleRootCertInfo> IAppleRootCertService.GetAvailableCertificates() => throw mode.Blocked("IAppleRootCertService.GetAvailableCertificates");
    Task<IReadOnlyList<InstalledCertInfo>> IAppleRootCertService.GetInstalledAppleCertsAsync() => throw mode.Blocked("IAppleRootCertService.GetInstalledAppleCertsAsync");
    Task<bool> IAppleRootCertService.IsCertificateInstalledAsync(string namePattern) => throw mode.Blocked("IAppleRootCertService.IsCertificateInstalledAsync");
    Task<bool> IAppleRootCertService.InstallCertificateAsync(AppleRootCertInfo cert, IProgress<string>? progress) => throw mode.Blocked("IAppleRootCertService.InstallCertificateAsync");
    bool IAppleRootCertService.IsSupported { get => throw mode.Blocked("IAppleRootCertService.IsSupported"); }

    Task<bool> IAppleRootCertService.EnsureCertsCachedAsync(IProgress<string>? progress) => throw mode.Blocked("IAppleRootCertService.EnsureCertsCachedAsync");
    bool IAppleRootCertService.IsCacheReady { get => throw mode.Blocked("IAppleRootCertService.IsCacheReady"); }

    IReadOnlyDictionary<string, MauiSherpa.Core.Services.CachedCertInfo>? IAppleRootCertService.GetCachedCerts() => throw mode.Blocked("IAppleRootCertService.GetCachedCerts");
    Task<bool> IXcodeService.SelectXcodeAsync(string xcodeAppPath) => throw mode.Blocked("IXcodeService.SelectXcodeAsync");
    Task<bool> IXcodeService.AcceptLicenseAsync() => throw mode.Blocked("IXcodeService.AcceptLicenseAsync");
    Task<bool> IXcodeService.InstallCommandLineToolsAsync() => throw mode.Blocked("IXcodeService.InstallCommandLineToolsAsync");
    Task<bool> IXcodeService.UninstallXcodeAsync(string xcodeAppPath) => throw mode.Blocked("IXcodeService.UninstallXcodeAsync");
    Task<bool> IXcodeService.DownloadXcodeAsync(XcodeRelease release, string destinationPath, IProgress<XcodeDownloadProgress>? progress, CancellationToken ct) => throw mode.Blocked("IXcodeService.DownloadXcodeAsync");
    Task IXcodeService.ValidateArchiveSupportAsync(string archivePath, CancellationToken ct) => throw mode.Blocked("IXcodeService.ValidateArchiveSupportAsync");
    Task<bool> IXcodeService.InstallXcodeAsync(string archivePath, string? targetDirectory, IProgress<string>? progress, CancellationToken ct) => throw mode.Blocked("IXcodeService.InstallXcodeAsync");
    Task<bool> IXcodeService.NormalizeBundleNamesAsync(XcodeNormalizationPlan plan, IProgress<string>? progress, CancellationToken ct) => throw mode.Blocked("IXcodeService.NormalizeBundleNamesAsync");
    Task<AppleAuthResult> IAppleDownloadAuthService.AuthenticateAsync(string appleId, string password) => throw mode.Blocked("IAppleDownloadAuthService.AuthenticateAsync");
    Task<AppleAuthResult> IAppleDownloadAuthService.SubmitTwoFactorCodeAsync(string code, TwoFactorMethod? method) => throw mode.Blocked("IAppleDownloadAuthService.SubmitTwoFactorCodeAsync");
    Task<bool> IAppleDownloadAuthService.RequestSmsCodeAsync(TwoFactorMethod phone) => throw mode.Blocked("IAppleDownloadAuthService.RequestSmsCodeAsync");
    Task<bool> IAppleDownloadAuthService.ValidateSessionAsync() => throw mode.Blocked("IAppleDownloadAuthService.ValidateSessionAsync");
    HttpClient IAppleDownloadAuthService.CreateAuthenticatedHttpClient() => throw mode.Blocked("IAppleDownloadAuthService.CreateAuthenticatedHttpClient");
    System.Net.CookieCollection IAppleDownloadAuthService.GetAllCookies() => throw mode.Blocked("IAppleDownloadAuthService.GetAllCookies");
    Task IAppleDownloadAuthService.SignOutAsync() => throw mode.Blocked("IAppleDownloadAuthService.SignOutAsync");
    Task<IReadOnlyList<FastlaneSessionInfo>> IAppleDownloadAuthService.DetectFastlaneSessionsAsync() => throw mode.Blocked("IAppleDownloadAuthService.DetectFastlaneSessionsAsync");
    Task<AppleAuthResult> IAppleDownloadAuthService.ImportFastlaneSessionAsync(string appleId, string cookieFilePath) => throw mode.Blocked("IAppleDownloadAuthService.ImportFastlaneSessionAsync");
    Task<AppleAuthResult> IAppleDownloadAuthService.ImportCookiesAsync(string? appleId, IReadOnlyList<AppleAuthCookie> cookies) => throw mode.Blocked("IAppleDownloadAuthService.ImportCookiesAsync");
    event Action? IAppleDownloadAuthService.AuthStateChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    Task<bool> ISimulatorService.InstallRuntimeAsync(string dmgPath, IProgress<string>? progress) => throw mode.Blocked("ISimulatorService.InstallRuntimeAsync");
    Task<bool> ISimulatorService.DeleteRuntimeAsync(string deleteIdentifier, IProgress<string>? progress) => throw mode.Blocked("ISimulatorService.DeleteRuntimeAsync");
    Task<bool> ISimulatorService.CreateSimulatorAsync(string name, string deviceTypeId, string runtimeId, IProgress<string>? progress) => throw mode.Blocked("ISimulatorService.CreateSimulatorAsync");
    Task<bool> ISimulatorService.DeleteSimulatorAsync(string udid, IProgress<string>? progress) => throw mode.Blocked("ISimulatorService.DeleteSimulatorAsync");
    Task<bool> ISimulatorService.BootSimulatorAsync(string udid, IProgress<string>? progress) => throw mode.Blocked("ISimulatorService.BootSimulatorAsync");
    Task<bool> ISimulatorService.ShutdownSimulatorAsync(string udid, IProgress<string>? progress) => throw mode.Blocked("ISimulatorService.ShutdownSimulatorAsync");
    Task<bool> ISimulatorService.EraseSimulatorAsync(string udid, IProgress<string>? progress) => throw mode.Blocked("ISimulatorService.EraseSimulatorAsync");
    Task<IReadOnlyList<SimulatorApp>> ISimulatorService.GetInstalledAppsAsync(string udid) => throw mode.Blocked("ISimulatorService.GetInstalledAppsAsync");
    Task<bool> ISimulatorService.InstallAppAsync(string udid, string appPath, IProgress<string>? progress) => throw mode.Blocked("ISimulatorService.InstallAppAsync");
    Task<bool> ISimulatorService.UninstallAppAsync(string udid, string bundleId, IProgress<string>? progress) => throw mode.Blocked("ISimulatorService.UninstallAppAsync");
    Task<bool> ISimulatorService.LaunchAppAsync(string udid, string bundleId, IProgress<string>? progress) => throw mode.Blocked("ISimulatorService.LaunchAppAsync");
    Task<bool> ISimulatorService.TerminateAppAsync(string udid, string bundleId, IProgress<string>? progress) => throw mode.Blocked("ISimulatorService.TerminateAppAsync");
    Task<bool> ISimulatorService.TakeScreenshotAsync(string udid, string outputPath, IProgress<string>? progress) => throw mode.Blocked("ISimulatorService.TakeScreenshotAsync");
    Task<string?> ISimulatorService.GetAppContainerPathAsync(string udid, string bundleId, string containerType) => throw mode.Blocked("ISimulatorService.GetAppContainerPathAsync");
    Task<bool> ISimulatorService.OpenUrlAsync(string udid, string url, IProgress<string>? progress) => throw mode.Blocked("ISimulatorService.OpenUrlAsync");
    string ISimulatorService.GetSimulatorDataPath(string udid) => throw mode.Blocked("ISimulatorService.GetSimulatorDataPath");
    Task<bool> ISimulatorService.CloneSimulatorAsync(string udid, string newName, IProgress<string>? progress) => throw mode.Blocked("ISimulatorService.CloneSimulatorAsync");
    Task<bool> ISimulatorService.SendPushNotificationAsync(string udid, string bundleId, string payloadJson, IProgress<string>? progress) => throw mode.Blocked("ISimulatorService.SendPushNotificationAsync");
    Task<bool> ISimulatorService.SetLocationAsync(string udid, double latitude, double longitude, IProgress<string>? progress) => throw mode.Blocked("ISimulatorService.SetLocationAsync");
    Task<bool> ISimulatorService.ClearLocationAsync(string udid, IProgress<string>? progress) => throw mode.Blocked("ISimulatorService.ClearLocationAsync");
    Task<bool> ISimulatorService.OverrideStatusBarAsync(string udid, StatusBarOverride overrides, IProgress<string>? progress) => throw mode.Blocked("ISimulatorService.OverrideStatusBarAsync");
    Task<bool> ISimulatorService.ClearStatusBarAsync(string udid, IProgress<string>? progress) => throw mode.Blocked("ISimulatorService.ClearStatusBarAsync");
    Task<bool> ISimulatorService.StartRoutePlaybackAsync(string udid, IReadOnlyList<Services.RouteWaypoint> waypoints, double speedMps, CancellationToken ct) => throw mode.Blocked("ISimulatorService.StartRoutePlaybackAsync");
    void ISimulatorService.StopRoutePlayback() => throw mode.Blocked("ISimulatorService.StopRoutePlayback");
    event Action? ISimulatorService.RoutePlaybackStateChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    Task<IReadOnlyList<AppPermission>> ISimulatorService.GetPermissionsAsync(string udid, bool includeSystemApps) => throw mode.Blocked("ISimulatorService.GetPermissionsAsync");
    Task<bool> ISimulatorService.GrantPermissionAsync(string udid, string bundleId, string simctlService, IProgress<string>? progress) => throw mode.Blocked("ISimulatorService.GrantPermissionAsync");
    Task<bool> ISimulatorService.RevokePermissionAsync(string udid, string bundleId, string simctlService, IProgress<string>? progress) => throw mode.Blocked("ISimulatorService.RevokePermissionAsync");
    Task<bool> ISimulatorService.ResetPermissionAsync(string udid, string bundleId, string simctlService, IProgress<string>? progress) => throw mode.Blocked("ISimulatorService.ResetPermissionAsync");
    Task<bool> ISimulatorService.ResetAllPermissionsAsync(string udid, IProgress<string>? progress) => throw mode.Blocked("ISimulatorService.ResetAllPermissionsAsync");
    Task<bool> IAndroidDeviceToolsService.SetLocationAsync(string serial, double latitude, double longitude) => throw mode.Blocked("IAndroidDeviceToolsService.SetLocationAsync");
    Task<bool> IAndroidDeviceToolsService.ClearLocationAsync(string serial) => throw mode.Blocked("IAndroidDeviceToolsService.ClearLocationAsync");
    Task<bool> IAndroidDeviceToolsService.StartRoutePlaybackAsync(string serial, IReadOnlyList<Services.RouteWaypoint> waypoints, double speedMps, CancellationToken ct) => throw mode.Blocked("IAndroidDeviceToolsService.StartRoutePlaybackAsync");
    void IAndroidDeviceToolsService.StopRoutePlayback() => throw mode.Blocked("IAndroidDeviceToolsService.StopRoutePlayback");
    Task<bool> IAndroidDeviceToolsService.SetBatteryLevelAsync(string serial, int level) => throw mode.Blocked("IAndroidDeviceToolsService.SetBatteryLevelAsync");
    Task<bool> IAndroidDeviceToolsService.SetBatteryStatusAsync(string serial, string status) => throw mode.Blocked("IAndroidDeviceToolsService.SetBatteryStatusAsync");
    Task<bool> IAndroidDeviceToolsService.ResetBatteryAsync(string serial) => throw mode.Blocked("IAndroidDeviceToolsService.ResetBatteryAsync");
    Task<bool> IAndroidDeviceToolsService.EnableDemoModeAsync(string serial) => throw mode.Blocked("IAndroidDeviceToolsService.EnableDemoModeAsync");
    Task<bool> IAndroidDeviceToolsService.SetDemoStatusAsync(string serial, AndroidDemoStatus status) => throw mode.Blocked("IAndroidDeviceToolsService.SetDemoStatusAsync");
    Task<bool> IAndroidDeviceToolsService.DisableDemoModeAsync(string serial) => throw mode.Blocked("IAndroidDeviceToolsService.DisableDemoModeAsync");
    Task<bool> IAndroidDeviceToolsService.OpenDeepLinkAsync(string serial, string url) => throw mode.Blocked("IAndroidDeviceToolsService.OpenDeepLinkAsync");
    Task<IReadOnlyList<AndroidPackageInfo>> IAndroidDeviceToolsService.GetInstalledPackagesAsync(string serial) => throw mode.Blocked("IAndroidDeviceToolsService.GetInstalledPackagesAsync");
    Task<bool> IAndroidDeviceToolsService.LaunchPackageAsync(string serial, string packageName) => throw mode.Blocked("IAndroidDeviceToolsService.LaunchPackageAsync");
    Task<bool> IAndroidDeviceToolsService.ForceStopPackageAsync(string serial, string packageName) => throw mode.Blocked("IAndroidDeviceToolsService.ForceStopPackageAsync");
    Task<bool> IAndroidDeviceToolsService.InstallApkAsync(string serial, string apkPath, IProgress<string>? progress) => throw mode.Blocked("IAndroidDeviceToolsService.InstallApkAsync");
    Task<bool> IAndroidDeviceToolsService.UninstallPackageAsync(string serial, string packageName, IProgress<string>? progress) => throw mode.Blocked("IAndroidDeviceToolsService.UninstallPackageAsync");
    Task<bool> IAndroidDeviceToolsService.ClearPackageDataAsync(string serial, string packageName) => throw mode.Blocked("IAndroidDeviceToolsService.ClearPackageDataAsync");
    bool IAndroidDeviceToolsService.IsPlayingRoute { get => throw mode.Blocked("IAndroidDeviceToolsService.IsPlayingRoute"); }

    event Action? IAndroidDeviceToolsService.RoutePlaybackStateChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    Task<ApplePairingResult> IAppleDevicePairingService.PairUsbAsync(string identifier, CancellationToken cancellationToken) => throw mode.Blocked("IAppleDevicePairingService.PairUsbAsync");
    Task<ApplePairingResult> IAppleDevicePairingService.VerifyAsync(string identifier, CancellationToken cancellationToken) => throw mode.Blocked("IAppleDevicePairingService.VerifyAsync");
    Task<ApplePairingResult> IAppleDevicePairingService.UnpairAsync(string identifier, CancellationToken cancellationToken) => throw mode.Blocked("IAppleDevicePairingService.UnpairAsync");
    Task<ApplePairingResult> IAppleDevicePairingService.PairWirelessAsync(IProgress<ApplePairingProgress> progress, CancellationToken cancellationToken) => throw mode.Blocked("IAppleDevicePairingService.PairWirelessAsync");
    Task<IReadOnlyList<PhysicalDeviceApp>> IPhysicalDeviceService.GetInstalledAppsAsync(string identifier) => throw mode.Blocked("IPhysicalDeviceService.GetInstalledAppsAsync");
    Task<bool> IPhysicalDeviceService.InstallAppAsync(string identifier, string appPath, IProgress<string>? progress) => throw mode.Blocked("IPhysicalDeviceService.InstallAppAsync");
    Task<bool> IPhysicalDeviceService.LaunchAppAsync(string identifier, string bundleId, IProgress<string>? progress) => throw mode.Blocked("IPhysicalDeviceService.LaunchAppAsync");
    Task<bool> IPhysicalDeviceService.UninstallAppAsync(string identifier, string bundleId, IProgress<string>? progress) => throw mode.Blocked("IPhysicalDeviceService.UninstallAppAsync");
    Task<bool> IPhysicalDeviceService.TerminateAppAsync(string identifier, string bundleId) => throw mode.Blocked("IPhysicalDeviceService.TerminateAppAsync");
    Task<string?> IPhysicalDeviceService.DownloadAppContainerAsync(string identifier, string bundleId, string outputDir, IProgress<string>? progress) => throw mode.Blocked("IPhysicalDeviceService.DownloadAppContainerAsync");
    Task<string?> IPhysicalDeviceService.TakeScreenshotAsync(string udid, string outputPath) => throw mode.Blocked("IPhysicalDeviceService.TakeScreenshotAsync");
    Task<bool> IPhysicalDeviceService.SetLocationAsync(string udid, double latitude, double longitude) => throw mode.Blocked("IPhysicalDeviceService.SetLocationAsync");
    Task<bool> IPhysicalDeviceService.ResetLocationAsync(string udid) => throw mode.Blocked("IPhysicalDeviceService.ResetLocationAsync");
    bool ISimulatorLogService.IsSupported { get => throw mode.Blocked("ISimulatorLogService.IsSupported"); }

    bool ISimulatorLogService.IsRunning { get => throw mode.Blocked("ISimulatorLogService.IsRunning"); }

    IReadOnlyList<SimulatorLogEntry> ISimulatorLogService.Entries { get => throw mode.Blocked("ISimulatorLogService.Entries"); }

    Task ISimulatorLogService.StartAsync(string udid, CancellationToken ct) => throw mode.Blocked("ISimulatorLogService.StartAsync");
    void ISimulatorLogService.Stop() => throw mode.Blocked("ISimulatorLogService.Stop");
    void ISimulatorLogService.Clear() => throw mode.Blocked("ISimulatorLogService.Clear");
    IAsyncEnumerable<SimulatorLogEntry> ISimulatorLogService.StreamAsync(CancellationToken ct) => throw mode.Blocked("ISimulatorLogService.StreamAsync");
    event Action? ISimulatorLogService.OnCleared
    {
        add
        {
        }

        remove
        {
        }
    }

    Task<byte[]> ILocalCertificateService.ExportP12Async(string identity, string password) => throw mode.Blocked("ILocalCertificateService.ExportP12Async");
    Task<byte[]> ILocalCertificateService.ExportP12Async(IReadOnlyList<LocalSigningIdentity> identities, string password) => throw mode.Blocked("ILocalCertificateService.ExportP12Async");
    Task<byte[]> ILocalCertificateService.ExportCertificateAsync(string serialNumber) => throw mode.Blocked("ILocalCertificateService.ExportCertificateAsync");
    Task ILocalCertificateService.DeleteCertificateAsync(string identity) => throw mode.Blocked("ILocalCertificateService.DeleteCertificateAsync");
    Task<bool> ILocalCertificateService.ImportP12Async(byte[] p12Data, string password, CancellationToken cancellationToken) => throw mode.Blocked("ILocalCertificateService.ImportP12Async");
    Task ILocalCertificateService.ImportPrivateKeyAsync(byte[] privateKeyDer) => throw mode.Blocked("ILocalCertificateService.ImportPrivateKeyAsync");
    Task<bool> IDoctorService.FixDependencyAsync(DependencyStatus dependency, IProgress<string>? progress) => throw mode.Blocked("IDoctorService.FixDependencyAsync");
    Task<bool> IDoctorService.UpdateWorkloadsAsync(string workloadSetVersion, IProgress<string>? progress) => throw mode.Blocked("IDoctorService.UpdateWorkloadsAsync");
    Task<ProcessRequest?> IDoctorService.GetWorkloadUpdateRequestAsync(string featureBand, string? workingDirectory, string workloadSetVersion, string? installRoot, string? architecture, string? sdkVersion, CancellationToken cancellationToken) => throw mode.Blocked("IDoctorService.GetWorkloadUpdateRequestAsync");
    Task<MauiSherpa.Workloads.Models.DotnetWorkloadSetPreview> IDotnetWorkloadService.GetWorkloadSetPreviewAsync(MauiSherpa.Workloads.Models.DotnetWorkloadTarget target, string workloadSetVersion, CancellationToken cancellationToken) => throw mode.Blocked("IDotnetWorkloadService.GetWorkloadSetPreviewAsync");
    ProcessRequest IDotnetWorkloadService.CreateInstallRequest(MauiSherpa.Workloads.Models.DotnetWorkloadTarget target, IReadOnlyList<string> workloadIds, string? workloadSetVersion) => throw mode.Blocked("IDotnetWorkloadService.CreateInstallRequest");
    ProcessRequest IDotnetWorkloadService.CreateUninstallRequest(MauiSherpa.Workloads.Models.DotnetWorkloadTarget target, IReadOnlyList<string> workloadIds) => throw mode.Blocked("IDotnetWorkloadService.CreateUninstallRequest");
    ProcessRequest IDotnetWorkloadService.CreateUpdateSetRequest(MauiSherpa.Workloads.Models.DotnetWorkloadTarget target, string workloadSetVersion) => throw mode.Blocked("IDotnetWorkloadService.CreateUpdateSetRequest");
    ProcessRequest IDotnetWorkloadService.CreateLatestSetUpdateRequest(MauiSherpa.Workloads.Models.DotnetWorkloadTarget target) => throw mode.Blocked("IDotnetWorkloadService.CreateLatestSetUpdateRequest");
    ProcessRequest IDotnetWorkloadService.CreateRepairRequest(MauiSherpa.Workloads.Models.DotnetWorkloadTarget target) => throw mode.Blocked("IDotnetWorkloadService.CreateRepairRequest");
    ProcessRequest IDotnetWorkloadService.CreateRestoreRequest(MauiSherpa.Workloads.Models.DotnetWorkloadTarget target, string projectDirectory) => throw mode.Blocked("IDotnetWorkloadService.CreateRestoreRequest");
    MauiSherpa.Workloads.Models.GlobalJsonWorkloadPinPreview IDotnetWorkloadService.PreviewProjectWorkloadPin(string projectDirectory, string? workloadSetVersion) => throw mode.Blocked("IDotnetWorkloadService.PreviewProjectWorkloadPin");
    Task IDotnetWorkloadService.ApplyProjectWorkloadPinAsync(MauiSherpa.Workloads.Models.GlobalJsonWorkloadPinPreview preview, CancellationToken cancellationToken) => throw mode.Blocked("IDotnetWorkloadService.ApplyProjectWorkloadPinAsync");
    void IDotnetWorkloadService.Invalidate(string? installRoot) => throw mode.Blocked("IDotnetWorkloadService.Invalidate");
    Task<bool> IDotnetUpService.EnsureInstalledAsync(bool force, IProgress<string>? progress, CancellationToken cancellationToken) => throw mode.Blocked("IDotnetUpService.EnsureInstalledAsync");
    Task<MauiSherpa.Workloads.Models.GlobalJsonResolution> IDotnetUpService.InspectProjectFolderAsync(string folderPath, MauiSherpa.Workloads.Models.DotnetUpListResult list, CancellationToken cancellationToken) => throw mode.Blocked("IDotnetUpService.InspectProjectFolderAsync");
    ProcessRequest IDotnetUpService.CreateProcessRequest(IReadOnlyList<string> arguments, string? title, string? description, string? workingDirectory, bool usePseudoTerminal, bool acceptsStandardInput) => throw mode.Blocked("IDotnetUpService.CreateProcessRequest");
    ProcessRequest IDotnetUpService.InstallForProjectRequest(string folderPath) => throw mode.Blocked("IDotnetUpService.InstallForProjectRequest");
    ProcessRequest IDotnetUpService.InstallSdkRequest(string? channel, bool terminalMode) => throw mode.Blocked("IDotnetUpService.InstallSdkRequest");
    ProcessRequest IDotnetUpService.UpdateSdksRequest() => throw mode.Blocked("IDotnetUpService.UpdateSdksRequest");
    ProcessRequest IDotnetUpService.UninstallSdkRequest(string channel, MauiSherpa.Workloads.Models.DotnetUpInstallSource? source, string? installPath) => throw mode.Blocked("IDotnetUpService.UninstallSdkRequest");
    ProcessRequest IDotnetUpService.InstallRuntimeRequest(string? spec) => throw mode.Blocked("IDotnetUpService.InstallRuntimeRequest");
    ProcessRequest IDotnetUpService.UpdateRuntimesRequest() => throw mode.Blocked("IDotnetUpService.UpdateRuntimesRequest");
    ProcessRequest IDotnetUpService.UninstallRuntimeRequest(string spec, string? installPath) => throw mode.Blocked("IDotnetUpService.UninstallRuntimeRequest");
    ProcessRequest IDotnetUpService.UpdateAllRequest() => throw mode.Blocked("IDotnetUpService.UpdateAllRequest");
    ProcessState IProcessExecutionService.CurrentState { get => throw mode.Blocked("IProcessExecutionService.CurrentState"); }

    int? IProcessExecutionService.ProcessId { get => throw mode.Blocked("IProcessExecutionService.ProcessId"); }

    event EventHandler<ProcessOutputEventArgs>? IProcessExecutionService.OutputReceived
    {
        add
        {
        }

        remove
        {
        }
    }

    event EventHandler<ProcessStateChangedEventArgs>? IProcessExecutionService.StateChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    Task<ProcessResult> IProcessExecutionService.ExecuteAsync(ProcessRequest request, CancellationToken cancellationToken) => throw mode.Blocked("IProcessExecutionService.ExecuteAsync");
    Task<bool> IProcessExecutionService.SendInputAsync(string data, CancellationToken cancellationToken) => throw mode.Blocked("IProcessExecutionService.SendInputAsync");
    void IProcessExecutionService.Cancel() => throw mode.Blocked("IProcessExecutionService.Cancel");
    void IProcessExecutionService.Kill() => throw mode.Blocked("IProcessExecutionService.Kill");
    string IProcessExecutionService.GetFullOutput() => throw mode.Blocked("IProcessExecutionService.GetFullOutput");
    Task ICopilotService.ConnectAsync() => throw mode.Blocked("ICopilotService.ConnectAsync");
    Task ICopilotService.DisconnectAsync() => throw mode.Blocked("ICopilotService.DisconnectAsync");
    Task ICopilotService.StartSessionAsync(string? model, string? systemPrompt) => throw mode.Blocked("ICopilotService.StartSessionAsync");
    Task<IReadOnlyList<CopilotModelOption>> ICopilotService.ListModelsAsync() => throw mode.Blocked("ICopilotService.ListModelsAsync");
    Task ICopilotService.SetModelAsync(string model) => throw mode.Blocked("ICopilotService.SetModelAsync");
    Task ICopilotService.EndSessionAsync() => throw mode.Blocked("ICopilotService.EndSessionAsync");
    Task ICopilotService.SendMessageAsync(string message) => throw mode.Blocked("ICopilotService.SendMessageAsync");
    Task ICopilotService.AbortAsync() => throw mode.Blocked("ICopilotService.AbortAsync");
    event Action<string>? ICopilotService.OnAssistantMessage
    {
        add
        {
        }

        remove
        {
        }
    }

    event Action<string>? ICopilotService.OnAssistantDelta
    {
        add
        {
        }

        remove
        {
        }
    }

    event Action? ICopilotService.OnSessionIdle
    {
        add
        {
        }

        remove
        {
        }
    }

    event Action<string>? ICopilotService.OnError
    {
        add
        {
        }

        remove
        {
        }
    }

    event Action<string, string>? ICopilotService.OnToolStart
    {
        add
        {
        }

        remove
        {
        }
    }

    event Action<string, string>? ICopilotService.OnToolComplete
    {
        add
        {
        }

        remove
        {
        }
    }

    event Action<string>? ICopilotService.OnReasoningStart
    {
        add
        {
        }

        remove
        {
        }
    }

    event Action<string, string>? ICopilotService.OnReasoningDelta
    {
        add
        {
        }

        remove
        {
        }
    }

    event Action? ICopilotService.OnTurnStart
    {
        add
        {
        }

        remove
        {
        }
    }

    event Action? ICopilotService.OnTurnEnd
    {
        add
        {
        }

        remove
        {
        }
    }

    event Action<string>? ICopilotService.OnIntentChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    event Action<CopilotUsageInfo>? ICopilotService.OnUsageInfoChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    event Action<CopilotSessionError>? ICopilotService.OnSessionError
    {
        add
        {
        }

        remove
        {
        }
    }

    void ICopilotService.AddUserMessage(string content) => throw mode.Blocked("ICopilotService.AddUserMessage");
    void ICopilotService.AddAssistantMessage(string content) => throw mode.Blocked("ICopilotService.AddAssistantMessage");
    void ICopilotService.AddReasoningMessage(string reasoningId) => throw mode.Blocked("ICopilotService.AddReasoningMessage");
    void ICopilotService.UpdateReasoningMessage(string reasoningId, string content) => throw mode.Blocked("ICopilotService.UpdateReasoningMessage");
    void ICopilotService.CompleteReasoningMessage(string? reasoningId) => throw mode.Blocked("ICopilotService.CompleteReasoningMessage");
    void ICopilotService.AddToolMessage(string toolName, string? toolCallId) => throw mode.Blocked("ICopilotService.AddToolMessage");
    void ICopilotService.SetToolCommand(string? toolName, string? toolCallId, string? command) => throw mode.Blocked("ICopilotService.SetToolCommand");
    void ICopilotService.CompleteToolMessage(string? toolName, string? toolCallId, bool success, string result) => throw mode.Blocked("ICopilotService.CompleteToolMessage");
    void ICopilotService.AddErrorMessage(CopilotChatMessage errorMessage) => throw mode.Blocked("ICopilotService.AddErrorMessage");
    void ICopilotService.ClearMessages() => throw mode.Blocked("ICopilotService.ClearMessages");
    Func<ToolPermissionRequest, Task<ToolPermissionResult>>? ICopilotService.PermissionHandler { get => throw mode.Blocked("ICopilotService.PermissionHandler"); set => throw mode.Blocked("ICopilotService.PermissionHandler"); }

    Func<CopilotUserInputRequest, Task<CopilotUserInputResponse>>? ICopilotService.UserInputHandler { get => throw mode.Blocked("ICopilotService.UserInputHandler"); set => throw mode.Blocked("ICopilotService.UserInputHandler"); }

    IReadOnlyList<Microsoft.Extensions.AI.AIFunction> ICopilotService.AdditionalTools { get => throw mode.Blocked("ICopilotService.AdditionalTools"); set => throw mode.Blocked("ICopilotService.AdditionalTools"); }

    event Action<GitHub.Copilot.SessionEvent>? ICopilotService.OnSessionEvent
    {
        add
        {
        }

        remove
        {
        }
    }

    Shiny.Controls.AgentUI.AgentRunner ICopilotAgentService.Runner { get => throw mode.Blocked("ICopilotAgentService.Runner"); }

    Shiny.Controls.AgentUI.AgentPromptQueue ICopilotAgentService.Prompts { get => throw mode.Blocked("ICopilotAgentService.Prompts"); }

    Shiny.Controls.AgentUI.A2UI.A2UICatalogRegistry ICopilotAgentService.Catalogs { get => throw mode.Blocked("ICopilotAgentService.Catalogs"); }

    void ICopilotAgentService.Reset() => throw mode.Blocked("ICopilotAgentService.Reset");
    IReadOnlyList<Microsoft.Extensions.AI.AIFunction> ICopilotToolsService.GetTools() => throw mode.Blocked("ICopilotToolsService.GetTools");
    CopilotTool? ICopilotToolsService.GetTool(string name) => throw mode.Blocked("ICopilotToolsService.GetTool");
    IReadOnlyList<string> ICopilotToolsService.ReadOnlyToolNames { get => throw mode.Blocked("ICopilotToolsService.ReadOnlyToolNames"); }

    Task<IReadOnlyList<ProfilingTargetInfo>> IProfilingContextService.GetAvailableTargetsAsync(CancellationToken ct) => throw mode.Blocked("IProfilingContextService.GetAvailableTargetsAsync");
    Task<ProfilingSnapshotResult> IProfilingContextService.GetSnapshotAsync(ProfilingSnapshotOptions options, CancellationToken ct) => throw mode.Blocked("IProfilingContextService.GetSnapshotAsync");
    CloudSecretsProviderType ICloudSecretsProvider.ProviderType { get => throw mode.Blocked("ICloudSecretsProvider.ProviderType"); }

    string ICloudSecretsProvider.DisplayName { get => throw mode.Blocked("ICloudSecretsProvider.DisplayName"); }

    Task<bool> ICloudSecretsProvider.TestConnectionAsync(CancellationToken cancellationToken) => throw mode.Blocked("ICloudSecretsProvider.TestConnectionAsync");
    Task<bool> ICloudSecretsProvider.StoreSecretAsync(string key, byte[] value, Dictionary<string, string>? metadata, CancellationToken cancellationToken) => throw mode.Blocked("ICloudSecretsProvider.StoreSecretAsync");
    Task<byte[]?> ICloudSecretsProvider.GetSecretAsync(string key, CancellationToken cancellationToken) => throw mode.Blocked("ICloudSecretsProvider.GetSecretAsync");
    Task<Dictionary<string, string>?> ICloudSecretsProvider.GetSecretMetadataAsync(string key, CancellationToken cancellationToken) => throw mode.Blocked("ICloudSecretsProvider.GetSecretMetadataAsync");
    Task<bool> ICloudSecretsProvider.SetSecretMetadataAsync(string key, Dictionary<string, string> metadata, CancellationToken cancellationToken) => throw mode.Blocked("ICloudSecretsProvider.SetSecretMetadataAsync");
    Task<bool> ICloudSecretsProvider.DeleteSecretAsync(string key, CancellationToken cancellationToken) => throw mode.Blocked("ICloudSecretsProvider.DeleteSecretAsync");
    Task<bool> ICloudSecretsProvider.SecretExistsAsync(string key, CancellationToken cancellationToken) => throw mode.Blocked("ICloudSecretsProvider.SecretExistsAsync");
    Task<IReadOnlyList<string>> ICloudSecretsProvider.ListSecretsAsync(string? prefix, CancellationToken cancellationToken) => throw mode.Blocked("ICloudSecretsProvider.ListSecretsAsync");
    Task<IReadOnlyList<string>> ICloudSecretsProvider.ListSecretsStrictAsync(string? prefix, CancellationToken cancellationToken) => throw mode.Blocked("ICloudSecretsProvider.ListSecretsStrictAsync");
    IReadOnlyList<CloudProviderSettingInfo> ICloudSecretsProviderFactory.GetProviderSettings(CloudSecretsProviderType providerType) => throw mode.Blocked("ICloudSecretsProviderFactory.GetProviderSettings");
    Task ISecretsProviderRegistry.SaveProviderAsync(CloudSecretsProviderConfig provider) => throw mode.Blocked("ISecretsProviderRegistry.SaveProviderAsync");
    Task ISecretsProviderRegistry.DeleteProviderAsync(string providerId) => throw mode.Blocked("ISecretsProviderRegistry.DeleteProviderAsync");
    Task<bool> ISecretsProviderRegistry.TestProviderConnectionAsync(string providerId) => throw mode.Blocked("ISecretsProviderRegistry.TestProviderConnectionAsync");
    event Action? ISecretsProviderRegistry.ProvidersChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    SecretItemKind ISecretItemAdapter.Kind { get => throw mode.Blocked("ISecretItemAdapter.Kind"); }

    bool ISecretItemAdapter.IsProviderOwned { get => throw mode.Blocked("ISecretItemAdapter.IsProviderOwned"); }

    Task<IReadOnlyList<SecretItemRef>> ISecretItemAdapter.ListLocalItemsAsync(CancellationToken cancellationToken) => throw mode.Blocked("ISecretItemAdapter.ListLocalItemsAsync");
    Task<IReadOnlyList<SecretItemRef>> ISecretItemAdapter.ListProviderItemsAsync(string providerId, CancellationToken cancellationToken) => throw mode.Blocked("ISecretItemAdapter.ListProviderItemsAsync");
    Task<IReadOnlySet<string>> ISecretItemAdapter.ListProviderItemIdsAsync(string providerId, CancellationToken cancellationToken) => throw mode.Blocked("ISecretItemAdapter.ListProviderItemIdsAsync");
    IReadOnlySet<string> ISecretItemAdapter.ExtractProviderItemIds(IReadOnlyList<string> providerKeys) => throw mode.Blocked("ISecretItemAdapter.ExtractProviderItemIds");
    Task<SecretItemPayload?> ISecretItemAdapter.ReadLocalAsync(SecretItemRef item, CancellationToken cancellationToken) => throw mode.Blocked("ISecretItemAdapter.ReadLocalAsync");
    Task<SecretItemPayload?> ISecretItemAdapter.ReadProviderAsync(SecretItemRef item, string providerId, CancellationToken cancellationToken) => throw mode.Blocked("ISecretItemAdapter.ReadProviderAsync");
    Task<bool> ISecretItemAdapter.WriteProviderAsync(SecretItemPayload payload, string providerId, CancellationToken cancellationToken) => throw mode.Blocked("ISecretItemAdapter.WriteProviderAsync");
    Task<bool> ISecretItemAdapter.DeleteProviderAsync(SecretItemRef item, string providerId, CancellationToken cancellationToken) => throw mode.Blocked("ISecretItemAdapter.DeleteProviderAsync");
    Task<IReadOnlyList<SecretItemSyncState>> ISecretSyncCoordinator.ListItemsAsync(SecretItemKind kind, CancellationToken cancellationToken) => throw mode.Blocked("ISecretSyncCoordinator.ListItemsAsync");
    Task<string?> ISecretSyncCoordinator.SetDefaultProvidersAsync(SecretItemRef item, CancellationToken cancellationToken) => throw mode.Blocked("ISecretSyncCoordinator.SetDefaultProvidersAsync");
    Task<string?> ISecretSyncCoordinator.SetDesiredProvidersAsync(SecretItemRef item, IReadOnlyCollection<string> providerIds, CancellationToken cancellationToken) => throw mode.Blocked("ISecretSyncCoordinator.SetDesiredProvidersAsync");
    Task<string?> ISecretSyncCoordinator.UpdateDesiredProvidersAsync(SecretItemRef item, IReadOnlyDictionary<string, bool> changes, CancellationToken cancellationToken) => throw mode.Blocked("ISecretSyncCoordinator.UpdateDesiredProvidersAsync");
    Task<string?> ISecretSyncCoordinator.MoveAsync(SecretItemRef previousItem, SecretItemRef item, IReadOnlyCollection<string> providerIds, string sourceProviderId, CancellationToken cancellationToken) => throw mode.Blocked("ISecretSyncCoordinator.MoveAsync");
    Task<string?> ISecretSyncCoordinator.ResolveConflictAsync(SecretItemRef item, string sourceProviderId, CancellationToken cancellationToken) => throw mode.Blocked("ISecretSyncCoordinator.ResolveConflictAsync");
    event Action<SecretItemRef>? ISecretSyncCoordinator.ItemStateChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    string IBackgroundTaskHandler.Type { get => throw mode.Blocked("IBackgroundTaskHandler.Type"); }

    Task IBackgroundTaskHandler.ExecuteAsync(BackgroundTaskRequest request, IBackgroundTaskContext context) => throw mode.Blocked("IBackgroundTaskHandler.ExecuteAsync");
    Task<string> IBackgroundTaskService.EnqueueAsync(BackgroundTaskRequest request, CancellationToken cancellationToken) => throw mode.Blocked("IBackgroundTaskService.EnqueueAsync");
    Task IBackgroundTaskService.RetryAsync(string taskId, CancellationToken cancellationToken) => throw mode.Blocked("IBackgroundTaskService.RetryAsync");
    Task IBackgroundTaskService.CancelAsync(string taskId) => throw mode.Blocked("IBackgroundTaskService.CancelAsync");
    Task IBackgroundTaskService.DismissAsync(string taskId, CancellationToken cancellationToken) => throw mode.Blocked("IBackgroundTaskService.DismissAsync");
    Task IBackgroundTaskService.ClearCompletedAsync(CancellationToken cancellationToken) => throw mode.Blocked("IBackgroundTaskService.ClearCompletedAsync");
    event Action? IBackgroundTaskService.TasksChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    Task<bool> IManagedSecretsService.CreateFolderAsync(string folderPath, CancellationToken cancellationToken) => throw mode.Blocked("IManagedSecretsService.CreateFolderAsync");
    Task<bool> IManagedSecretsService.RenameFolderAsync(string folderPath, string newFolderPath, CancellationToken cancellationToken) => throw mode.Blocked("IManagedSecretsService.RenameFolderAsync");
    Task<bool> IManagedSecretsService.DeleteFolderAsync(string folderPath, CancellationToken cancellationToken) => throw mode.Blocked("IManagedSecretsService.DeleteFolderAsync");
    Task<bool> IManagedSecretsService.CreateAsync(string key, byte[] value, ManagedSecretType type, string? description, string? originalFileName, Dictionary<string, string>? metadata, CancellationToken cancellationToken) => throw mode.Blocked("IManagedSecretsService.CreateAsync");
    Task<bool> IManagedSecretsService.UpdateAsync(string key, byte[]? value, string? description, Dictionary<string, string>? metadata, CancellationToken cancellationToken) => throw mode.Blocked("IManagedSecretsService.UpdateAsync");
    Task<bool> IManagedSecretsService.MoveAsync(string key, string newKey, byte[]? value, string? description, Dictionary<string, string>? metadata, CancellationToken cancellationToken) => throw mode.Blocked("IManagedSecretsService.MoveAsync");
    Task<bool> IManagedSecretsService.DeleteAsync(string key, CancellationToken cancellationToken) => throw mode.Blocked("IManagedSecretsService.DeleteAsync");
    Task ICloudSecretsService.SaveProviderAsync(CloudSecretsProviderConfig provider) => throw mode.Blocked("ICloudSecretsService.SaveProviderAsync");
    Task ICloudSecretsService.EnableDefaultLocalProviderAsync(bool setActiveProvider) => throw mode.Blocked("ICloudSecretsService.EnableDefaultLocalProviderAsync");
    Task ICloudSecretsService.DeleteProviderAsync(string providerId) => throw mode.Blocked("ICloudSecretsService.DeleteProviderAsync");
    Task<bool> ICloudSecretsService.TestProviderConnectionAsync(string providerId) => throw mode.Blocked("ICloudSecretsService.TestProviderConnectionAsync");
    Task<bool> ICloudSecretsService.StoreSecretAsync(string key, byte[] value, Dictionary<string, string>? metadata, CancellationToken cancellationToken) => throw mode.Blocked("ICloudSecretsService.StoreSecretAsync");
    Task<Dictionary<string, string>?> ICloudSecretsService.GetSecretMetadataAsync(string key, CancellationToken cancellationToken) => throw mode.Blocked("ICloudSecretsService.GetSecretMetadataAsync");
    Task<bool> ICloudSecretsService.SetSecretMetadataAsync(string key, Dictionary<string, string> metadata, CancellationToken cancellationToken) => throw mode.Blocked("ICloudSecretsService.SetSecretMetadataAsync");
    Task<bool> ICloudSecretsService.DeleteSecretAsync(string key, CancellationToken cancellationToken) => throw mode.Blocked("ICloudSecretsService.DeleteSecretAsync");
    Task<bool> ICertificateSyncService.UploadToCloudAsync(AppleCertificate certificate, byte[] p12Data, string password, CertificateSecretMetadata? metadata, CancellationToken cancellationToken) => throw mode.Blocked("ICertificateSyncService.UploadToCloudAsync");
    Task<bool> ICertificateSyncService.DownloadAndInstallAsync(string certificateId, CancellationToken cancellationToken) => throw mode.Blocked("ICertificateSyncService.DownloadAndInstallAsync");
    Task<bool> ICertificateSyncService.DownloadAndInstallBySerialAsync(string serialNumber, CancellationToken cancellationToken) => throw mode.Blocked("ICertificateSyncService.DownloadAndInstallBySerialAsync");
    Task<(byte[]? P12, string? Password)> ICertificateSyncService.GetCertificateSecretsAsync(string serialNumber, bool autoUploadFromKeychain, CancellationToken cancellationToken) => throw mode.Blocked("ICertificateSyncService.GetCertificateSecretsAsync");
    string ICertificateSyncService.GetCertificateSecretKey(string serialNumber) => throw mode.Blocked("ICertificateSyncService.GetCertificateSecretKey");
    string ICertificateSyncService.GetCertificatePasswordKey(string serialNumber) => throw mode.Blocked("ICertificateSyncService.GetCertificatePasswordKey");
    string ICertificateSyncService.GetCertificateMetadataKey(string serialNumber) => throw mode.Blocked("ICertificateSyncService.GetCertificateMetadataKey");
    Task<bool> ICertificateSyncService.DeleteFromCloudAsync(string serialNumber, CancellationToken cancellationToken) => throw mode.Blocked("ICertificateSyncService.DeleteFromCloudAsync");
    string ISecretsPublisher.ProviderId { get => throw mode.Blocked("ISecretsPublisher.ProviderId"); }

    string ISecretsPublisher.DisplayName { get => throw mode.Blocked("ISecretsPublisher.DisplayName"); }

    string ISecretsPublisher.IconClass { get => throw mode.Blocked("ISecretsPublisher.IconClass"); }

    Task<bool> ISecretsPublisher.TestConnectionAsync(CancellationToken cancellationToken) => throw mode.Blocked("ISecretsPublisher.TestConnectionAsync");
    Task<IReadOnlyList<PublisherRepository>> ISecretsPublisher.ListRepositoriesAsync(string? filter, CancellationToken cancellationToken) => throw mode.Blocked("ISecretsPublisher.ListRepositoriesAsync");
    Task<IReadOnlyList<string>> ISecretsPublisher.ListSecretsAsync(string repositoryId, CancellationToken cancellationToken) => throw mode.Blocked("ISecretsPublisher.ListSecretsAsync");
    Task ISecretsPublisher.PublishSecretAsync(string repositoryId, string secretName, string secretValue, CancellationToken cancellationToken) => throw mode.Blocked("ISecretsPublisher.PublishSecretAsync");
    Task ISecretsPublisher.PublishSecretsAsync(string repositoryId, IReadOnlyDictionary<string, string> secrets, IProgress<string>? progress, CancellationToken cancellationToken) => throw mode.Blocked("ISecretsPublisher.PublishSecretsAsync");
    Task ISecretsPublisher.DeleteSecretAsync(string repositoryId, string secretName, CancellationToken cancellationToken) => throw mode.Blocked("ISecretsPublisher.DeleteSecretAsync");
    IReadOnlyList<(string ProviderId, string DisplayName, string IconClass)> ISecretsPublisherFactory.GetAvailableProviders() => throw mode.Blocked("ISecretsPublisherFactory.GetAvailableProviders");
    ISecretsPublisher ISecretsPublisherFactory.CreatePublisher(SecretsPublisherConfig config) => throw mode.Blocked("ISecretsPublisherFactory.CreatePublisher");
    (bool IsValid, string? ErrorMessage) ISecretsPublisherFactory.ValidateConfig(string providerId, Dictionary<string, string> settings) => throw mode.Blocked("ISecretsPublisherFactory.ValidateConfig");
    IReadOnlyList<(string Key, string Label, string Type, bool Required, string? Placeholder)> ISecretsPublisherFactory.GetRequiredSettings(string providerId) => throw mode.Blocked("ISecretsPublisherFactory.GetRequiredSettings");
    Task ISecretsPublisherService.SavePublisherAsync(SecretsPublisherConfig config) => throw mode.Blocked("ISecretsPublisherService.SavePublisherAsync");
    Task ISecretsPublisherService.DeletePublisherAsync(string id) => throw mode.Blocked("ISecretsPublisherService.DeletePublisherAsync");
    Task<bool> ISecretsPublisherService.TestConnectionAsync(string publisherId, CancellationToken cancellationToken) => throw mode.Blocked("ISecretsPublisherService.TestConnectionAsync");
    ISecretsPublisher? ISecretsPublisherService.GetPublisherInstance(string publisherId) => throw mode.Blocked("ISecretsPublisherService.GetPublisherInstance");
    Task<IReadOnlyList<PublisherRepository>> ISecretsPublisherService.ListRepositoriesAsync(string publisherId, string? filter, CancellationToken cancellationToken) => throw mode.Blocked("ISecretsPublisherService.ListRepositoriesAsync");
    Task ISecretsPublisherService.PublishSecretsAsync(string publisherId, string repositoryId, IReadOnlyDictionary<string, string> secrets, IProgress<string>? progress, CancellationToken cancellationToken) => throw mode.Blocked("ISecretsPublisherService.PublishSecretsAsync");
    event Action? ISecretsPublisherService.OnPublishersChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    Task IPublishProfileService.SaveProfileAsync(PublishProfile profile) => throw mode.Blocked("IPublishProfileService.SaveProfileAsync");
    Task IPublishProfileService.DeleteProfileAsync(string id) => throw mode.Blocked("IPublishProfileService.DeleteProfileAsync");
    Task<Dictionary<string, string>> IPublishProfileService.ResolveSecretsAsync(PublishProfile profile, IProgress<string>? progress, CancellationToken ct) => throw mode.Blocked("IPublishProfileService.ResolveSecretsAsync");
    event Action? IPublishProfileService.OnProfilesChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    Task<PublishProfileHealthSnapshot> IPublishProfileHealthService.LoadSnapshotAsync(CancellationToken cancellationToken) => throw mode.Blocked("IPublishProfileHealthService.LoadSnapshotAsync");
    PublishProfileHealthReport IPublishProfileHealthService.Evaluate(PublishProfile profile, PublishProfileHealthSnapshot snapshot) => throw mode.Blocked("IPublishProfileHealthService.Evaluate");
    Task<IReadOnlyDictionary<string, PublishProfileHealthReport>> IPublishProfileHealthService.EvaluateAsync(IEnumerable<PublishProfile> profiles, CancellationToken cancellationToken) => throw mode.Blocked("IPublishProfileHealthService.EvaluateAsync");
    PublishProfile IPublishProfileHealthService.ApplyFixes(PublishProfile profile, IEnumerable<PublishProfileHealthIssue> issues) => throw mode.Blocked("IPublishProfileHealthService.ApplyFixes");
    PublishProfile IPublishProfileHealthService.Repoint(PublishProfile profile, IReadOnlyDictionary<string, AppleCertificate> certificateReplacements, IReadOnlyDictionary<string, AppleProfile> profileReplacements, IReadOnlyList<AppleProfile>? knownProfiles, IReadOnlyList<AppleCertificate> knownCertificates) => throw mode.Blocked("IPublishProfileHealthService.Repoint");
    void IPublishProfileHealthService.Invalidate() => throw mode.Blocked("IPublishProfileHealthService.Invalidate");
    Task IPublishProfileHealthService.RefreshAsync(CancellationToken cancellationToken) => throw mode.Blocked("IPublishProfileHealthService.RefreshAsync");
    void IPublishProfileHealthService.RecordCertificateReplacement(string oldSerialNumber, string newSerialNumber) => throw mode.Blocked("IPublishProfileHealthService.RecordCertificateReplacement");
    void IPublishProfileHealthService.RecordProvisioningProfileReplacement(string oldProfileId, string newProfileId) => throw mode.Blocked("IPublishProfileHealthService.RecordProvisioningProfileReplacement");
    Task<PublishProfile?> IPublishProfileHealthService.ApplyFixesAsync(PublishProfile profile, IEnumerable<PublishProfileHealthIssue> issues, CancellationToken cancellationToken) => throw mode.Blocked("IPublishProfileHealthService.ApplyFixesAsync");
    Task IPushProjectService.SaveProjectAsync(PushProject project) => throw mode.Blocked("IPushProjectService.SaveProjectAsync");
    Task IPushProjectService.DeleteProjectAsync(string id) => throw mode.Blocked("IPushProjectService.DeleteProjectAsync");
    Task<PushProject> IPushProjectService.DuplicateProjectAsync(string id) => throw mode.Blocked("IPushProjectService.DuplicateProjectAsync");
    Task IPushProjectService.AddHistoryEntryAsync(string projectId, PushSendHistoryEntry entry) => throw mode.Blocked("IPushProjectService.AddHistoryEntryAsync");
    Task IPushProjectService.ClearHistoryAsync(string projectId) => throw mode.Blocked("IPushProjectService.ClearHistoryAsync");
    event Action? IPushProjectService.OnProjectsChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    Task<byte[]> IBackupService.ExportSettingsAsync(string password, BackupExportSelection? selection) => throw mode.Blocked("IBackupService.ExportSettingsAsync");
    Task<BackupImportResult> IBackupService.ImportBackupAsync(byte[] encryptedData, string password) => throw mode.Blocked("IBackupService.ImportBackupAsync");
    Task<MauiSherpaSettings> IBackupService.ImportSettingsAsync(byte[] encryptedData, string password) => throw mode.Blocked("IBackupService.ImportSettingsAsync");
    Task<bool> IBackupService.ValidateBackupAsync(byte[] data) => throw mode.Blocked("IBackupService.ValidateBackupAsync");
    Task<BackupCertificateDiscoveryResult> IBackupService.GetBackupCertificatesAsync(CancellationToken cancellationToken) => throw mode.Blocked("IBackupService.GetBackupCertificatesAsync");
    Task<BackupRestoreResult> IBackupService.RestoreStoredItemsAsync(BackupImportResult importResult, CancellationToken cancellationToken) => throw mode.Blocked("IBackupService.RestoreStoredItemsAsync");
    Task<IReadOnlyList<GitHubRelease>> IUpdateService.GetAllReleasesAsync(CancellationToken cancellationToken) => throw mode.Blocked("IUpdateService.GetAllReleasesAsync");
    Task IUpdateService.DownloadAndApplyUpdateAsync(GitHubRelease release, IProgress<(double Percent, string Message)>? progress, CancellationToken cancellationToken) => throw mode.Blocked("IUpdateService.DownloadAndApplyUpdateAsync");
    void IUpdateService.DismissVersion(string version) => throw mode.Blocked("IUpdateService.DismissVersion");
    Task<AndroidKeystore> IKeystoreService.CreateKeystoreAsync(string alias, string keyPassword, string keystorePassword, string cn, string ou, string o, string l, string st, string c, int validityDays, string keyAlg, int keySize, string keystoreType) => throw mode.Blocked("IKeystoreService.CreateKeystoreAsync");
    Task<KeystoreInfo> IKeystoreService.GetKeystoreInfoAsync(string keystorePath, string password) => throw mode.Blocked("IKeystoreService.GetKeystoreInfoAsync");
    Task IKeystoreService.ExportPepkAsync(string keystorePath, string alias, string keystorePassword, string keyPassword, string encryptionKey, string outputPath) => throw mode.Blocked("IKeystoreService.ExportPepkAsync");
    Task<AndroidKeystore> IKeystoreService.AddKeystoreAsync(AndroidKeystore keystore, byte[] content, string? keystorePassword, string? keyPassword) => throw mode.Blocked("IKeystoreService.AddKeystoreAsync");
    Task IKeystoreService.RemoveKeystoreAsync(string id) => throw mode.Blocked("IKeystoreService.RemoveKeystoreAsync");
    Task<byte[]?> IKeystoreService.GetKeystoreContentAsync(string id) => throw mode.Blocked("IKeystoreService.GetKeystoreContentAsync");
    Task<IKeystoreFileLease> IKeystoreService.OpenKeystoreFileAsync(string id) => throw mode.Blocked("IKeystoreService.OpenKeystoreFileAsync");
    Task IKeystoreService.ExportKeystoreFileAsync(string id, string destinationPath) => throw mode.Blocked("IKeystoreService.ExportKeystoreFileAsync");
    Task<bool> IKeystoreService.VerifyKeyPasswordAsync(string keystorePath, string alias, string keystorePassword, string keyPassword) => throw mode.Blocked("IKeystoreService.VerifyKeyPasswordAsync");
    Task IKeystoreService.SaveKeystorePasswordAsync(string id, string password) => throw mode.Blocked("IKeystoreService.SaveKeystorePasswordAsync");
    Task IKeystoreService.SaveKeyPasswordAsync(string id, string password) => throw mode.Blocked("IKeystoreService.SaveKeyPasswordAsync");
    Task IKeystoreSyncService.UploadKeystoreToCloudAsync(string keystoreId, string password, CancellationToken ct) => throw mode.Blocked("IKeystoreSyncService.UploadKeystoreToCloudAsync");
    Task IKeystoreSyncService.UploadKeystoreFileAsync(string keystoreId, CancellationToken ct) => throw mode.Blocked("IKeystoreSyncService.UploadKeystoreFileAsync");
    Task IKeystoreSyncService.UploadKeystorePasswordAsync(string keystoreId, string password, CancellationToken ct) => throw mode.Blocked("IKeystoreSyncService.UploadKeystorePasswordAsync");
    Task IKeystoreSyncService.UploadKeystoreMetadataAsync(string keystoreId, CancellationToken ct) => throw mode.Blocked("IKeystoreSyncService.UploadKeystoreMetadataAsync");
    Task IKeystoreSyncService.DownloadKeystoreFromCloudAsync(string cloudKey, CancellationToken ct) => throw mode.Blocked("IKeystoreSyncService.DownloadKeystoreFromCloudAsync");
    Task IKeystoreSyncService.DeleteKeystoreFromCloudAsync(string alias, CancellationToken ct) => throw mode.Blocked("IKeystoreSyncService.DeleteKeystoreFromCloudAsync");
    Task<ApnsPushResult> IApnsPushService.SendPushAsync(ApnsPushRequest request, CancellationToken ct) => throw mode.Blocked("IApnsPushService.SendPushAsync");
    Task<AasaValidationResult> IDeepLinkValidationService.ValidateAppleAppSiteAssociationAsync(string domain) => throw mode.Blocked("IDeepLinkValidationService.ValidateAppleAppSiteAssociationAsync");
    Task<AssetLinksValidationResult> IDeepLinkValidationService.ValidateAssetLinksAsync(string domain) => throw mode.Blocked("IDeepLinkValidationService.ValidateAssetLinksAsync");
    void IDisposable.Dispose()
    {
    }
}
