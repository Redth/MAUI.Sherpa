namespace MauiSherpa.Core.Interfaces;

public interface IAlertService
{
    Task ShowAlertAsync(string title, string message, string? cancel = null);
    Task<bool> ShowConfirmAsync(string title, string message, string? confirm = null, string? cancel = null);
    Task ShowToastAsync(string message);
}

public interface ILoggingService
{
    void LogInformation(string message);
    void LogWarning(string message);
    void LogError(string message, Exception? exception = null);
    void LogDebug(string message);
    
    IReadOnlyList<LogEntry> GetRecentLogs(int maxCount = 500);
    void ClearLogs();
    event Action? OnLogAdded;
}

public record LogEntry(DateTime Timestamp, string Level, string Message);

public interface INavigationService
{
    Task NavigateToAsync(string route);
    Task NavigateBackAsync();
    Task<string?> GetCurrentRouteAsync();
}

public interface IThemeService
{
    string CurrentTheme { get; }
    bool IsDarkMode { get; }
    event Action? ThemeChanged;
    void SetTheme(string theme); // "Light", "Dark", or "System"
}

public interface IDialogService
{
    Task ShowLoadingAsync(string message);
    Task HideLoadingAsync();
    Task<string?> ShowInputDialogAsync(string title, string message, string placeholder = "");
    Task<string?> ShowFileDialogAsync(string title, bool isSave = false, string[]? filters = null, string? defaultFileName = null);
    Task<string?> PickFolderAsync(string title);
    Task CopyToClipboardAsync(string text);
}

public interface IFileSystemService
{
    Task<string?> ReadFileAsync(string path);
    Task WriteFileAsync(string path, string content);
    Task<bool> FileExistsAsync(string path);
    Task<bool> DirectoryExistsAsync(string path);
    Task<IReadOnlyList<string>> GetFilesAsync(string path, string searchPattern = "*");
    Task CreateDirectoryAsync(string path);
    Task DeleteFileAsync(string path);
    Task DeleteDirectoryAsync(string path);
    void RevealInFileManager(string path);
}

public interface ISecureStorageService
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    Task RemoveAsync(string key);
}

public interface IPlatformService
{
    bool IsWindows { get; }
    bool IsMacCatalyst { get; }
    string PlatformName { get; }
}

public interface IAndroidSdkService
{
    string? SdkPath { get; }
    bool IsSdkInstalled { get; }
    
    Task<bool> DetectSdkAsync();
    Task<bool> SetSdkPathAsync(string path);
    Task<string?> GetDefaultSdkPathAsync();
    Task<IReadOnlyList<SdkPackageInfo>> GetInstalledPackagesAsync();
    Task<IReadOnlyList<SdkPackageInfo>> GetAvailablePackagesAsync();
    Task<bool> InstallPackageAsync(string packagePath, IProgress<string>? progress = null);
    Task<bool> UninstallPackageAsync(string packagePath);
    Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync();
    Task<bool> AcquireSdkAsync(string? targetPath = null, IProgress<string>? progress = null);
    
    // AVD/Emulator methods
    Task<IReadOnlyList<AvdInfo>> GetAvdsAsync();
    Task<IReadOnlyList<AvdDeviceDefinition>> GetAvdDeviceDefinitionsAsync();
    Task<IReadOnlyList<string>> GetSystemImagesAsync();
    Task<IReadOnlyList<string>> GetAvdSkinsAsync();
    Task<bool> CreateAvdAsync(string name, string systemImage, EmulatorCreateOptions? options = null, IProgress<string>? progress = null);
    Task<bool> DeleteAvdAsync(string name);
    Task<bool> StartEmulatorAsync(string avdName, bool coldBoot = false, IProgress<string>? progress = null);
    Task<bool> StopEmulatorAsync(string serial);
    
    // Path change notification
    event Action? SdkPathChanged;
}

public interface IAndroidSdkSettingsService
{
    string? CustomSdkPath { get; }
    Task<string?> GetEffectiveSdkPathAsync();
    Task SetCustomSdkPathAsync(string? path);
    Task ResetToDefaultAsync();
    Task InitializeAsync();
    event Action? SdkPathChanged;
}

public record SdkPackageInfo(
    string Path,
    string Description,
    string? Version,
    string? Location,
    bool IsInstalled
);

public record DeviceInfo(
    string Serial,
    string State,
    string? Model,
    bool IsEmulator
);

public record AvdInfo(
    string Name,
    string? Device,
    string Path,
    string? Target,
    string? BasedOn,
    Dictionary<string, string> Properties
);

public record AvdDeviceDefinition(
    string Id,
    string Name,
    string? Oem,
    int? NumericId
);

public record EmulatorCreateOptions(
    string? Device = null,
    string? SdCardSize = null,
    string? Skin = null,
    string? CustomPath = null,
    int? RamSizeMb = null,
    int? InternalStorageMb = null
);

// Apple Identity & App Store Connect
public record AppleIdentity(
    string Id,
    string Name,
    string KeyId,
    string IssuerId,
    string? P8KeyPath,
    string? P8KeyContent
);

public record AppleBundleId(
    string Id,
    string Identifier,
    string Name,
    string Platform,
    string? SeedId,
    IReadOnlyList<AppleBundleIdCapability>? Capabilities = null
);

/// <summary>
/// Represents a capability enabled for a Bundle ID
/// </summary>
public record AppleBundleIdCapability(
    string Id,
    string CapabilityType
);

/// <summary>
/// Category groupings for capabilities
/// </summary>
public static class CapabilityCategories
{
    public static readonly IReadOnlyDictionary<string, string[]> Categories = new Dictionary<string, string[]>
    {
        ["App Services"] = new[] { "PUSH_NOTIFICATIONS", "ICLOUD", "IN_APP_PURCHASE", "GAME_CENTER", "APPLE_PAY", "WALLET" },
        ["App Capabilities"] = new[] { "HEALTHKIT", "HOMEKIT", "SIRIKIT", "NFC_TAG_READING", "CLASSKIT", "WEATHERKIT" },
        ["App Groups & Data"] = new[] { "APP_GROUPS", "ASSOCIATED_DOMAINS", "DATA_PROTECTION", "KEYCHAIN_SHARING" },
        ["Identity & Security"] = new[] { "SIGN_IN_WITH_APPLE", "APPLE_ID_AUTH", "APP_ATTEST", "AUTOFILL_CREDENTIAL_PROVIDER" },
        ["Network & Communication"] = new[] { "NETWORK_EXTENSIONS", "PERSONAL_VPN", "MULTIPATH", "HOT_SPOT", "ACCESS_WIFI_INFORMATION" },
        ["System"] = new[] { "MAPS", "INTER_APP_AUDIO", "WIRELESS_ACCESSORY_CONFIGURATION", "FONT_INSTALLATION", "DRIVER_KIT" }
    };
    
    public static string GetCategory(string capabilityType)
    {
        foreach (var (category, types) in Categories)
        {
            if (types.Contains(capabilityType))
                return category;
        }
        return "Other";
    }
    
    /// <summary>
    /// Human-readable names for capability types
    /// </summary>
    public static string GetDisplayName(string capabilityType) => capabilityType switch
    {
        "PUSH_NOTIFICATIONS" => "Push Notifications",
        "ICLOUD" => "iCloud",
        "IN_APP_PURCHASE" => "In-App Purchase",
        "GAME_CENTER" => "Game Center",
        "APPLE_PAY" => "Apple Pay",
        "WALLET" => "Wallet",
        "HEALTHKIT" => "HealthKit",
        "HOMEKIT" => "HomeKit",
        "SIRIKIT" => "SiriKit",
        "NFC_TAG_READING" => "NFC Tag Reading",
        "CLASSKIT" => "ClassKit",
        "WEATHERKIT" => "WeatherKit",
        "APP_GROUPS" => "App Groups",
        "ASSOCIATED_DOMAINS" => "Associated Domains",
        "DATA_PROTECTION" => "Data Protection",
        "KEYCHAIN_SHARING" => "Keychain Sharing",
        "SIGN_IN_WITH_APPLE" => "Sign in with Apple",
        "APPLE_ID_AUTH" => "Apple ID Authentication",
        "APP_ATTEST" => "App Attest",
        "AUTOFILL_CREDENTIAL_PROVIDER" => "AutoFill Credential Provider",
        "NETWORK_EXTENSIONS" => "Network Extensions",
        "PERSONAL_VPN" => "Personal VPN",
        "MULTIPATH" => "Multipath",
        "HOT_SPOT" => "Hotspot",
        "ACCESS_WIFI_INFORMATION" => "Access WiFi Information",
        "MAPS" => "Maps",
        "INTER_APP_AUDIO" => "Inter-App Audio",
        "WIRELESS_ACCESSORY_CONFIGURATION" => "Wireless Accessory Configuration",
        "FONT_INSTALLATION" => "Font Installation",
        "DRIVER_KIT" => "DriverKit",
        "COMMUNICATION_NOTIFICATIONS" => "Communication Notifications",
        "TIME_SENSITIVE_NOTIFICATIONS" => "Time Sensitive Notifications",
        "GROUP_ACTIVITIES" => "Group Activities",
        "FAMILY_CONTROLS" => "Family Controls",
        "EXPOSURE_NOTIFICATION" => "Exposure Notification",
        "EXTENDED_VIRTUAL_ADDRESSING" => "Extended Virtual Addressing",
        "INCREASED_MEMORY_LIMIT" => "Increased Memory Limit",
        "COREMEDIA_HLS_LOW_LATENCY" => "Low Latency HLS",
        "SYSTEM_EXTENSION_INSTALL" => "System Extension",
        "USER_MANAGEMENT" => "User Management",
        "MARZIPAN" => "Mac Catalyst",
        "CARPLAY_PLAYABLE_CONTENT" => "CarPlay",
        _ => capabilityType.Replace("_", " ").ToLowerInvariant()
            .Split(' ')
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..])
            .Aggregate((a, b) => $"{a} {b}")
    };
    
    /// <summary>
    /// Icon class for capability types (Font Awesome)
    /// </summary>
    public static string GetIcon(string capabilityType) => capabilityType switch
    {
        "PUSH_NOTIFICATIONS" => "fa-bell",
        "ICLOUD" => "fa-cloud",
        "IN_APP_PURCHASE" => "fa-credit-card",
        "GAME_CENTER" => "fa-gamepad",
        "APPLE_PAY" => "fa-apple-pay",
        "WALLET" => "fa-wallet",
        "HEALTHKIT" => "fa-heart-pulse",
        "HOMEKIT" => "fa-house",
        "SIRIKIT" => "fa-microphone",
        "NFC_TAG_READING" => "fa-wifi",
        "APP_GROUPS" => "fa-layer-group",
        "ASSOCIATED_DOMAINS" => "fa-link",
        "DATA_PROTECTION" => "fa-shield-halved",
        "KEYCHAIN_SHARING" => "fa-key",
        "SIGN_IN_WITH_APPLE" => "fa-apple",
        "APP_ATTEST" => "fa-certificate",
        "NETWORK_EXTENSIONS" => "fa-network-wired",
        "PERSONAL_VPN" => "fa-lock",
        "MAPS" => "fa-map",
        "WEATHERKIT" => "fa-cloud-sun",
        _ => "fa-puzzle-piece"
    };
}

public record AppleDevice(
    string Id,
    string Udid,
    string Name,
    string Platform,
    string DeviceClass,
    string Status,
    string? Model
);

public record AppleCertificate(
    string Id,
    string Name,
    string CertificateType,
    string Platform,
    DateTime ExpirationDate,
    string SerialNumber
);

public record AppleProfile(
    string Id,
    string Name,
    string ProfileType,
    string Platform,
    string State,
    DateTime ExpirationDate,
    string? BundleId,
    string Uuid
);

public interface IAppleIdentityService
{
    Task<IReadOnlyList<AppleIdentity>> GetIdentitiesAsync();
    Task<AppleIdentity?> GetIdentityAsync(string id);
    Task SaveIdentityAsync(AppleIdentity identity);
    Task DeleteIdentityAsync(string id);
    Task<bool> TestConnectionAsync(AppleIdentity identity);
}

public interface IAppleIdentityStateService
{
    AppleIdentity? SelectedIdentity { get; }
    event Action? OnSelectionChanged;
    void SetSelectedIdentity(AppleIdentity? identity);
}

public interface IAppleConnectService
{
    // Bundle IDs
    Task<IReadOnlyList<AppleBundleId>> GetBundleIdsAsync();
    Task<AppleBundleId> CreateBundleIdAsync(string identifier, string name, string platform, string? seedId = null);
    Task DeleteBundleIdAsync(string id);
    
    // Bundle ID Capabilities
    Task<IReadOnlyList<AppleBundleIdCapability>> GetBundleIdCapabilitiesAsync(string bundleIdResourceId);
    Task<IReadOnlyList<string>> GetAvailableCapabilityTypesAsync();
    Task EnableCapabilityAsync(string bundleIdResourceId, string capabilityType);
    Task DisableCapabilityAsync(string capabilityId);
    
    // Devices
    Task<IReadOnlyList<AppleDevice>> GetDevicesAsync();
    Task<AppleDevice> RegisterDeviceAsync(string udid, string name, string platform);
    Task UpdateDeviceStatusAsync(string id, bool enabled);
    
    // Certificates
    Task<IReadOnlyList<AppleCertificate>> GetCertificatesAsync();
    Task<AppleCertificateCreateResult> CreateCertificateAsync(string certificateType, string? commonName = null, string? passphrase = null);
    Task RevokeCertificateAsync(string id);
    
    // Provisioning Profiles
    Task<IReadOnlyList<AppleProfile>> GetProfilesAsync();
    Task<AppleProfile> CreateProfileAsync(AppleProfileCreateRequest request);
    Task<byte[]> DownloadProfileAsync(string id);
    Task DeleteProfileAsync(string id);
    Task<string> InstallProfileAsync(string id);
    Task<int> InstallProfilesAsync(IEnumerable<string> ids, IProgress<string>? progress = null);
}

/// <summary>
/// Request to create a new Apple provisioning profile
/// </summary>
public record AppleProfileCreateRequest(
    string Name,
    string ProfileType,              // e.g., "IOS_APP_DEVELOPMENT", "IOS_APP_STORE"
    string BundleIdResourceId,       // App Store Connect resource ID (not the identifier)
    IReadOnlyList<string> CertificateIds,
    IReadOnlyList<string>? DeviceIds // null or empty for App Store profiles
);

/// <summary>
/// Result of creating a new Apple certificate
/// </summary>
public record AppleCertificateCreateResult(
    string CertificateId,
    byte[] PfxData,
    DateTime ExpirationDate
);

// Apple Root/Intermediate Certificates (for macOS keychain management)
public record AppleRootCertInfo(
    string Name,
    string Url,
    string Type, // "Root" or "Intermediate"
    string? Description = null
);

public record InstalledCertInfo(
    string SubjectName,
    string IssuerName,
    string? SerialNumber,
    DateTime? ExpirationDate,
    bool IsAppleCert
);

public interface IAppleRootCertService
{
    /// <summary>
    /// Gets the list of Apple root and intermediate certificates available for download
    /// </summary>
    IReadOnlyList<AppleRootCertInfo> GetAvailableCertificates();
    
    /// <summary>
    /// Checks which Apple certificates are installed in the system keychain
    /// </summary>
    Task<IReadOnlyList<InstalledCertInfo>> GetInstalledAppleCertsAsync();
    
    /// <summary>
    /// Checks if a specific certificate is installed (by name pattern)
    /// </summary>
    Task<bool> IsCertificateInstalledAsync(string namePattern);
    
    /// <summary>
    /// Downloads and installs a certificate from Apple's servers
    /// </summary>
    Task<bool> InstallCertificateAsync(AppleRootCertInfo cert, IProgress<string>? progress = null);
    
    /// <summary>
    /// Gets whether this service is supported on the current platform
    /// </summary>
    bool IsSupported { get; }
    
    /// <summary>
    /// Ensures all certificates are downloaded and cached for serial number extraction.
    /// Caching starts automatically on service construction; this awaits completion.
    /// </summary>
    Task<bool> EnsureCertsCachedAsync(IProgress<string>? progress = null);
    
    /// <summary>
    /// Returns true if the certificate cache is ready.
    /// </summary>
    bool IsCacheReady { get; }
    
    /// <summary>
    /// Gets cached certificate info with serial numbers for precise matching.
    /// </summary>
    IReadOnlyDictionary<string, MauiSherpa.Core.Services.CachedCertInfo>? GetCachedCerts();
}

// ============================================================================
// Local Signing Identities - Keychain Certificate Management
// ============================================================================

/// <summary>
/// A signing identity from the local macOS keychain that includes the private key
/// </summary>
public record LocalSigningIdentity(
    string Identity,          // Full identity string (e.g., "Apple Development: Name (TEAM)")
    string CommonName,        // Certificate common name
    string? TeamId,           // Team ID extracted from identity
    string? SerialNumber,     // For matching with API certificates
    DateTime? ExpirationDate,
    bool IsValid,             // Valid according to security tool
    string? Hash = null       // SHA-1 hash from keychain (for looking up details)
);

/// <summary>
/// Service for managing local signing identities in the macOS keychain
/// </summary>
public interface ILocalCertificateService
{
    /// <summary>
    /// Gets all valid code signing identities from the local keychain
    /// </summary>
    Task<IReadOnlyList<LocalSigningIdentity>> GetSigningIdentitiesAsync();
    
    /// <summary>
    /// Checks if a certificate with the given serial number has a private key locally
    /// </summary>
    Task<bool> HasPrivateKeyAsync(string serialNumber);
    
    /// <summary>
    /// Exports a signing identity as a P12/PFX file
    /// </summary>
    /// <param name="identity">The full identity string</param>
    /// <param name="password">Password to protect the P12 file</param>
    /// <returns>P12 file contents</returns>
    Task<byte[]> ExportP12Async(string identity, string password);
    
    /// <summary>
    /// Exports a certificate (public key only) as a .cer file
    /// </summary>
    /// <param name="serialNumber">The certificate serial number</param>
    /// <returns>DER-encoded certificate data</returns>
    Task<byte[]> ExportCertificateAsync(string serialNumber);
    
    /// <summary>
    /// Deletes a certificate and its private key from the local keychain
    /// </summary>
    /// <param name="identity">The identity string or serial number</param>
    Task DeleteCertificateAsync(string identity);
    
    /// <summary>
    /// Invalidates the cached list of signing identities, forcing a refresh on next query
    /// </summary>
    void InvalidateCache();
    
    /// <summary>
    /// Gets whether this service is supported on the current platform
    /// </summary>
    bool IsSupported { get; }
}

// ============================================================================
// CI Secrets Wizard Models
// ============================================================================

/// <summary>
/// Platform selection for CI secrets wizard
/// </summary>
public enum ApplePlatformType
{
    iOS,
    MacCatalyst,
    macOS
}

/// <summary>
/// Distribution type for CI secrets wizard
/// </summary>
public enum AppleDistributionType
{
    Development,
    AdHoc,        // iOS only
    AppStore,
    Direct        // Mac Catalyst / macOS only (Developer ID)
}

/// <summary>
/// State for the CI secrets wizard
/// </summary>
public record CISecretsWizardState
{
    public ApplePlatformType Platform { get; init; }
    public AppleDistributionType Distribution { get; init; }
    public bool NeedsInstallerCert { get; init; }
    
    // Selected resources
    public AppleBundleId? SelectedBundleId { get; init; }
    public AppleCertificate? SigningCertificate { get; init; }
    public AppleCertificate? InstallerCertificate { get; init; }
    public AppleProfile? ProvisioningProfile { get; init; }
    
    // Local signing identity (with private key)
    public LocalSigningIdentity? LocalSigningIdentity { get; init; }
    public LocalSigningIdentity? LocalInstallerIdentity { get; init; }
    
    // Notarization (for Direct Distribution)
    public string? NotarizationAppleId { get; init; }
    public string? NotarizationPassword { get; init; }
    public string? NotarizationTeamId { get; init; }
}

/// <summary>
/// A secret to be exported for CI configuration
/// </summary>
public record CISecretExport(
    string Name,           // Recommended secret name (e.g., "APPLE_CERTIFICATE_P12")
    string Value,          // The actual secret value (base64 encoded, etc.)
    string Description,    // Human-readable description
    bool IsSensitive       // Whether to mask in UI
);

// ============================================================================
// MAUI Doctor Service - SDK/Workload Health Checking
// ============================================================================

/// <summary>
/// Context for doctor checks - determines SDK path and version constraints
/// </summary>
public record DoctorContext(
    string? WorkingDirectory,
    string? DotNetSdkPath,
    string? GlobalJsonPath,
    string? PinnedSdkVersion,
    string? PinnedWorkloadSetVersion,
    string? EffectiveFeatureBand
);

/// <summary>
/// Status of a dependency check
/// </summary>
public enum DependencyStatusType
{
    Ok,
    Warning,
    Error,
    Unknown
}

/// <summary>
/// Category of dependency for grouping in UI
/// </summary>
public enum DependencyCategory
{
    DotNetSdk,
    Workload,
    Jdk,
    AndroidSdk,
    Xcode,
    WindowsAppSdk,
    WindowsSdkBuildTools,
    WebView2,
    Other
}

/// <summary>
/// Status of an individual dependency
/// </summary>
public record DependencyStatus(
    string Name,
    DependencyCategory Category,
    string? RequiredVersion,
    string? RecommendedVersion,
    string? InstalledVersion,
    DependencyStatusType Status,
    string? Message,
    bool IsFixable,
    string? FixAction = null
);

/// <summary>
/// Information about an installed workload manifest
/// </summary>
public record WorkloadManifestInfo(
    string ManifestId,
    string Version,
    string? Description,
    int WorkloadCount,
    int PackCount
);

/// <summary>
/// Summary of installed SDK version
/// </summary>
public record SdkVersionInfo(
    string Version,
    string FeatureBand,
    int Major,
    int Minor,
    bool IsPreview
);

/// <summary>
/// Complete doctor report
/// </summary>
public record DoctorReport(
    DoctorContext Context,
    IReadOnlyList<SdkVersionInfo> InstalledSdks,
    IReadOnlyList<SdkVersionInfo>? AvailableSdkVersions,
    string? InstalledWorkloadSetVersion,
    IReadOnlyList<string>? AvailableWorkloadSetVersions,
    IReadOnlyList<WorkloadManifestInfo> Manifests,
    IReadOnlyList<DependencyStatus> Dependencies,
    DateTime Timestamp
)
{
    public bool HasErrors => Dependencies.Any(d => d.Status == DependencyStatusType.Error);
    public bool HasWarnings => Dependencies.Any(d => d.Status == DependencyStatusType.Warning);
    public int OkCount => Dependencies.Count(d => d.Status == DependencyStatusType.Ok);
    public int WarningCount => Dependencies.Count(d => d.Status == DependencyStatusType.Warning);
    public int ErrorCount => Dependencies.Count(d => d.Status == DependencyStatusType.Error);
}

/// <summary>
/// Service for checking MAUI development environment health
/// </summary>
public interface IDoctorService
{
    /// <summary>
    /// Gets the context for doctor checks based on a working directory.
    /// Looks for .dotnet local SDK and global.json in the directory and parents.
    /// </summary>
    Task<DoctorContext> GetContextAsync(string? workingDirectory = null);
    
    /// <summary>
    /// Runs a complete doctor check and returns the report.
    /// </summary>
    Task<DoctorReport> RunDoctorAsync(DoctorContext? context = null, IProgress<string>? progress = null);
    
    /// <summary>
    /// Gets available workload set versions for a feature band.
    /// </summary>
    Task<IReadOnlyList<string>> GetAvailableWorkloadSetVersionsAsync(string featureBand, bool includePrerelease = false);
    
    /// <summary>
    /// Attempts to fix a dependency issue.
    /// </summary>
    Task<bool> FixDependencyAsync(DependencyStatus dependency, IProgress<string>? progress = null);
    
    /// <summary>
    /// Installs or updates workloads to a specific workload set version.
    /// </summary>
    Task<bool> UpdateWorkloadsAsync(string workloadSetVersion, IProgress<string>? progress = null);
}

// ============================================================================
// Process Execution Service - CLI Tool Execution with Terminal UI
// ============================================================================

/// <summary>
/// State of a process execution
/// </summary>
public enum ProcessState
{
    Pending,
    AwaitingConfirmation,
    Running,
    Completed,
    Cancelled,
    Killed,
    Failed
}

/// <summary>
/// Request to execute a CLI process
/// </summary>
public record ProcessRequest(
    string Command,
    string[] Arguments,
    string? WorkingDirectory = null,
    bool RequiresElevation = false,
    string? ElevationPrompt = null,
    Dictionary<string, string>? Environment = null,
    string? Title = null,
    string? Description = null
)
{
    /// <summary>
    /// Gets the full command line string for display
    /// </summary>
    public string CommandLine => Arguments.Length > 0 
        ? $"{Command} {string.Join(" ", Arguments)}" 
        : Command;
}

/// <summary>
/// Result of a process execution
/// </summary>
public record ProcessResult(
    int ExitCode,
    string Output,
    string Error,
    TimeSpan Duration,
    ProcessState FinalState
)
{
    public bool Success => ExitCode == 0 && FinalState == ProcessState.Completed;
    public bool WasCancelled => FinalState == ProcessState.Cancelled;
    public bool WasKilled => FinalState == ProcessState.Killed;
}

/// <summary>
/// Event args for process output
/// </summary>
public class ProcessOutputEventArgs : EventArgs
{
    public string Data { get; }
    public bool IsError { get; }
    public DateTime Timestamp { get; }

    public ProcessOutputEventArgs(string data, bool isError = false)
    {
        Data = data;
        IsError = isError;
        Timestamp = DateTime.Now;
    }
}

/// <summary>
/// Event args for process state changes
/// </summary>
public class ProcessStateChangedEventArgs : EventArgs
{
    public ProcessState OldState { get; }
    public ProcessState NewState { get; }

    public ProcessStateChangedEventArgs(ProcessState oldState, ProcessState newState)
    {
        OldState = oldState;
        NewState = newState;
    }
}

/// <summary>
/// Service for executing CLI processes with streaming output
/// </summary>
public interface IProcessExecutionService
{
    /// <summary>
    /// Current state of the process
    /// </summary>
    ProcessState CurrentState { get; }
    
    /// <summary>
    /// The process ID if running
    /// </summary>
    int? ProcessId { get; }
    
    /// <summary>
    /// Event fired when output is received
    /// </summary>
    event EventHandler<ProcessOutputEventArgs>? OutputReceived;
    
    /// <summary>
    /// Event fired when the process state changes
    /// </summary>
    event EventHandler<ProcessStateChangedEventArgs>? StateChanged;
    
    /// <summary>
    /// Executes a process and returns the result when complete
    /// </summary>
    Task<ProcessResult> ExecuteAsync(ProcessRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sends a graceful cancellation signal (SIGINT/Ctrl+C)
    /// </summary>
    void Cancel();
    
    /// <summary>
    /// Force kills the process (SIGKILL)
    /// </summary>
    void Kill();
    
    /// <summary>
    /// Gets all output received so far
    /// </summary>
    string GetFullOutput();
}

/// <summary>
/// Service for displaying process execution in a modal dialog
/// </summary>
public interface IProcessModalService
{
    /// <summary>
    /// Shows the process execution modal with confirmation
    /// </summary>
    /// <param name="request">The process request</param>
    /// <param name="requireConfirmation">Whether to show confirmation before executing</param>
    /// <returns>The process result, or null if cancelled before execution</returns>
    Task<ProcessResult?> ShowProcessAsync(ProcessRequest request, bool requireConfirmation = true);
    
    /// <summary>
    /// Event fired when the modal is shown
    /// </summary>
    event Action? OnModalShown;
    
    /// <summary>
    /// Event fired when the modal is closed
    /// </summary>
    event Action? OnModalClosed;
}

// ============================================================================
// Operation Modal - Generic Progress Modal for API and Long-Running Operations
// ============================================================================

/// <summary>
/// State of an operation
/// </summary>
public enum OperationState
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// A log entry in an operation
/// </summary>
public record OperationLogEntry(
    DateTime Timestamp,
    string Message,
    OperationLogLevel Level = OperationLogLevel.Info
);

/// <summary>
/// Log level for operation entries
/// </summary>
public enum OperationLogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Success
}

/// <summary>
/// Result of an operation
/// </summary>
public record OperationResult(
    bool Success,
    string? Message,
    TimeSpan Duration,
    OperationState FinalState,
    IReadOnlyList<OperationLogEntry> Log
);

/// <summary>
/// Context for running an operation, providing progress reporting
/// </summary>
public interface IOperationContext
{
    /// <summary>
    /// Log a message
    /// </summary>
    void Log(string message, OperationLogLevel level = OperationLogLevel.Info);
    
    /// <summary>
    /// Log an info message
    /// </summary>
    void LogInfo(string message);
    
    /// <summary>
    /// Log a success message
    /// </summary>
    void LogSuccess(string message);
    
    /// <summary>
    /// Log a warning message
    /// </summary>
    void LogWarning(string message);
    
    /// <summary>
    /// Log an error message
    /// </summary>
    void LogError(string message);
    
    /// <summary>
    /// Set the current status text
    /// </summary>
    void SetStatus(string status);
    
    /// <summary>
    /// Set progress (0-100, or null for indeterminate)
    /// </summary>
    void SetProgress(int? percent);
    
    /// <summary>
    /// Cancellation token for the operation
    /// </summary>
    CancellationToken CancellationToken { get; }
    
    /// <summary>
    /// Whether cancellation has been requested
    /// </summary>
    bool IsCancellationRequested { get; }
}

/// <summary>
/// Service for showing operation progress in a modal
/// </summary>
public interface IOperationModalService
{
    /// <summary>
    /// Run an operation and show progress in a modal
    /// </summary>
    /// <param name="title">Title of the operation</param>
    /// <param name="description">Description of what the operation does</param>
    /// <param name="operation">The async operation to execute</param>
    /// <param name="canCancel">Whether the operation can be cancelled</param>
    /// <returns>The operation result</returns>
    Task<OperationResult> RunAsync(
        string title,
        string description,
        Func<IOperationContext, Task<bool>> operation,
        bool canCancel = true);
    
    /// <summary>
    /// Whether an operation is currently running
    /// </summary>
    bool IsRunning { get; }
    
    /// <summary>
    /// Event fired when the modal is shown
    /// </summary>
    event Action? OnModalShown;
    
    /// <summary>
    /// Event fired when the modal is closed
    /// </summary>
    event Action? OnModalClosed;
}

// ============================================================================
// Multi-Operation Modal - Batch Operations with Selection and Progress
// ============================================================================

/// <summary>
/// State of an individual operation item in a batch
/// </summary>
public enum OperationItemState
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped
}

/// <summary>
/// Defines an operation that can be run in a batch
/// </summary>
public record OperationItem(
    string Id,
    string Name,
    string Description,
    Func<IOperationContext, Task<bool>> Execute,
    bool IsEnabled = true,
    bool CanDisable = true
);

/// <summary>
/// Runtime state of an operation item
/// </summary>
public class OperationItemStatus
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public bool IsEnabled { get; set; } = true;
    public bool CanDisable { get; init; } = true;
    public OperationItemState State { get; set; } = OperationItemState.Pending;
    public List<OperationLogEntry> Log { get; } = new();
    public string? ErrorMessage { get; set; }
    public TimeSpan? Duration { get; set; }
}

/// <summary>
/// Result of a multi-operation batch
/// </summary>
public record MultiOperationResult(
    int TotalOperations,
    int Completed,
    int Failed,
    int Skipped,
    TimeSpan TotalDuration,
    IReadOnlyList<OperationItemStatus> OperationResults
)
{
    public bool AllSucceeded => Failed == 0 && Completed > 0;
    public bool HasFailures => Failed > 0;
}

/// <summary>
/// Service for running multiple operations with selection and progress
/// </summary>
public interface IMultiOperationModalService
{
    /// <summary>
    /// Show a multi-operation modal with operation selection
    /// </summary>
    /// <param name="title">Title of the batch operation</param>
    /// <param name="description">Description of what the batch does</param>
    /// <param name="operations">List of operations to show</param>
    /// <returns>The result of running the batch</returns>
    Task<MultiOperationResult> RunAsync(
        string title,
        string description,
        IEnumerable<OperationItem> operations);
    
    /// <summary>
    /// Whether a batch is currently running
    /// </summary>
    bool IsRunning { get; }
    
    /// <summary>
    /// Event fired when the modal is shown
    /// </summary>
    event Action? OnModalShown;
    
    /// <summary>
    /// Event fired when the modal is closed
    /// </summary>
    event Action? OnModalClosed;
}

/// <summary>
/// Service for interacting with GitHub Copilot
/// </summary>
public interface ICopilotService
{
    /// <summary>
    /// Whether the client is connected
    /// </summary>
    bool IsConnected { get; }
    
    /// <summary>
    /// Current session ID if a session is active
    /// </summary>
    string? CurrentSessionId { get; }
    
    /// <summary>
    /// Cached availability result from last check
    /// </summary>
    CopilotAvailability? CachedAvailability { get; }
    
    /// <summary>
    /// Check if the Copilot CLI is installed (caches result)
    /// </summary>
    Task<CopilotAvailability> CheckAvailabilityAsync(bool forceRefresh = false);
    
    /// <summary>
    /// Connect to the Copilot CLI
    /// </summary>
    Task ConnectAsync();
    
    /// <summary>
    /// Disconnect from the Copilot CLI
    /// </summary>
    Task DisconnectAsync();
    
    /// <summary>
    /// Start a new chat session
    /// </summary>
    /// <param name="model">Optional model to use</param>
    Task StartSessionAsync(string? model = null, string? systemPrompt = null);
    
    /// <summary>
    /// End the current chat session
    /// </summary>
    Task EndSessionAsync();
    
    /// <summary>
    /// Send a message to the current session
    /// </summary>
    /// <param name="message">The message to send</param>
    Task SendMessageAsync(string message);
    
    /// <summary>
    /// Abort the current message processing
    /// </summary>
    Task AbortAsync();
    
    /// <summary>
    /// Event fired when a message is received from the assistant
    /// </summary>
    event Action<string>? OnAssistantMessage;
    
    /// <summary>
    /// Event fired when a streaming delta is received
    /// </summary>
    event Action<string>? OnAssistantDelta;
    
    /// <summary>
    /// Event fired when the session becomes idle
    /// </summary>
    event Action? OnSessionIdle;
    
    /// <summary>
    /// Event fired when an error occurs
    /// </summary>
    event Action<string>? OnError;
    
    /// <summary>
    /// Event fired when tool execution starts
    /// </summary>
    event Action<string, string>? OnToolStart; // toolName, args
    
    /// <summary>
    /// Event fired when tool execution completes
    /// </summary>
    event Action<string, string>? OnToolComplete; // toolName, result
    
    /// <summary>
    /// Event fired when reasoning/thinking starts
    /// </summary>
    event Action<string>? OnReasoningStart; // reasoningId
    
    /// <summary>
    /// Event fired when reasoning delta is received
    /// </summary>
    event Action<string, string>? OnReasoningDelta; // reasoningId, content
    
    /// <summary>
    /// Event fired when assistant turn starts
    /// </summary>
    event Action? OnTurnStart;
    
    /// <summary>
    /// Event fired when assistant turn ends
    /// </summary>
    event Action? OnTurnEnd;
    
    /// <summary>
    /// Event fired when assistant intent changes (what Copilot is currently doing)
    /// </summary>
    event Action<string>? OnIntentChanged;
    
    /// <summary>
    /// Event fired when session usage info is updated
    /// </summary>
    event Action<CopilotUsageInfo>? OnUsageInfoChanged;
    
    /// <summary>
    /// Event fired when a session error occurs
    /// </summary>
    event Action<CopilotSessionError>? OnSessionError;
    
    /// <summary>
    /// Chat messages in the current session
    /// </summary>
    IReadOnlyList<CopilotChatMessage> Messages { get; }
    
    /// <summary>
    /// Add a user message to the chat history
    /// </summary>
    void AddUserMessage(string content);
    
    /// <summary>
    /// Add an assistant message to the chat history
    /// </summary>
    void AddAssistantMessage(string content);
    
    /// <summary>
    /// Add a reasoning/thinking message to the chat history
    /// </summary>
    void AddReasoningMessage(string reasoningId);
    
    /// <summary>
    /// Update a reasoning message with additional content
    /// </summary>
    void UpdateReasoningMessage(string reasoningId, string content);
    
    /// <summary>
    /// Mark a reasoning message as complete and collapse it
    /// </summary>
    void CompleteReasoningMessage(string? reasoningId = null);
    
    /// <summary>
    /// Add a tool call message to the chat history
    /// </summary>
    void AddToolMessage(string toolName, string? toolCallId = null);
    
    /// <summary>
    /// Mark a tool message as complete with result
    /// </summary>
    void CompleteToolMessage(string? toolName, string? toolCallId, bool success, string result);
    
    /// <summary>
    /// Add an error message to the chat history
    /// </summary>
    void AddErrorMessage(CopilotChatMessage errorMessage);
    
    /// <summary>
    /// Clear all chat messages
    /// </summary>
    void ClearMessages();
    
    /// <summary>
    /// Sets a delegate to handle permission requests for tool execution.
    /// The delegate receives the tool name, description, and the default result.
    /// Return the default result to accept default behavior, or a custom result to override.
    /// </summary>
    Func<ToolPermissionRequest, Task<ToolPermissionResult>>? PermissionHandler { get; set; }
}

/// <summary>
/// Information about a tool permission request
/// </summary>
public record ToolPermissionRequest(
    string ToolName,
    string ToolDescription,
    bool IsReadOnly,
    ToolPermissionResult DefaultResult,
    string? Command = null,
    string? Path = null
);

/// <summary>
/// Result of a tool permission request
/// </summary>
public record ToolPermissionResult(bool IsAllowed, string? DenialReason = null);

/// <summary>
/// A chat message in a Copilot conversation
/// </summary>
public record CopilotChatMessage
{
    public string Content { get; set; } = "";
    public bool IsUser { get; init; }
    public CopilotMessageType MessageType { get; init; } = CopilotMessageType.Text;
    public string? ToolName { get; init; }
    public string? ToolCallId { get; init; }
    public bool IsComplete { get; set; }
    public bool IsSuccess { get; set; } = true;
    public bool IsCollapsed { get; set; }
    public string? ReasoningId { get; init; }
    
    // Simple constructor for backwards compatibility
    public CopilotChatMessage(string content, bool isUser)
    {
        Content = content;
        IsUser = isUser;
        MessageType = CopilotMessageType.Text;
        IsComplete = true;
    }
    
    // Full constructor
    public CopilotChatMessage(string content, bool isUser, CopilotMessageType messageType, string? toolName = null, string? reasoningId = null, string? toolCallId = null)
    {
        Content = content;
        IsUser = isUser;
        MessageType = messageType;
        ToolName = toolName;
        ReasoningId = reasoningId;
        ToolCallId = toolCallId;
        IsComplete = messageType == CopilotMessageType.Text;
    }
}

/// <summary>
/// Type of Copilot chat message
/// </summary>
public enum CopilotMessageType
{
    Text,
    Reasoning,
    ToolCall,
    Error
}

/// <summary>
/// Session usage information from Copilot
/// </summary>
public record CopilotUsageInfo(
    string? Model,
    int? CurrentTokens,
    int? TokenLimit,
    int? InputTokens,
    int? OutputTokens
);

/// <summary>
/// Session error information from Copilot
/// </summary>
public record CopilotSessionError(
    string Message,
    string? Code,
    string? Details
);

/// <summary>
/// Result of checking Copilot CLI availability
/// </summary>
public record CopilotAvailability(
    bool IsInstalled,
    bool IsAuthenticated,
    string? Version,
    string? Login,
    string? ErrorMessage
);

/// <summary>
/// Context for a Copilot assistance request
/// </summary>
public record CopilotContext(
    string Title,
    string Message,
    CopilotContextType Type = CopilotContextType.General,
    string? OperationName = null,
    string? ErrorMessage = null,
    string? Details = null,
    int? ExitCode = null
);

/// <summary>
/// Type of Copilot context request
/// </summary>
public enum CopilotContextType
{
    General,
    EnvironmentFix,
    OperationFailure,
    ProcessFailure
}

/// <summary>
/// Service for managing the global Copilot overlay
/// </summary>
public interface ICopilotContextService
{
    /// <summary>
    /// Whether the Copilot overlay is currently open
    /// </summary>
    bool IsOverlayOpen { get; }
    
    /// <summary>
    /// Open the Copilot overlay without sending a message
    /// </summary>
    void OpenOverlay();
    
    /// <summary>
    /// Close the Copilot overlay
    /// </summary>
    void CloseOverlay();
    
    /// <summary>
    /// Toggle the Copilot overlay open/closed
    /// </summary>
    void ToggleOverlay();
    
    /// <summary>
    /// Open the overlay and send a simple message
    /// </summary>
    void OpenWithMessage(string message);
    
    /// <summary>
    /// Open the overlay and send a context-aware message
    /// </summary>
    void OpenWithContext(CopilotContext context);
    
    /// <summary>
    /// Event fired when overlay open is requested
    /// </summary>
    event Action? OnOpenRequested;
    
    /// <summary>
    /// Event fired when overlay close is requested
    /// </summary>
    event Action? OnCloseRequested;
    
    /// <summary>
    /// Event fired when a message should be sent
    /// </summary>
    event Action<string>? OnMessageRequested;
    
    /// <summary>
    /// Event fired when a context message should be sent
    /// </summary>
    event Action<CopilotContext>? OnContextRequested;
    
    /// <summary>
    /// Notify that the overlay state changed (called by overlay component)
    /// </summary>
    void NotifyOverlayStateChanged(bool isOpen);
}

/// <summary>
/// Represents a Copilot tool with metadata
/// </summary>
public record CopilotTool(Microsoft.Extensions.AI.AIFunction Function, bool IsReadOnly = false)
{
    public string Name => Function.Name;
    public string Description => Function.Description ?? string.Empty;
}

/// <summary>
/// Service that provides Copilot SDK tool definitions for Apple Developer operations
/// </summary>
public interface ICopilotToolsService
{
    /// <summary>
    /// Gets all tool definitions for use in Copilot sessions
    /// </summary>
    IReadOnlyList<Microsoft.Extensions.AI.AIFunction> GetTools();
    
    /// <summary>
    /// Gets a specific tool by name
    /// </summary>
    CopilotTool? GetTool(string name);
    
    /// <summary>
    /// Gets the names of all read-only tools
    /// </summary>
    IReadOnlyList<string> ReadOnlyToolNames { get; }
}

/// <summary>
/// Service for coordinating splash screen visibility between MAUI and Blazor
/// </summary>
public interface ISplashService
{
    /// <summary>
    /// Event fired when Blazor is ready and splash should hide
    /// </summary>
    event Action? OnBlazorReady;
    
    /// <summary>
    /// Called by Blazor when it's fully loaded and ready
    /// </summary>
    void NotifyBlazorReady();
    
    /// <summary>
    /// Whether Blazor has signaled it's ready
    /// </summary>
    bool IsBlazorReady { get; }
}

// ============================================================================
// Cloud Secrets Storage - Abstractions for secure cloud-based secrets
// ============================================================================

/// <summary>
/// Type of cloud secrets provider
/// </summary>
public enum CloudSecretsProviderType
{
    None,
    AzureKeyVault,
    AwsSecretsManager,
    GoogleSecretManager,
    Infisical
}

/// <summary>
/// Configuration for a cloud secrets provider instance
/// </summary>
public record CloudSecretsProviderConfig(
    string Id,
    string Name,
    CloudSecretsProviderType ProviderType,
    Dictionary<string, string> Settings
)
{
    /// <summary>
    /// Creates a new config with a generated ID
    /// </summary>
    public static CloudSecretsProviderConfig Create(
        string name,
        CloudSecretsProviderType providerType,
        Dictionary<string, string> settings) =>
        new(Guid.NewGuid().ToString("N"), name, providerType, settings);
}

/// <summary>
/// Where a secret/certificate private key is stored
/// </summary>
public enum SecretLocation
{
    /// <summary>Not found anywhere - cannot be used for signing</summary>
    None,
    /// <summary>Only in local keychain - can sign but not synced</summary>
    LocalOnly,
    /// <summary>Only in cloud storage - can be installed locally</summary>
    CloudOnly,
    /// <summary>Synced - exists in both local keychain and cloud</summary>
    Both
}

/// <summary>
/// Information about a certificate's secret (private key) storage status
/// </summary>
public record CertificateSecretInfo(
    string CertificateId,
    string SerialNumber,
    SecretLocation Location,
    string? CloudProviderId,
    string? CloudSecretId,
    DateTime? LastSyncedUtc
);

/// <summary>
/// Metadata stored alongside a certificate's private key in the cloud
/// </summary>
public record CertificateSecretMetadata(
    string CertificateId,
    string SerialNumber,
    string CommonName,
    string CertificateType,
    DateTime ExpirationDate,
    string CreatedByMachine,
    DateTime CreatedAt
);

/// <summary>
/// Information about provider configuration requirements
/// </summary>
public record CloudProviderSettingInfo(
    string Key,
    string DisplayName,
    string Description,
    bool IsRequired,
    bool IsSecret,
    string? DefaultValue = null,
    string? Placeholder = null
);

/// <summary>
/// Abstract interface for cloud secrets providers (Azure, AWS, Google, Infisical, etc.)
/// </summary>
public interface ICloudSecretsProvider
{
    /// <summary>
    /// The type of this provider
    /// </summary>
    CloudSecretsProviderType ProviderType { get; }
    
    /// <summary>
    /// Human-readable display name
    /// </summary>
    string DisplayName { get; }
    
    /// <summary>
    /// Tests the connection to the cloud provider
    /// </summary>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Stores a secret value
    /// </summary>
    /// <param name="key">The secret key/name</param>
    /// <param name="value">The secret value as bytes</param>
    /// <param name="metadata">Optional metadata to store with the secret</param>
    Task<bool> StoreSecretAsync(string key, byte[] value, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Retrieves a secret value
    /// </summary>
    /// <param name="key">The secret key/name</param>
    /// <returns>The secret value, or null if not found</returns>
    Task<byte[]?> GetSecretAsync(string key, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes a secret
    /// </summary>
    /// <param name="key">The secret key/name</param>
    Task<bool> DeleteSecretAsync(string key, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if a secret exists
    /// </summary>
    /// <param name="key">The secret key/name</param>
    Task<bool> SecretExistsAsync(string key, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Lists all secrets with an optional prefix filter
    /// </summary>
    /// <param name="prefix">Optional prefix to filter secrets</param>
    Task<IReadOnlyList<string>> ListSecretsAsync(string? prefix = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Factory for creating cloud secrets provider instances
/// </summary>
public interface ICloudSecretsProviderFactory
{
    /// <summary>
    /// Creates a provider instance from configuration
    /// </summary>
    ICloudSecretsProvider CreateProvider(CloudSecretsProviderConfig config);
    
    /// <summary>
    /// Gets the list of supported provider types
    /// </summary>
    IReadOnlyList<CloudSecretsProviderType> SupportedProviders { get; }
    
    /// <summary>
    /// Gets the required and optional settings for a provider type
    /// </summary>
    IReadOnlyList<CloudProviderSettingInfo> GetProviderSettings(CloudSecretsProviderType providerType);
    
    /// <summary>
    /// Gets the display name for a provider type
    /// </summary>
    string GetProviderDisplayName(CloudSecretsProviderType providerType);
}

/// <summary>
/// Service for managing cloud secrets storage providers and operations
/// </summary>
public interface ICloudSecretsService
{
    // Provider management
    
    /// <summary>
    /// Gets all configured cloud secrets providers
    /// </summary>
    Task<IReadOnlyList<CloudSecretsProviderConfig>> GetProvidersAsync();
    
    /// <summary>
    /// Saves (adds or updates) a provider configuration
    /// </summary>
    Task SaveProviderAsync(CloudSecretsProviderConfig provider);
    
    /// <summary>
    /// Deletes a provider configuration
    /// </summary>
    Task DeleteProviderAsync(string providerId);
    
    /// <summary>
    /// Tests a provider's connection
    /// </summary>
    Task<bool> TestProviderConnectionAsync(string providerId);
    
    // Active provider
    
    /// <summary>
    /// Gets the currently active provider (if any)
    /// </summary>
    CloudSecretsProviderConfig? ActiveProvider { get; }
    
    /// <summary>
    /// Sets the active provider by ID (null to clear)
    /// </summary>
    Task SetActiveProviderAsync(string? providerId);
    
    /// <summary>
    /// Event fired when the active provider changes
    /// </summary>
    event Action? OnActiveProviderChanged;
    
    // Secret operations (uses active provider)
    
    /// <summary>
    /// Stores a secret using the active provider
    /// </summary>
    Task<bool> StoreSecretAsync(string key, byte[] value, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Retrieves a secret from the active provider
    /// </summary>
    Task<byte[]?> GetSecretAsync(string key, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes a secret from the active provider
    /// </summary>
    Task<bool> DeleteSecretAsync(string key, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if a secret exists in the active provider
    /// </summary>
    Task<bool> SecretExistsAsync(string key, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Lists secrets from the active provider
    /// </summary>
    Task<IReadOnlyList<string>> ListSecretsAsync(string? prefix = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Service for syncing certificate private keys between local keychain and cloud storage
/// </summary>
public interface ICertificateSyncService
{
    /// <summary>
    /// Gets the storage status for a list of certificates
    /// </summary>
    Task<IReadOnlyList<CertificateSecretInfo>> GetCertificateStatusesAsync(
        IEnumerable<AppleCertificate> certificates,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Uploads a certificate's private key to cloud storage
    /// </summary>
    /// <param name="certificate">The certificate to upload</param>
    /// <param name="p12Data">The P12/PFX data containing the private key</param>
    /// <param name="password">Password protecting the P12</param>
    /// <param name="metadata">Optional metadata about the certificate</param>
    Task<bool> UploadToCloudAsync(
        AppleCertificate certificate,
        byte[] p12Data,
        string password,
        CertificateSecretMetadata? metadata = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Downloads a certificate's private key from cloud storage and installs locally
    /// </summary>
    /// <param name="certificateId">The certificate ID to download</param>
    Task<bool> DownloadAndInstallAsync(string certificateId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the cloud secret key for a certificate
    /// </summary>
    string GetCertificateSecretKey(string serialNumber);
    
    /// <summary>
    /// Gets the password secret key for a certificate
    /// </summary>
    string GetCertificatePasswordKey(string serialNumber);
    
    /// <summary>
    /// Gets the metadata secret key for a certificate
    /// </summary>
    string GetCertificateMetadataKey(string serialNumber);
    
    /// <summary>
    /// Deletes a certificate's private key from cloud storage
    /// </summary>
    /// <param name="serialNumber">The serial number of the certificate to delete</param>
    Task<bool> DeleteFromCloudAsync(string serialNumber, CancellationToken cancellationToken = default);
}
