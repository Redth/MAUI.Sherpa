using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Services;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#if DEBUG
using Microsoft.Maui.DevFlow.Agent.Gtk;
using Microsoft.Maui.DevFlow.Blazor.Gtk;
#endif

namespace MauiSherpa;

public class Program : GtkMauiApplication
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    protected override void OnStarted()
    {
        base.OnStarted();

        // Wire up system theme monitoring so Blazor UI reacts to OS dark/light changes
        if (Services.GetService<IThemeService>() is ThemeService themeService)
        {
            themeService.StartMonitoringSystemTheme();
            StartGSettingsThemeMonitor(themeService);
        }

        // Eagerly resolve CopilotModalService so its constructor subscribes
        // to context events before the Blazor app loads or user clicks Copilot button.
        Services.GetService<ICopilotModalService>();

#if DEBUG
        if (Microsoft.Maui.Controls.Application.Current is { } application)
        {
            application.StartDevFlowAgent();
            Services.GetService<GtkBlazorWebViewDebugService>()?.WireBlazorCdpToAgent();
        }
#endif
    }

    public static void AttachLinuxToolbar(Window window)
    {
        if (_toolbarManager != null || window.Handler?.PlatformView is not Gtk.Window gtkWindow)
            return;

        var services = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services;
        if (services == null)
            return;

        var toolbarService = services.GetService<IToolbarService>();
        var copilotContext = services.GetService<ICopilotContextService>();
        var appleIdentityService = services.GetService<IAppleIdentityService>();
        var appleIdentityState = services.GetService<IAppleIdentityStateService>();
        var googleIdentityService = services.GetService<IGoogleIdentityService>();
        var googleIdentityState = services.GetService<IGoogleIdentityStateService>();
        var formModalService = services.GetService<IFormModalService>();
        var bridgeHolder = services.GetService<MauiSherpa.Pages.Forms.HybridFormBridgeHolder>();
        if (toolbarService == null || copilotContext == null
            || appleIdentityService == null || appleIdentityState == null
            || googleIdentityService == null || googleIdentityState == null
            || formModalService == null || bridgeHolder == null)
            return;

        _toolbarManager = new LinuxToolbarManager(
            toolbarService, copilotContext,
            appleIdentityService, appleIdentityState,
            googleIdentityService, googleIdentityState,
            formModalService, bridgeHolder);
        _toolbarManager.AttachToWindow(gtkWindow);
    }

    /// <summary>
    /// Monitor GNOME GSettings for theme changes.
    /// GtkThemeManager.StartMonitoring() watches Gtk.Settings.OnNotify which
    /// may not fire on Wayland when gsettings/portal changes the theme.
    /// This watches dconf directly via Gio.Settings for reliable notifications.
    /// </summary>
    private static void StartGSettingsThemeMonitor(ThemeService themeService)
    {
        try
        {
            var schema = Gio.SettingsSchemaSource.GetDefault()?.Lookup("org.gnome.desktop.interface", true);
            if (schema == null) return;

            var settings = Gio.Settings.New("org.gnome.desktop.interface");

            // Set initial system dark mode from GSettings
            UpdateThemeFromGSettings(settings, themeService);

            settings.OnChanged += (s, args) =>
            {
                if (args.Key is "gtk-theme" or "color-scheme")
                    UpdateThemeFromGSettings(settings, themeService);
            };

            // Keep reference alive
            _gioSettings = settings;
        }
        catch
        {
            // Not a GNOME desktop — skip
        }
    }

    private static void UpdateThemeFromGSettings(Gio.Settings settings, ThemeService themeService)
    {
        var colorScheme = settings.GetString("color-scheme");
        var gtkTheme = settings.GetString("gtk-theme");

        var isDark = colorScheme == "prefer-dark"
            || (gtkTheme?.EndsWith("-dark", StringComparison.OrdinalIgnoreCase) ?? false);

        themeService.UpdateSystemDarkMode(isDark);
    }

    private static Gio.Settings? _gioSettings;
    private static LinuxToolbarManager? _toolbarManager;

    public static void Main(string[] args)
    {
        MauiSherpa.Core.Screenshots.ScreenshotMode.Current.Initialize();
        var app = new Program();
        app.Run(args);
    }
}
