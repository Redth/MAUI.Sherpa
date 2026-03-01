using Microsoft.Maui.Controls;

namespace MauiSherpa.Pages.Forms;

/// <summary>
/// Registers theme-aware color resources for native form pages.
/// Colors match the CSS variables in app.css for light/dark modes.
/// Uses DynamicResource so controls update automatically on theme change.
/// </summary>
public static class FormTheme
{
    // Resource keys
    public const string PageBg = "FormPageBg";
    public const string TextPrimary = "FormTextPrimary";
    public const string TextSecondary = "FormTextSecondary";
    public const string TextMuted = "FormTextMuted";
    public const string InputBg = "FormInputBg";
    public const string InputBorder = "FormInputBorder";
    public const string CardBg = "FormCardBg";
    public const string Separator = "FormSeparator";
    public const string AccentPrimary = "FormAccentPrimary";
    public const string AccentDanger = "FormAccentDanger";
    public const string ButtonOutlineBorder = "FormButtonOutlineBorder";

    // Light theme values (from app.css :root)
    static readonly Color LightPageBg = Color.FromArgb("#f7fafc");
    static readonly Color LightTextPrimary = Color.FromArgb("#2d3748");
    static readonly Color LightTextSecondary = Color.FromArgb("#4a5568");
    static readonly Color LightTextMuted = Color.FromArgb("#718096");
    static readonly Color LightInputBg = Color.FromArgb("#ffffff");
    static readonly Color LightInputBorder = Color.FromArgb("#e2e8f0");
    static readonly Color LightCardBg = Color.FromArgb("#ffffff");
    static readonly Color LightSeparator = Color.FromArgb("#e2e8f0");
    static readonly Color LightAccentPrimary = Color.FromArgb("#9f7aea");
    static readonly Color LightAccentDanger = Color.FromArgb("#e53e3e");
    static readonly Color LightButtonOutlineBorder = Color.FromArgb("#e2e8f0");

    // Dark theme values (from app.css .theme-dark)
    static readonly Color DarkPageBg = Color.FromArgb("#1a202c");
    static readonly Color DarkTextPrimary = Color.FromArgb("#e2e8f0");
    static readonly Color DarkTextSecondary = Color.FromArgb("#cbd5e0");
    static readonly Color DarkTextMuted = Color.FromArgb("#a0aec0");
    static readonly Color DarkInputBg = Color.FromArgb("#374151");
    static readonly Color DarkInputBorder = Color.FromArgb("#4a5568");
    static readonly Color DarkCardBg = Color.FromArgb("#2d3748");
    static readonly Color DarkSeparator = Color.FromArgb("#4a5568");
    static readonly Color DarkAccentPrimary = Color.FromArgb("#b794f4");
    static readonly Color DarkAccentDanger = Color.FromArgb("#fc8181");
    static readonly Color DarkButtonOutlineBorder = Color.FromArgb("#4a5568");

    /// <summary>
    /// Register form theme colors into the app's ResourceDictionary
    /// and subscribe to theme changes so DynamicResource bindings update.
    /// Call once during app startup.
    /// </summary>
    public static void Register(Application app)
    {
        ApplyTheme(app.Resources, IsDark(app));

        app.RequestedThemeChanged += (_, e) =>
            ApplyTheme(app.Resources, IsDark(app));

        // Also update when UserAppTheme is set programmatically (manual theme switch)
        app.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Application.UserAppTheme))
                ApplyTheme(app.Resources, IsDark(app));
        };
    }

    static bool IsDark(Application app)
    {
        if (app.UserAppTheme != AppTheme.Unspecified)
            return app.UserAppTheme == AppTheme.Dark;
        return app.PlatformAppTheme == AppTheme.Dark;
    }

    static void ApplyTheme(ResourceDictionary resources, bool dark)
    {
        resources[PageBg] = dark ? DarkPageBg : LightPageBg;
        resources[TextPrimary] = dark ? DarkTextPrimary : LightTextPrimary;
        resources[TextSecondary] = dark ? DarkTextSecondary : LightTextSecondary;
        resources[TextMuted] = dark ? DarkTextMuted : LightTextMuted;
        resources[InputBg] = dark ? DarkInputBg : LightInputBg;
        resources[InputBorder] = dark ? DarkInputBorder : LightInputBorder;
        resources[CardBg] = dark ? DarkCardBg : LightCardBg;
        resources[Separator] = dark ? DarkSeparator : LightSeparator;
        resources[AccentPrimary] = dark ? DarkAccentPrimary : LightAccentPrimary;
        resources[AccentDanger] = dark ? DarkAccentDanger : LightAccentDanger;
        resources[ButtonOutlineBorder] = dark ? DarkButtonOutlineBorder : LightButtonOutlineBorder;
    }
}
