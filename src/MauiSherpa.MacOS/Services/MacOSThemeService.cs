using MauiSherpa.Core.Interfaces;
using AppKit;
using Foundation;

namespace MauiSherpa.Services;

/// <summary>
/// macOS-specific theme service that detects native appearance (light/dark)
/// and observes system appearance changes.
/// </summary>
public class MacOSThemeService : IThemeService, IDisposable
{
    private string _currentTheme = "System";
    private double _fontScale = 1.0;
    private NSObject? _appearanceObserver;

    public MacOSThemeService()
    {
        // Observe system appearance changes via distributed notification
        _appearanceObserver = NSDistributedNotificationCenter.DefaultCenter.AddObserver(
            new NSString("AppleInterfaceThemeChangedNotification"),
            notification =>
            {
                if (_currentTheme == "System")
                {
                    NSApplication.SharedApplication.BeginInvokeOnMainThread(() =>
                    {
                        ThemeChanged?.Invoke();
                    });
                }
            });
    }

    public string CurrentTheme => _currentTheme;
    public double FontScale => _fontScale;

    public bool IsDarkMode
    {
        get
        {
            if (_currentTheme == "Dark") return true;
            if (_currentTheme == "Light") return false;

            // Detect macOS system appearance
            var appearance = NSApplication.SharedApplication.EffectiveAppearance;
            var name = appearance.FindBestMatch(new string[] { "NSAppearanceNameAqua", "NSAppearanceNameDarkAqua" });
            return name?.ToString() == "NSAppearanceNameDarkAqua";
        }
    }

    public event Action? ThemeChanged;

    public void SetTheme(string theme)
    {
        if (_currentTheme == theme) return;

        _currentTheme = theme;

        // Set MAUI app theme
        if (Application.Current != null)
        {
            Application.Current.UserAppTheme = theme switch
            {
                "Dark" => AppTheme.Dark,
                "Light" => AppTheme.Light,
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

    public void Dispose()
    {
        if (_appearanceObserver != null)
        {
            NSNotificationCenter.DefaultCenter.RemoveObserver(_appearanceObserver);
            _appearanceObserver = null;
        }
    }
}
