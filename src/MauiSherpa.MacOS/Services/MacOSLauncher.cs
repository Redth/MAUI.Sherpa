using AppKit;
using Foundation;

namespace MauiSherpa.Services;

/// <summary>
/// macOS implementation of ILauncher using NSWorkspace.
/// </summary>
public class MacOSLauncher : ILauncher
{
    public Task<bool> CanOpenAsync(string uri) =>
        Task.FromResult(true);

    public Task<bool> CanOpenAsync(Uri uri) =>
        Task.FromResult(true);

    public Task OpenAsync(string uri) =>
        OpenAsync(new Uri(uri));

    public Task<bool> OpenAsync(Uri uri)
    {
        var result = NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl(uri.AbsoluteUri));
        return Task.FromResult(result);
    }

    public Task<bool> OpenAsync(OpenFileRequest request)
    {
        if (request.File?.FullPath is string path)
        {
            var result = NSWorkspace.SharedWorkspace.OpenUrl(NSUrl.FromFilename(path));
            return Task.FromResult(result);
        }
        return Task.FromResult(false);
    }

    public Task<bool> TryOpenAsync(Uri uri)
    {
        var result = NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl(uri.AbsoluteUri));
        return Task.FromResult(result);
    }
}
