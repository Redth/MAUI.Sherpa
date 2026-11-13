using ResourceDictionary = Microsoft.UI.Xaml.ResourceDictionary;
using Color = global::Windows.UI.Color;

namespace MauiSherpa.Platforms.Windows;

internal static class WindowsAccentTheme
{
    internal static readonly Color Light = Color.FromArgb(255, 0x8B, 0x5C, 0xF6);
    internal static readonly Color Dark = Color.FromArgb(255, 0xB7, 0x94, 0xF4);

    internal static void Apply(ResourceDictionary resources)
    {
        // WinUI controls use different members of the accent ramp for light and dark themes.
        resources["SystemAccentColor"] = Light;
        resources["SystemAccentColorDark1"] = Light;
        resources["SystemAccentColorDark2"] = Color.FromArgb(255, 0x7C, 0x3A, 0xED);
        resources["SystemAccentColorDark3"] = Color.FromArgb(255, 0x6D, 0x28, 0xD9);
        resources["SystemAccentColorLight1"] = Color.FromArgb(255, 0xA7, 0x8B, 0xFA);
        resources["SystemAccentColorLight2"] = Dark;
        resources["SystemAccentColorLight3"] = Color.FromArgb(255, 0xC4, 0xB5, 0xFD);
    }
}
