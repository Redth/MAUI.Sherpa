using Microsoft.Maui.Controls;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Pages.Forms;

/// <summary>
/// Registers theme-aware color resources for native form pages.
/// Colors match the CSS variables in app.css for light/dark modes.
/// Uses DynamicResource so controls update automatically on theme change.
/// </summary>
public static class FormTheme
{
    // Resource keys
    public const string WindowBg = "AppWindowBg";
    public const string PageBg = "FormPageBg";
    public const string TextPrimary = "FormTextPrimary";
    public const string TextSecondary = "FormTextSecondary";
    public const string TextMuted = "FormTextMuted";
    public const string InputBg = "FormInputBg";
    public const string InputBorder = "FormInputBorder";
    public const string CardBg = "FormCardBg";
    public const string Separator = "FormSeparator";
    public const string AccentPrimary = "FormAccentPrimary";
    public const string AccentPrimaryText = "FormAccentPrimaryText";
    public const string AccentDanger = "FormAccentDanger";
    public const string SecondaryButtonBg = "FormSecondaryButtonBg";
    public const string ButtonOutlineBorder = "FormButtonOutlineBorder";

    // Platform-adaptive values matching DESIGN.md and app.css.
#if WINDOWS
    static readonly Color LightPageBg = Color.FromArgb("#F3F3F3");
    static readonly Color LightCardBg = Color.FromArgb("#FFFFFF");
    static readonly Color LightAccentPrimary = Color.FromArgb("#7C3AED");
    static readonly Color LightAccentPrimaryText = Color.FromArgb("#FFFFFF");
    static readonly Color LightSecondaryButtonBg = Color.FromArgb("#FFFFFF");
    static readonly Color DarkPageBg = Color.FromArgb("#202020");
    static readonly Color DarkCardBg = Color.FromArgb("#2B2B2B");
    static readonly Color DarkAccentPrimary = Color.FromArgb("#B794F4");
    static readonly Color DarkAccentPrimaryText = Color.FromArgb("#14101F");
    static readonly Color DarkSecondaryButtonBg = Color.FromArgb("#323235");
    public static int ControlCornerRadius => 4;
    public static int ActionButtonHeight => 32;
#elif LINUXGTK
    static readonly Color LightWindowBg = Color.FromArgb("#FAFAFA");
    static readonly Color LightPageBg = Color.FromArgb("#FFFFFF");
    static readonly Color LightCardBg = Color.FromArgb("#FFFFFF");
    static readonly Color LightAccentPrimary = Color.FromArgb("#3584E4");
    static readonly Color LightAccentPrimaryText = Color.FromArgb("#FFFFFF");
    static readonly Color LightSecondaryButtonBg = Color.FromArgb("#F0F0F2");
    static readonly Color DarkPageBg = Color.FromArgb("#242424");
    static readonly Color DarkCardBg = Color.FromArgb("#303030");
    static readonly Color DarkAccentPrimary = Color.FromArgb("#78AEED");
    static readonly Color DarkAccentPrimaryText = Color.FromArgb("#0E1A2B");
    static readonly Color DarkSecondaryButtonBg = Color.FromArgb("#3A3A3A");
    public static int ControlCornerRadius => 8;
    public static int ActionButtonHeight => 34;
#else
    static readonly Color LightPageBg = Color.FromArgb("#FFFFFF");
    static readonly Color LightCardBg = Color.FromArgb("#F5F5F7");
    static readonly Color LightAccentPrimary = Color.FromArgb("#007AFF");
    static readonly Color LightAccentPrimaryText = Color.FromArgb("#FFFFFF");
    static readonly Color LightSecondaryButtonBg = Color.FromArgb("#ECECEF");
    static readonly Color DarkPageBg = Color.FromArgb("#1E1E1E");
    static readonly Color DarkCardBg = Color.FromArgb("#28282B");
    static readonly Color DarkAccentPrimary = Color.FromArgb("#0A84FF");
    static readonly Color DarkAccentPrimaryText = Color.FromArgb("#FFFFFF");
    static readonly Color DarkSecondaryButtonBg = Color.FromArgb("#38383C");
    public static int ControlCornerRadius => 10;
    public static int ActionButtonHeight => 30;
#endif

    static readonly Color LightTextPrimary = Color.FromArgb("#18181B");
    static readonly Color LightTextSecondary = Color.FromArgb("#52525B");
    static readonly Color LightTextMuted = Color.FromArgb("#6B6B76");
    static readonly Color LightInputBg = Color.FromArgb("#FFFFFF");
    static readonly Color LightInputBorder = Color.FromArgb("#DCDCE0");
    static readonly Color LightSeparator = Color.FromArgb("#EBEBEF");
    static readonly Color LightAccentDanger = Color.FromArgb("#DC2626");
#if !WINDOWS && !LINUXGTK
    static readonly Color LightButtonOutlineBorder = Colors.Transparent;
    static readonly Color DarkButtonOutlineBorder = Colors.Transparent;
#else
    static readonly Color LightButtonOutlineBorder = Color.FromArgb("#D4D4D8");
    static readonly Color DarkButtonOutlineBorder = Color.FromArgb("#434347");
#endif

    static readonly Color DarkTextPrimary = Color.FromArgb("#F4F4F5");
    static readonly Color DarkTextSecondary = Color.FromArgb("#C4C4CC");
    static readonly Color DarkTextMuted = Color.FromArgb("#9898A3");
    static readonly Color DarkInputBg = Color.FromArgb("#323235");
    static readonly Color DarkInputBorder = Color.FromArgb("#434347");
    static readonly Color DarkSeparator = Color.FromArgb("#323236");
    static readonly Color DarkAccentDanger = Color.FromArgb("#F87171");

    /// <summary>
    /// Register form theme colors into the app's ResourceDictionary
    /// and subscribe to theme changes so DynamicResource bindings update.
    /// Call once during app startup.
    /// </summary>
    public static void Register(Application app, IThemeService themeService)
    {
        ApplyTheme(app.Resources, themeService.IsDarkMode);

        // Primary signal: IThemeService.ThemeChanged covers both manual and system theme changes
        themeService.ThemeChanged += () =>
            ApplyTheme(app.Resources, themeService.IsDarkMode);
    }

    static void ApplyTheme(ResourceDictionary resources, bool dark)
    {
#if LINUXGTK
        resources[WindowBg] = dark ? DarkPageBg : LightWindowBg;
#else
        resources[WindowBg] = dark ? DarkPageBg : LightPageBg;
#endif
        resources[PageBg] = dark ? DarkPageBg : LightPageBg;
        resources[TextPrimary] = dark ? DarkTextPrimary : LightTextPrimary;
        resources[TextSecondary] = dark ? DarkTextSecondary : LightTextSecondary;
        resources[TextMuted] = dark ? DarkTextMuted : LightTextMuted;
        resources[InputBg] = dark ? DarkInputBg : LightInputBg;
        resources[InputBorder] = dark ? DarkInputBorder : LightInputBorder;
        resources[CardBg] = dark ? DarkCardBg : LightCardBg;
        resources[Separator] = dark ? DarkSeparator : LightSeparator;
        resources[AccentPrimary] = dark ? DarkAccentPrimary : LightAccentPrimary;
        resources[AccentPrimaryText] = dark ? DarkAccentPrimaryText : LightAccentPrimaryText;
        resources[AccentDanger] = dark ? DarkAccentDanger : LightAccentDanger;
        resources[SecondaryButtonBg] = dark ? DarkSecondaryButtonBg : LightSecondaryButtonBg;
        resources[ButtonOutlineBorder] = dark ? DarkButtonOutlineBorder : LightButtonOutlineBorder;
    }
}
