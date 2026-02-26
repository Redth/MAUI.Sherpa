using MauiSherpa.Core.Interfaces;
#if MACCATALYST
using UIKit;
#endif

namespace MauiSherpa.Services;

public class ThemeService : IThemeService
{
    private string _currentTheme = "System";
    private double _fontScale = 1.0;
    private bool? _systemIsDark;
    
    public string CurrentTheme => _currentTheme;
    public double FontScale => _fontScale;
    
    public bool IsDarkMode
    {
        get
        {
            if (_currentTheme == "Dark") return true;
            if (_currentTheme == "Light") return false;
            
            // System theme detection
#if MACCATALYST
            var style = UIScreen.MainScreen.TraitCollection.UserInterfaceStyle;
            return style == UIUserInterfaceStyle.Dark;
#elif WINDOWS
            // Windows theme detection
            try
            {
                var uiSettings = new Windows.UI.ViewManagement.UISettings();
                var color = uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
                return color.R < 128; // Dark if background is dark
            }
            catch
            {
                return false;
            }
#else
            // Linux GTK and other platforms:
            // Prefer the value from our GSettings monitor (kept fresh on changes),
            // falling back to MAUI's PlatformAppTheme (backed by GtkThemeManager).
            if (_systemIsDark.HasValue)
                return _systemIsDark.Value;
            return Application.Current?.PlatformAppTheme == AppTheme.Dark;
#endif
        }
    }
    
    public event Action? ThemeChanged;
    
    public void SetTheme(string theme)
    {
        if (_currentTheme == theme) return;
        
        _currentTheme = theme;

        // Sync with MAUI's AppTheme so native platform (GTK, etc.) follows
        if (Application.Current != null)
        {
            Application.Current.UserAppTheme = theme switch
            {
                "Light" => AppTheme.Light,
                "Dark" => AppTheme.Dark,
                _ => AppTheme.Unspecified
            };
        }

        ThemeChanged?.Invoke();
    }

    public void SetFontScale(double scale)
    {
        scale = Math.Clamp(scale, 0.8, 1.5);
        if (Math.Abs(_fontScale - scale) < 0.001) return;
        
        _fontScale = scale;
        ThemeChanged?.Invoke();
    }

    /// <summary>
    /// Subscribe to MAUI's RequestedThemeChanged so the Blazor UI
    /// updates when the OS dark/light mode switches while in "System" mode.
    /// Call once after Application.Current is available.
    /// </summary>
    public void StartMonitoringSystemTheme()
    {
        if (Application.Current == null) return;
        Application.Current.RequestedThemeChanged += (_, _) =>
        {
            if (_currentTheme == "System")
                ThemeChanged?.Invoke();
        };
    }

    /// <summary>
    /// Called by platform code (e.g., GSettings monitor on Linux) when
    /// the OS dark/light mode changes. Updates the cached system theme
    /// and fires ThemeChanged if the app is in "System" mode.
    /// </summary>
    public void UpdateSystemDarkMode(bool isDark)
    {
        if (_systemIsDark == isDark) return;
        _systemIsDark = isDark;

        if (_currentTheme == "System")
            ThemeChanged?.Invoke();
    }
}
