namespace MauiSherpa.Services;

/// <summary>
/// Opens profiling artifact viewers in separate windows.
/// Each viewer type gets its own window; reopening the same type
/// activates the existing window instead of creating a new one.
/// </summary>
public class ProfilingViewerService
{
    private readonly Dictionary<string, Window> _windows = new();

    public void OpenSpeedscope(string filePath)
    {
        var encodedPath = Uri.EscapeDataString(filePath);
        OpenViewer("speedscope", $"/profiling/speedscope?file={encodedPath}",
            $"Trace — {Path.GetFileName(filePath)}", 1200, 700);
    }

    public void OpenGcDump(string filePath)
    {
        var encodedPath = Uri.EscapeDataString(filePath);
        OpenViewer("gcdump", $"/profiling/gcdump?file={encodedPath}",
            $"GC Dump — {Path.GetFileName(filePath)}", 900, 600);
    }

    private void OpenViewer(string viewerKey, string route, string title, int width, int height)
    {
        // If a window for this key exists and is still alive, activate it
        if (_windows.TryGetValue(viewerKey, out var existing))
        {
            Application.Current?.CloseWindow(existing);
            _windows.Remove(viewerKey);
        }

        var page = new InspectorPage(route, title);
        var window = new Window(page)
        {
            Title = title,
            Width = width,
            Height = height,
        };
        window.Destroying += (_, _) => _windows.Remove(viewerKey);
        _windows[viewerKey] = window;
        Application.Current?.OpenWindow(window);
    }
}
