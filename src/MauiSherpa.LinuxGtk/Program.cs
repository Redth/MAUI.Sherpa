using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using MauiDevFlow.Agent.Gtk;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Services;
using Platform.Maui.Linux.Gtk4.Platform;

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

        // Wire up native GTK HeaderBar toolbar driven by IToolbarService
        var toolbarService = Services.GetService<IToolbarService>();
        var copilotContext = Services.GetService<ICopilotContextService>();
        var themeForToolbar = Services.GetService<IThemeService>();
        if (toolbarService != null && copilotContext != null && themeForToolbar != null)
        {
            var mauiWindow = Microsoft.Maui.Controls.Application.Current?.Windows?.FirstOrDefault();
            var gtkWindow = (mauiWindow as Window)?.Handler?.PlatformView as Gtk.Window;
            if (gtkWindow != null)
            {
                _toolbarManager = new LinuxToolbarManager(toolbarService, copilotContext, themeForToolbar);
                _toolbarManager.AttachToWindow(gtkWindow);
            }
        }

        // Eagerly resolve CopilotModalService so its constructor subscribes
        // to context events before the Blazor app loads or user clicks Copilot button.
        Services.GetService<ICopilotModalService>();

#if DEBUG
        Microsoft.Maui.Controls.Application.Current?.StartDevFlowAgent();
#endif
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
        var app = new Program();
        app.Run(args);
    }
}
