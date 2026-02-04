using MauiSherpa.Core.Interfaces;
#if MACCATALYST
using Foundation;
using UIKit;
using UniformTypeIdentifiers;
#endif

namespace MauiSherpa.Services;

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

    public async Task<string?> ShowInputDialogAsync(string title, string message, string placeholder = "")
    {
#if MACCATALYST
        var tcs = new TaskCompletionSource<string?>();
        
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var alertController = UIAlertController.Create(title, message, UIAlertControllerStyle.Alert);
            
            alertController.AddTextField(textField =>
            {
                textField.Placeholder = placeholder;
                textField.SecureTextEntry = title.Contains("Password", StringComparison.OrdinalIgnoreCase);
            });
            
            alertController.AddAction(UIAlertAction.Create("Cancel", UIAlertActionStyle.Cancel, _ =>
            {
                tcs.TrySetResult(null);
            }));
            
            alertController.AddAction(UIAlertAction.Create("OK", UIAlertActionStyle.Default, _ =>
            {
                var text = alertController.TextFields?.FirstOrDefault()?.Text;
                tcs.TrySetResult(text);
            }));
            
            var viewController = Microsoft.Maui.ApplicationModel.Platform.GetCurrentUIViewController();
            viewController?.PresentViewController(alertController, true, null);
        });
        
        return await tcs.Task;
#else
        // Windows implementation using ContentDialog would go here
        return await Task.FromResult<string?>(null);
#endif
    }

    public async Task<string?> ShowFileDialogAsync(string title, bool isSave = false, string[]? filters = null, string? defaultFileName = null)
    {
#if MACCATALYST
        if (isSave)
        {
            // For save, we use a folder picker and then append the filename
            var folder = await PickFolderAsync(title);
            if (folder != null && !string.IsNullOrEmpty(defaultFileName))
            {
                return Path.Combine(folder, defaultFileName);
            }
            return folder;
        }
        else
        {
            // TODO: Implement file open picker
            return null;
        }
#else
        return await Task.FromResult<string?>(null);
#endif
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
