using System.Collections.Concurrent;
using System.Globalization;
using MauiSherpa.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace MauiSherpa.Core.Screenshots;

public sealed class ScreenshotMode
{
    public const string EnvironmentVariable = "MAUI_SHERPA_SCREENSHOT_MODE";
    public const string FixtureVersion = "website-v1";
    public const int WindowWidth = 1440;
    public const int WindowHeight = 960;
    public const int SidebarWidth = 250;
    public static ScreenshotMode Current { get; } = Parse(Environment.GetEnvironmentVariable(EnvironmentVariable));
    public static DateTime ReferenceDate { get; } = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    private readonly ConcurrentQueue<string> violations = new();
    private readonly Lazy<string> dataDirectory;

    private ScreenshotMode(bool enabled)
    {
        IsEnabled = enabled;
        dataDirectory = new(() =>
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var parent = OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst()
                ? Path.Combine(home, "Library", "Application Support")
                : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var path = Path.Combine(parent, "MauiSherpa.Screenshots", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        });
    }

    public bool IsEnabled { get; }
    public string DataDirectory => IsEnabled ? dataDirectory.Value
        : throw new InvalidOperationException("Screenshot Mode is not enabled.");
    public IReadOnlyList<string> Violations => violations.ToArray();
    public event Action? ViolationsChanged;

    internal static ScreenshotMode Parse(string? value, bool? supported = null)
    {
        if (string.IsNullOrEmpty(value))
            return new(false);
#if DEBUG
        supported ??= true;
#else
        supported ??= false;
#endif
        if (value != FixtureVersion || supported != true)
            throw new InvalidOperationException(
                $"Screenshot Mode requires a Debug build and {EnvironmentVariable}={FixtureVersion}. Refusing to start with real data.");
        return new(true);
    }

    public void Initialize()
    {
        if (!IsEnabled)
            return;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        if (OperatingSystem.IsLinux())
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(DataDirectory, "data"));
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(DataDirectory, "config"));
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", Path.Combine(DataDirectory, "cache"));
        }
        Console.WriteLine($"SCREENSHOT MODE: {FixtureVersion}; isolated storage: {DataDirectory}");
    }

    internal InvalidOperationException Blocked(string operation)
    {
        var message = $"Screenshot Mode blocked {operation}; no real data or external actions are available.";
        violations.Enqueue(message);
        Console.Error.WriteLine(message);
        ViolationsChanged?.Invoke();
        return new InvalidOperationException(message);
    }

    public static void BlockExternalAction(string operation)
    {
        if (Current.IsEnabled)
            throw Current.Blocked(operation);
    }

    public void RegisterServices(IServiceCollection services)
    {
        if (!IsEnabled)
            return;
        var contracts = typeof(ScreenshotServices).GetInterfaces()
            .Where(type => type.Namespace == typeof(IAndroidSdkService).Namespace)
            .ToHashSet();
        for (var index = services.Count - 1; index >= 0; index--)
        {
            var type = services[index].ServiceType;
            if (contracts.Contains(type) || type.IsClass && type.GetInterfaces().Any(contracts.Contains))
                services.RemoveAt(index);
        }
        services.AddSingleton(this);
        services.AddSingleton<ScreenshotServices>();
        foreach (var contract in contracts)
            services.AddSingleton(contract, provider => provider.GetRequiredService<ScreenshotServices>());
        for (var index = services.Count - 1; index >= 0; index--)
            if (services[index].ServiceType == typeof(IPublishProfileHealthService))
                services.RemoveAt(index);
        services.AddSingleton<IPublishProfileHealthService>(sp => new Services.PublishProfileHealthService(
            sp.GetRequiredService<Shiny.Mediator.IMediator>(),
            sp.GetRequiredService<IAppleIdentityStateService>(),
            sp.GetRequiredService<IAppleIdentityService>(),
            sp.GetRequiredService<IGoogleIdentityService>(),
            sp.GetRequiredService<IManagedSecretsService>(),
            sp.GetRequiredService<ICloudSecretsService>(),
            sp.GetRequiredService<IPublishProfileService>(),
            sp.GetRequiredService<ILoggingService>(),
            () => ReferenceDate));
        var presentationContracts = new HashSet<Type>
        {
            typeof(IAlertService), typeof(ILoggingService), typeof(INavigationService), typeof(IThemeService),
            typeof(IPlatformService), typeof(IOperationModalService), typeof(IMultiOperationModalService),
            typeof(IProcessModalService), typeof(ICopilotContextService), typeof(ISplashService),
            typeof(IToolbarService), typeof(ICopilotModalService), typeof(IDebugFlagService), typeof(IFormModalService)
        };
        var uncovered = services.Select(s => s.ServiceType)
            .Where(type => type.IsInterface && type.Namespace == typeof(IAndroidSdkService).Namespace
                && !contracts.Contains(type) && !presentationContracts.Contains(type))
            .Select(type => type.Name).Distinct().ToArray();
        if (uncovered.Length > 0)
            throw Blocked($"uncovered service registrations: {string.Join(", ", uncovered)}");
    }
}
