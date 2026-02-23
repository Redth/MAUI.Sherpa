using Microsoft.Extensions.Logging;
using System.Reflection;
using MauiSherpa.Services;
using MauiSherpa.Core.ViewModels;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Microsoft.Maui.Platform.MacOS.Hosting;
using Microsoft.Maui.Platform.MacOS.Handlers;
using Microsoft.Maui.Essentials.MacOS;
using Shiny.Mediator;
using MauiDevFlow.Agent;
using MauiDevFlow.Blazor;

namespace MauiSherpa;

public static class MacOSMauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // Migrate data from old paths
        MigrateAppData();

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiAppMacOS<MacOSApp>()
            .AddMacOSEssentials()
            .AddMacOSBlazorWebView()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Use native sidebar for FlyoutPage
        builder.ConfigureMauiHandlers(handlers =>
        {
            handlers.AddHandler<FlyoutPage, NativeSidebarFlyoutPageHandler>();
        });

        builder.Services.AddMauiBlazorWebView();

        // Debug logging service
        var debugLogService = new DebugLogService();
        builder.Services.AddSingleton(debugLogService);
        builder.Logging.AddProvider(new DebugOverlayLoggerProvider(debugLogService));
        builder.Logging.SetMinimumLevel(LogLevel.Debug);

        // Splash service
        builder.Services.AddSingleton<ISplashService, SplashService>();

        // Platform services
        builder.Services.AddSingleton<BlazorToastService>();
        builder.Services.AddSingleton<IAlertService, AlertService>();
        builder.Services.AddSingleton<ILoggingService, LoggingService>();
        builder.Services.AddSingleton<IPlatformService, MacOSPlatformService>();
        builder.Services.AddScoped<INavigationService, NavigationService>();
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<IFileSystemService, FileSystemService>();
        builder.Services.AddSingleton<ISecureStorageService, SecureStorageService>();
        builder.Services.AddSingleton<IThemeService, MacOSThemeService>();

        // Toolbar service for native macOS toolbar integration
        builder.Services.AddSingleton<IToolbarService, ToolbarService>();

        // Process execution services
        builder.Services.AddSingleton<IProcessExecutionService, ProcessExecutionService>();
        builder.Services.AddSingleton<ProcessModalService>();
        builder.Services.AddSingleton<IProcessModalService>(sp => sp.GetRequiredService<ProcessModalService>());
        builder.Services.AddSingleton<OperationModalService>();
        builder.Services.AddSingleton<IOperationModalService>(sp => sp.GetRequiredService<OperationModalService>());
        builder.Services.AddSingleton<MultiOperationModalService>();
        builder.Services.AddSingleton<IMultiOperationModalService>(sp => sp.GetRequiredService<MultiOperationModalService>());

        // Core services
        builder.Services.AddSingleton<IAndroidSdkService, AndroidSdkService>();
        builder.Services.AddSingleton<IAndroidSdkSettingsService, AndroidSdkSettingsService>();
        builder.Services.AddSingleton<IOpenJdkSettingsService, OpenJdkSettingsService>();
        builder.Services.AddSingleton<IKeystoreService, KeystoreService>();
        builder.Services.AddSingleton<IKeystoreSyncService, KeystoreSyncService>();
        builder.Services.AddSingleton<ILogcatService, LogcatService>();
        builder.Services.AddSingleton<IAdbDeviceWatcherService, AdbDeviceWatcherService>();
        builder.Services.AddSingleton<IDeviceFileService, DeviceFileService>();
        builder.Services.AddSingleton<IDeviceShellService, DeviceShellService>();
        builder.Services.AddSingleton<IScreenCaptureService, ScreenCaptureService>();
        builder.Services.AddSingleton<IAndroidDeviceToolsService, AndroidDeviceToolsService>();
        builder.Services.AddSingleton<IFirebasePushService, FirebasePushService>();
        builder.Services.AddSingleton<DeviceInspectorService>();
        builder.Services.AddSingleton<IDoctorService, DoctorService>();
        builder.Services.AddSingleton<ICopilotToolsService, CopilotToolsService>();
        builder.Services.AddSingleton<ICopilotService, CopilotService>();
        builder.Services.AddSingleton<ICopilotContextService, CopilotContextService>();

        // Apple services
        builder.Services.AddSingleton<IAppleIdentityService, AppleIdentityService>();
        builder.Services.AddSingleton<IAppleIdentityStateService, AppleIdentityStateService>();
        builder.Services.AddSingleton<IGoogleIdentityService, GoogleIdentityService>();
        builder.Services.AddSingleton<IGoogleIdentityStateService, GoogleIdentityStateService>();
        builder.Services.AddSingleton<IAppleConnectService, AppleConnectService>();
        builder.Services.AddSingleton<IAppleRootCertService, AppleRootCertService>();
        builder.Services.AddSingleton<IApnsPushService, ApnsPushService>();
        builder.Services.AddSingleton<ILocalCertificateService>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggingService>();
            var platform = sp.GetRequiredService<IPlatformService>();
            return new LocalCertificateService(logger, platform);
        });
        builder.Services.AddSingleton<ISimulatorService, MauiSherpa.Core.Services.SimulatorService>();
        builder.Services.AddSingleton<ISimulatorLogService, SimulatorLogService>();
        builder.Services.AddSingleton<IPhysicalDeviceService, MauiSherpa.Core.Services.PhysicalDeviceService>();
        builder.Services.AddSingleton<SimInspectorService>();
        builder.Services.AddSingleton<InspectorCoordinator>();

        // Cloud Secrets Storage services
        builder.Services.AddSingleton<ICloudSecretsProviderFactory, CloudSecretsProviderFactory>();
        builder.Services.AddSingleton<ICloudSecretsService, CloudSecretsService>();
        builder.Services.AddSingleton<IManagedSecretsService, ManagedSecretsService>();
        builder.Services.AddSingleton<ICertificateSyncService, CertificateSyncService>();

        // CI/CD Secrets Publisher services
        builder.Services.AddSingleton<ISecretsPublisherFactory, SecretsPublisherFactory>();
        builder.Services.AddSingleton<ISecretsPublisherService, SecretsPublisherService>();

        // Encrypted Settings services
        builder.Services.AddSingleton<IEncryptedSettingsService, EncryptedSettingsService>();
        builder.Services.AddSingleton<IBackupService, BackupService>();
        builder.Services.AddSingleton<ISettingsMigrationService, SettingsMigrationService>();

        // Update service
        builder.Services.AddSingleton<IUpdateService>(sp =>
        {
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "MauiSherpa");
            var logger = sp.GetRequiredService<ILoggingService>();
            var version = typeof(MacOSMauiProgram).Assembly
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? AppInfo.VersionString;
            return new UpdateService(httpClient, logger, version);
        });

        // ViewModels
        builder.Services.AddSingleton<DashboardViewModel>();
        builder.Services.AddSingleton<AndroidSdkViewModel>();
        builder.Services.AddSingleton<AppleToolsViewModel>();
        builder.Services.AddSingleton<CopilotViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();

        // Shiny Mediator with caching
        builder.AddShinyMediator(cfg =>
        {
            cfg.UseMaui();
            cfg.AddMauiPersistentCache();
            cfg.AddStandardAppSupportMiddleware();
        });

        // Register handlers from Core assembly
        builder.Services.AddSingletonAsImplementedInterfaces<MauiSherpa.Core.Handlers.Android.GetSdkPathHandler>();
        builder.Services.AddSingletonAsImplementedInterfaces<MauiSherpa.Core.Handlers.Android.GetJdkPathHandler>();
        builder.Services.AddSingletonAsImplementedInterfaces<MauiSherpa.Core.Handlers.Android.GetKeystoresHandler>();
        builder.Services.AddSingletonAsImplementedInterfaces<MauiSherpa.Core.Handlers.Android.GetKeystoreSignaturesHandler>();
        builder.Services.AddSingletonAsImplementedInterfaces<MauiSherpa.Core.Handlers.Android.GetInstalledPackagesHandler>();
        builder.Services.AddSingletonAsImplementedInterfaces<MauiSherpa.Core.Handlers.Android.GetAvailablePackagesHandler>();
        builder.Services.AddSingletonAsImplementedInterfaces<MauiSherpa.Core.Handlers.Android.GetEmulatorsHandler>();
        builder.Services.AddSingletonAsImplementedInterfaces<MauiSherpa.Core.Handlers.Android.GetSystemImagesHandler>();
        builder.Services.AddSingletonAsImplementedInterfaces<MauiSherpa.Core.Handlers.Android.GetAndroidDevicesHandler>();
        builder.Services.AddSingletonAsImplementedInterfaces<MauiSherpa.Core.Handlers.Android.GetDeviceDefinitionsHandler>();
        builder.Services.AddSingletonAsImplementedInterfaces<MauiSherpa.Core.Handlers.Android.GetAvdSkinsHandler>();
        builder.Services.AddSingletonAsImplementedInterfaces<MauiSherpa.Core.Handlers.Apple.GetCertificatesHandler>();
        builder.Services.AddSingletonAsImplementedInterfaces<MauiSherpa.Core.Handlers.Apple.GetProfilesHandler>();
        builder.Services.AddSingletonAsImplementedInterfaces<MauiSherpa.Core.Handlers.Apple.GetAppleDevicesHandler>();
        builder.Services.AddSingletonAsImplementedInterfaces<MauiSherpa.Core.Handlers.Apple.GetBundleIdsHandler>();
        builder.Services.AddSingletonAsImplementedInterfaces<MauiSherpa.Core.Handlers.Apple.GetInstalledCertsHandler>();
        builder.Services.AddSingletonAsImplementedInterfaces<MauiSherpa.Core.Handlers.Publisher.ListPublisherRepositoriesHandler>();
        builder.Services.AddSingletonAsImplementedInterfaces<MauiSherpa.Core.Handlers.Apple.TrackBundleIdUsageHandler>();
        builder.Services.AddSingletonAsImplementedInterfaces<MauiSherpa.Core.Handlers.Apple.GetRecentBundleIdsHandler>();
        builder.Services.AddSingletonAsImplementedInterfaces<MauiSherpa.Core.Handlers.Apple.GetSimulatorsHandler>();
        builder.Services.AddSingletonAsImplementedInterfaces<MauiSherpa.Core.Handlers.Apple.GetSimulatorDeviceTypesHandler>();
        builder.Services.AddSingletonAsImplementedInterfaces<MauiSherpa.Core.Handlers.Apple.GetSimulatorRuntimesHandler>();
        builder.Services.AddSingletonAsImplementedInterfaces<MauiSherpa.Core.Handlers.Apple.GetSimulatorAppsHandler>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.AddMauiDevFlowAgent();
        builder.AddMauiBlazorDevFlowTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    static void MigrateAppData()
    {
        try
        {
            var newDir = AppDataPath.GetAppDataDirectory();
            var markerFile = Path.Combine(newDir, ".migration-checked");

            if (File.Exists(markerFile))
                return;

            Directory.CreateDirectory(newDir);

            var oldDirs = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".maui-sherpa"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", ".config", "MauiSherpa")
            };

            foreach (var oldDir in oldDirs)
            {
                try
                {
                    if (!Directory.Exists(oldDir)) continue;
                    foreach (var file in Directory.GetFiles(oldDir, "*", SearchOption.AllDirectories))
                    {
                        var relativePath = Path.GetRelativePath(oldDir, file);
                        if (relativePath == ".migration-checked") continue;
                        var destPath = Path.Combine(newDir, relativePath);
                        var destDir = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(destDir))
                            Directory.CreateDirectory(destDir);
                        if (!File.Exists(destPath) || File.GetLastWriteTimeUtc(file) > File.GetLastWriteTimeUtc(destPath))
                            File.Copy(file, destPath, overwrite: true);
                    }
                }
                catch { }
            }

            File.WriteAllText(markerFile, "migrated");
        }
        catch { }
    }
}
