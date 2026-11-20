namespace MauiSherpa.Services;

/// <summary>
/// Opens profiling artifact viewers in separate windows.
/// Each viewer type gets its own window; reopening the same type
/// closes the existing window and opens a fresh one.
/// </summary>
public class ProfilingViewerService
{
    private readonly Dictionary<string, Window> _windows = new();

    public Task OpenSpeedscopeAsync(string filePath)
    {
        var encodedPath = Uri.EscapeDataString(filePath);
        return OpenViewerAsync("speedscope", $"/profiling/speedscope?file={encodedPath}",
            $"Trace — {Path.GetFileName(filePath)}", 1200, 700);
    }

    public Task OpenGcDumpAsync(string filePath)
    {
        var encodedPath = Uri.EscapeDataString(filePath);
        return OpenViewerAsync("gcdump", $"/profiling/gcdump?file={encodedPath}",
            $"GC Dump — {Path.GetFileName(filePath)}", 900, 600);
    }

    private Task OpenViewerAsync(string viewerKey, string route, string title, int width, int height) =>
        NativeUiDispatcher.RunAsync(() =>
        {
            OpenViewer(viewerKey, route, title, width, height);
            return Task.CompletedTask;
        });

    private void OpenViewer(string viewerKey, string route, string title, int width, int height)
    {
        MauiSherpa.Core.Screenshots.ScreenshotMode.BlockExternalAction("opening a profiling artifact");
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
