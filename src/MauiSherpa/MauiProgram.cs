using Microsoft.Extensions.Logging;
using MauiSherpa.Services;
using MauiSherpa.Core.ViewModels;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using MauiDevFlow.Agent;
using MauiDevFlow.Blazor;
using Shiny.Mediator;

namespace MauiSherpa;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // Migrate data from old ~/.maui-sherpa/ to ~/Library/Application Support/MauiSherpa/
        MigrateAppData();

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddMauiBlazorWebView();

        // Debug logging service (must be registered before logger provider)
        var debugLogService = new DebugLogService();
        builder.Services.AddSingleton(debugLogService);
        
        // Add custom logger provider for debug overlay
        builder.Logging.AddProvider(new DebugOverlayLoggerProvider(debugLogService));
        builder.Logging.SetMinimumLevel(LogLevel.Debug);

        // Splash service (must be registered early as singleton for sharing)
        builder.Services.AddSingleton<ISplashService, SplashService>();
        
        // Platform services
        builder.Services.AddSingleton<BlazorToastService>();
        builder.Services.AddSingleton<IAlertService, AlertService>();
        builder.Services.AddSingleton<ILoggingService, LoggingService>();
        builder.Services.AddSingleton<IPlatformService, PlatformService>();
        builder.Services.AddScoped<INavigationService, NavigationService>();
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<IFileSystemService, FileSystemService>();
        builder.Services.AddSingleton<ISecureStorageService, SecureStorageService>();
        builder.Services.AddSingleton<IThemeService, ThemeService>();

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
        builder.Services.AddSingleton<ILogcatService, LogcatService>();
        builder.Services.AddSingleton<IAdbDeviceWatcherService, AdbDeviceWatcherService>();
        builder.Services.AddSingleton<IDeviceFileService, DeviceFileService>();
        builder.Services.AddSingleton<IDeviceShellService, DeviceShellService>();
        builder.Services.AddSingleton<IScreenCaptureService, ScreenCaptureService>();
        builder.Services.AddSingleton<DeviceInspectorService>();
        builder.Services.AddSingleton<IDoctorService, DoctorService>();
        builder.Services.AddSingleton<ICopilotToolsService, CopilotToolsService>();
        builder.Services.AddSingleton<ICopilotService, CopilotService>();
        builder.Services.AddSingleton<ICopilotContextService, CopilotContextService>();
        
        // Apple services
        builder.Services.AddSingleton<IAppleIdentityService, AppleIdentityService>();
        builder.Services.AddSingleton<IAppleIdentityStateService, AppleIdentityStateService>();
        builder.Services.AddSingleton<IAppleConnectService, AppleConnectService>();
        builder.Services.AddSingleton<IAppleRootCertService, AppleRootCertService>();
        builder.Services.AddSingleton<ILocalCertificateService, LocalCertificateService>();
        
        // Cloud Secrets Storage services
        builder.Services.AddSingleton<ICloudSecretsProviderFactory, CloudSecretsProviderFactory>();
        builder.Services.AddSingleton<ICloudSecretsService, CloudSecretsService>();
        builder.Services.AddSingleton<ICertificateSyncService, CertificateSyncService>();

        // CI/CD Secrets Publisher services
        builder.Services.AddSingleton<ISecretsPublisherFactory, SecretsPublisherFactory>();
        builder.Services.AddSingleton<ISecretsPublisherService, SecretsPublisherService>();

        // Encrypted Settings services
        builder.Services.AddSingleton<IEncryptedSettingsService, EncryptedSettingsService>();
        builder.Services.AddSingleton<IBackupService, BackupService>();
        builder.Services.AddSingleton<ISettingsMigrationService, SettingsMigrationService>();

        // ViewModels
        builder.Services.AddSingleton<DashboardViewModel>();
        builder.Services.AddSingleton<AndroidSdkViewModel>();
        builder.Services.AddSingleton<AppleToolsViewModel>();
        builder.Services.AddSingleton<CopilotViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();

        // Shiny Mediator with caching and offline support
        builder.AddShinyMediator(cfg =>
        {
            cfg.UseMaui();
            cfg.AddMauiPersistentCache(); // Use persistent cache (includes memory caching)
            cfg.AddStandardAppSupportMiddleware();
        });
        
        // Register handlers from Core assembly
        builder.Services.AddSingletonAsImplementedInterfaces<MauiSherpa.Core.Handlers.Android.GetSdkPathHandler>();
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

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.AddMauiDevFlowAgent();
        builder.AddMauiBlazorDevFlowTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    /// <summary>
    /// One-time migration from ~/.maui-sherpa/ to ~/Library/Application Support/MauiSherpa/
    /// Avoids TCC permission dialogs caused by accessing dotfiles in the home directory root.
    /// </summary>
    static void MigrateAppData()
    {
        try
        {
            var oldDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".maui-sherpa");
            var newDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MauiSherpa");

            if (!Directory.Exists(oldDir))
                return;

            // Already migrated
            if (Directory.Exists(newDir) && Directory.GetFiles(newDir, "*", SearchOption.AllDirectories).Length > 0)
                return;

            Directory.CreateDirectory(newDir);

            // Copy all files preserving directory structure
            foreach (var file in Directory.GetFiles(oldDir, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(oldDir, file);
                var destPath = Path.Combine(newDir, relativePath);
                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);
                File.Copy(file, destPath, overwrite: false);
            }
        }
        catch
        {
            // Migration is best-effort — don't block app startup
        }
    }
}
