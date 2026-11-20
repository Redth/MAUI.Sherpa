namespace MauiSherpa;

/// <summary>
/// Registers the app's bundled symbolic icons with the GTK icon theme. Icons named
/// <c>*-symbolic</c> are recolored by GTK from the widget's foreground color, so they
/// follow light/dark switches without per-theme assets.
/// </summary>
internal static class LinuxIconTheme
{
    private static bool _registered;

    public static void EnsureRegistered()
    {
        if (_registered || Gdk.Display.GetDefault() is not { } display)
            return;

        var iconDirectory = Path.Combine(AppContext.BaseDirectory, "Resources", "icons");
        if (!Directory.Exists(iconDirectory))
            return;

        Gtk.IconTheme.GetForDisplay(display).AddSearchPath(iconDirectory);
        _registered = true;
    }
}
