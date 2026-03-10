using MauiSherpa.Core.Interfaces;
#if MACCATALYST
using Foundation;
using UIKit;
using UniformTypeIdentifiers;
#elif MACOSAPP
using AppKit;
using Foundation;
#endif
using Microsoft.Extensions.DependencyInjection;

namespace MauiSherpa.Services;

public class DialogService : IDialogService
{
    private readonly IClipboard _clipboard;
    private readonly IFilePicker? _filePicker;
    private IDispatcher Dispatcher => Application.Current!.Dispatcher;

    public DialogService(IClipboard clipboard, IServiceProvider serviceProvider)
    {
        _clipboard = clipboard;
        _filePicker = serviceProvider.GetService<IFilePicker>();
    }

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
        
        await Dispatcher.DispatchAsync(() =>
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
#elif MACOSAPP
        var tcs = new TaskCompletionSource<string?>();

        await Dispatcher.DispatchAsync(() =>
        {
            var alert = new NSAlert
            {
                AlertStyle = NSAlertStyle.Informational,
                MessageText = title,
                InformativeText = message
            };

            var isPassword = title.Contains("Password", StringComparison.OrdinalIgnoreCase);
            NSTextField input;
            if (isPassword)
                input = NSSecureTextField.CreateTextField(string.Empty);
            else
                input = NSTextField.CreateTextField(string.Empty);

            input.PlaceholderString = placeholder;
            input.Frame = new CoreGraphics.CGRect(0, 0, 260, 24);
            alert.AccessoryView = input;

            alert.AddButton("OK");
            alert.AddButton("Cancel");

            alert.Window.InitialFirstResponder = input;

            var response = alert.RunModal();
            if (response == 1000) // NSAlertFirstButtonReturn (OK)
                tcs.TrySetResult(input.StringValue);
            else
                tcs.TrySetResult(null);
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

        await Dispatcher.DispatchAsync(() =>
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
        return await Dispatcher.DispatchAsync(async () =>
        {
            var folderPicker = new Windows.Storage.Pickers.FolderPicker();
            folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            folderPicker.FileTypeFilter.Add("*");

            var hwnd = GetWindowHandle();
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

            var folder = await folderPicker.PickSingleFolderAsync();
            return folder?.Path;
        });
#elif LINUXGTK
        return await Dispatcher.DispatchAsync(async () =>
        {
            var dialog = Gtk.FileDialog.New();
            dialog.SetTitle(title);
            dialog.SetModal(true);
            var window = GetActiveGtkWindow();
            if (window is null) return null;
            try
            {
                var folder = await dialog.SelectFolderAsync(window);
                return folder?.GetPath();
            }
            catch { return null; }
        });
#else
        return await Task.FromResult<string?>(null);
#endif
    }

    public async Task CopyToClipboardAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        
        await _clipboard.SetTextAsync(text);
    }

    public async Task<string?> PickOpenFileAsync(string title, string[]? extensions = null)
    {
#if MACCATALYST
        var tcs = new TaskCompletionSource<string?>();
        
        await Dispatcher.DispatchAsync(() =>
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
#elif LINUXGTK
        if (_filePicker == null) return null;
        try
        {
            // Don't pass FileTypes — MAUI's FilePickerFileType.Value doesn't support Linux platform.
            // The GTK file dialog will show all files; user can still filter visually.
            var result = await Dispatcher.DispatchAsync(async () =>
                await _filePicker.PickAsync(new PickOptions { PickerTitle = title })
            );
            return result?.FullPath;
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[FilePicker] Exception: {ex}");
            return null;
        }
#else
        try
        {
            var fileTypes = new Dictionary<DevicePlatform, IEnumerable<string>>();
            if (extensions != null && extensions.Length > 0)
            {
                var dotExtensions = extensions.Select(e => "." + e.TrimStart('.')).ToArray();
                fileTypes[DevicePlatform.WinUI] = dotExtensions;
                fileTypes[DevicePlatform.macOS] = dotExtensions;
            }

            var picker = _filePicker ?? FilePicker.Default;
            var result = await picker.PickAsync(new PickOptions
            {
                PickerTitle = title,
                FileTypes = extensions != null && extensions.Length > 0
                    ? new FilePickerFileType(fileTypes)
                    : null
            });
            return result?.FullPath;
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException
            || ex.GetType().Name == "NotImplementedInReferenceAssemblyException")
        {
            return null;
        }
#endif
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName, string extension)
    {
#if MACCATALYST
        var tcs = new TaskCompletionSource<string?>();
        
        await Dispatcher.DispatchAsync(async () =>
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
        return await Dispatcher.DispatchAsync(async () =>
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
#elif MACOSAPP
        var tcs = new TaskCompletionSource<string?>();

        await Dispatcher.DispatchAsync(() =>
        {
            var panel = new NSSavePanel
            {
                Title = title,
                NameFieldStringValue = suggestedName,
                CanCreateDirectories = true,
            };

            var cleanExt = extension.TrimStart('.');
            panel.AllowedFileTypes = new[] { cleanExt };

            var result = panel.RunModal();
            if (result == 1 && panel.Url?.Path is string path)
                tcs.TrySetResult(path);
            else
                tcs.TrySetResult(null);
        });

        return await tcs.Task;
#elif LINUXGTK
        return await Dispatcher.DispatchAsync(async () =>
        {
            var dialog = Gtk.FileDialog.New();
            dialog.SetTitle(title);
            dialog.SetModal(true);
            var cleanExt = extension.TrimStart('.');
            dialog.SetInitialName(suggestedName);
            var filterList = Gio.ListStore.New(Gtk.FileFilter.GetGType());
            var filter = Gtk.FileFilter.New();
            filter.AddSuffix(cleanExt);
            filterList.Append(filter);
            dialog.SetFilters(filterList);
            var window = GetActiveGtkWindow();
            if (window is null) return null;
            try
            {
                var file = await dialog.SaveAsync(window);
                return file?.GetPath();
            }
            catch { return null; }
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

#if LINUXGTK
    private static Gtk.Window? GetActiveGtkWindow()
    {
        if (Gtk.Application.GetDefault() is Gtk.Application app && app.GetActiveWindow() is Gtk.Window active)
            return active;
        var toplevels = Gtk.Window.GetToplevels();
        for (uint i = 0; i < toplevels.GetNItems(); i++)
        {
            if (toplevels.GetObject(i) is Gtk.Window window)
                return window;
        }
        return null;
    }
#endif
}
