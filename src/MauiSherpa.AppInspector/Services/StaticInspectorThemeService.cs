using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.AppInspector.Services;

public sealed class StaticInspectorThemeService : IThemeService
{
    public string CurrentTheme { get; private set; } = "Dark";
    public bool IsDarkMode => CurrentTheme.Equals("Dark", StringComparison.OrdinalIgnoreCase);
    public double FontScale { get; private set; } = 1.0;
    public event Action? ThemeChanged;

    public void SetTheme(string theme)
    {
        CurrentTheme = string.IsNullOrWhiteSpace(theme) ? "Dark" : theme;
        ThemeChanged?.Invoke();
    }

    public void SetFontScale(double scale)
    {
        FontScale = Math.Clamp(scale, 0.8, 1.4);
        ThemeChanged?.Invoke();
    }
}
