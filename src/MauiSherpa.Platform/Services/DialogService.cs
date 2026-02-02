using MauiSherpa.Core.Interfaces;
#if MACCATALYST
using Foundation;
using UIKit;
using UniformTypeIdentifiers;
#endif

namespace MauiSherpa.Platform.Services;

public class DialogService : IDialogService
{
    public Task ShowLoadingAsync(string message)
    {
        return Task.CompletedTask;
    }

    public Task HideLoadingAsync()
    {
        return Task.CompletedTask;
    }

    public Task<string?> ShowInputDialogAsync(string title, string message, string placeholder = "")
    {
        return Task.FromResult<string?>(null);
    }

    public Task<string?> ShowFileDialogAsync(string title, bool isSave = false, string[]? filters = null)
    {
        return Task.FromResult<string?>(null);
    }

    public async Task<string?> PickFolderAsync(string title)
    {
#if MACCATALYST
        var tcs = new TaskCompletionSource<string?>();

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var picker = new UIDocumentPickerViewController(new[] { UTTypes.Folder }, false);
            picker.DirectoryUrl = NSUrl.FromFilename(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            picker.AllowsMultipleSelection = false;

            picker.DidPickDocumentAtUrls += (sender, e) =>
            {
                if (e.Urls?.Length > 0)
                {
                    var url = e.Urls[0];
                    // Start accessing security-scoped resource
                    url.StartAccessingSecurityScopedResource();
                    tcs.TrySetResult(url.Path);
                }
                else
                {
                    tcs.TrySetResult(null);
                }
            };

            picker.WasCancelled += (sender, e) =>
            {
                tcs.TrySetResult(null);
            };

            var viewController = Microsoft.Maui.ApplicationModel.Platform.GetCurrentUIViewController();
            viewController?.PresentViewController(picker, true, null);
        });

        return await tcs.Task;
#else
        return await Task.FromResult<string?>(null);
#endif
    }

    public async Task CopyToClipboardAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        
        await Clipboard.Default.SetTextAsync(text);
    }
}
