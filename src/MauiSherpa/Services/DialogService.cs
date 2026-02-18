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
        if (isSave)
        {
            var ext = filters?.FirstOrDefault()?.TrimStart('*', '.') ?? "";
            var fileName = defaultFileName ?? $"file.{ext}";
            return await PickSaveFileAsync(title, fileName, ext);
        }
        else
        {
            var extensions = filters?
                .Select(f => f.TrimStart('*'))
                .ToArray();
            return await PickOpenFileAsync(title, extensions);
        }
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
#elif WINDOWS
        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var folderPicker = new Windows.Storage.Pickers.FolderPicker();
            folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            folderPicker.FileTypeFilter.Add("*");

            var hwnd = GetWindowHandle();
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

            var folder = await folderPicker.PickSingleFolderAsync();
            return folder?.Path;
        });
#else
        return await Task.FromResult<string?>(null);
#endif
    }

    public async Task CopyToClipboardAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        
        await Clipboard.Default.SetTextAsync(text);
    }

    public async Task<string?> PickOpenFileAsync(string title, string[]? extensions = null)
    {
#if MACCATALYST
        var tcs = new TaskCompletionSource<string?>();
        
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var types = new List<UTType>();
            if (extensions != null)
            {
                foreach (var ext in extensions)
                {
                    var cleanExt = ext.TrimStart('.');
                    var utType = UTType.CreateFromExtension(cleanExt);
                    if (utType != null)
                        types.Add(utType);
                }
            }
            if (types.Count == 0)
                types.Add(UTTypes.Data);

            var picker = new UIDocumentPickerViewController(types.ToArray(), false);
            picker.AllowsMultipleSelection = false;

            picker.DidPickDocumentAtUrls += (sender, e) =>
            {
                if (e.Urls?.Length > 0)
                {
                    var url = e.Urls[0];
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
        var fileTypes = new Dictionary<DevicePlatform, IEnumerable<string>>();
        if (extensions != null && extensions.Length > 0)
        {
            fileTypes[DevicePlatform.WinUI] = extensions.Select(e => "." + e.TrimStart('.'));
        }

        var options = new PickOptions
        {
            PickerTitle = title,
            FileTypes = extensions != null && extensions.Length > 0
                ? new FilePickerFileType(fileTypes)
                : null
        };

        var result = await FilePicker.PickAsync(options);
        return result?.FullPath;
#endif
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName, string extension)
    {
#if MACCATALYST
        var tcs = new TaskCompletionSource<string?>();
        
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            // Create temp file with suggested name
            var tempDir = Path.GetTempPath();
            var tempPath = Path.Combine(tempDir, suggestedName);
            if (!File.Exists(tempPath))
                await File.WriteAllBytesAsync(tempPath, Array.Empty<byte>());

            var tempUrl = NSUrl.FromFilename(tempPath);
            
            #pragma warning disable CA1422 // Obsolete API - no good alternative yet
            var picker = new UIDocumentPickerViewController(new[] { tempUrl }, UIDocumentPickerMode.MoveToService);
            #pragma warning restore CA1422
            
            picker.DidPickDocumentAtUrls += (sender, e) =>
            {
                var url = e.Urls?.FirstOrDefault();
                if (url != null)
                {
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
                // Clean up temp file
                try { File.Delete(tempPath); } catch { }
                tcs.TrySetResult(null);
            };

            var viewController = Microsoft.Maui.ApplicationModel.Platform.GetCurrentUIViewController();
            viewController?.PresentViewController(picker, true, null);
        });

        return await tcs.Task;
#elif WINDOWS
        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var savePicker = new Windows.Storage.Pickers.FileSavePicker();
            savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            savePicker.SuggestedFileName = suggestedName;
            
            var cleanExt = "." + extension.TrimStart('.');
            savePicker.FileTypeChoices.Add(title, new List<string> { cleanExt });

            var hwnd = GetWindowHandle();
            WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);

            var file = await savePicker.PickSaveFileAsync();
            return file?.Path;
        });
#else
        return await Task.FromResult<string?>(null);
#endif
    }

#if WINDOWS
    private static nint GetWindowHandle()
    {
        var window = Application.Current!.Windows[0].Handler!.PlatformView as Microsoft.UI.Xaml.Window;
        return WinRT.Interop.WindowNative.GetWindowHandle(window);
    }
#endif
}
