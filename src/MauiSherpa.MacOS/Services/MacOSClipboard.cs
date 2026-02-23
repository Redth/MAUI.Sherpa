using AppKit;
using Foundation;

namespace MauiSherpa.Services;

/// <summary>
/// macOS implementation of IClipboard using NSPasteboard.
/// </summary>
public class MacOSClipboard : IClipboard
{
    public bool HasText =>
        NSPasteboard.GeneralPasteboard.GetAvailableTypeFromArray(new string[] { NSPasteboard.NSPasteboardTypeString }) != null;

    public event EventHandler<EventArgs>? ClipboardContentChanged;

    public Task<string?> GetTextAsync()
    {
        var text = NSPasteboard.GeneralPasteboard.GetStringForType(NSPasteboard.NSPasteboardTypeString);
        return Task.FromResult(text);
    }

    public Task SetTextAsync(string? text)
    {
        NSPasteboard.GeneralPasteboard.ClearContents();
        if (text != null)
            NSPasteboard.GeneralPasteboard.SetStringForType(text, NSPasteboard.NSPasteboardTypeString);
        ClipboardContentChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
}
