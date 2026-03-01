namespace MauiSherpa.Pages.Forms;

/// <summary>
/// Static signal used by ModalLayout.razor to notify native MAUI pages
/// that the Blazor content has rendered and is ready to display.
/// Pages subscribe before creating the WebView and fade it in on signal.
/// Includes measured content height so the native sheet can resize.
/// </summary>
public static class ModalWebViewReadySignal
{
    /// <summary>Fired when Blazor content has rendered. Parameter is the content height in CSS pixels.</summary>
    public static event Action<double>? Ready;

    public static void Signal(double contentHeight) => Ready?.Invoke(contentHeight);
}
