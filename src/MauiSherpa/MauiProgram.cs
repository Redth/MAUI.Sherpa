using Microsoft.Extensions.Logging;
using MauiSherpa.Services;
using MauiSherpa.Core.ViewModels;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Shiny.Mediator;

namespace MauiSherpa;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
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

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
