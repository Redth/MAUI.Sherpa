using AppKit;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Services;

/// <summary>
/// Native AppKit alerts for the macOS head.
/// Page.DisplayAlert resolves its sheet parent through the MAUI Window handler, which can
/// hand back a window that is not the one on screen; the sheet is then attached somewhere
/// invisible and the awaiting call never completes. Presenting NSAlert directly on the
/// visible main window avoids that.
/// </summary>
public class MacOSAlertService : IAlertService
{
    private const nint FirstButtonReturn = 1000;

    private readonly BlazorToastService _toastService;
    private readonly ILoggingService _logger;

    public MacOSAlertService(BlazorToastService toastService, ILoggingService logger)
    {
        _toastService = toastService;
        _logger = logger;
    }

    public async Task ShowAlertAsync(string title, string message, string? cancel = null)
    {
        await PresentAsync(title, message, new[] { cancel ?? "OK" }, destructiveIndex: null);
    }

    public async Task<bool> ShowConfirmAsync(string title, string message, string? confirm = null, string? cancel = null)
    {
        var response = await PresentAsync(title, message, new[] { confirm ?? "Yes", cancel ?? "No" }, destructiveIndex: null);
        return response == 0;
    }

    public async Task<string?> ShowActionSheetAsync(string title, string? cancel, string? destruction, params string[] buttons)
    {
        var labels = buttons.Where(b => b is not null).ToList();
        int? destructiveIndex = null;
        if (destruction is not null)
        {
            destructiveIndex = labels.Count;
            labels.Add(destruction);
        }
        if (cancel is not null)
            labels.Add(cancel);

        var response = await PresentAsync(title, null, labels, destructiveIndex);
        return response >= 0 && response < labels.Count ? labels[response] : cancel;
    }

    public Task ShowToastAsync(string message)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher?.IsDispatchRequired ?? false)
            dispatcher.Dispatch(() => _toastService.ShowSuccess(message));
        else
            _toastService.ShowSuccess(message);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Shows the alert and returns the zero-based index of the button pressed.
    /// </summary>
    private Task<int> PresentAsync(string title, string? message, IReadOnlyList<string> buttons, int? destructiveIndex)
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        NSApplication.SharedApplication.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var alert = new NSAlert
                {
                    MessageText = title,
                    InformativeText = message ?? string.Empty
                };
                for (var i = 0; i < buttons.Count; i++)
                {
                    var button = alert.AddButton(buttons[i]);
                    if (i == destructiveIndex)
                        button.HasDestructiveAction = true;
                }

                var window = ResolveVisibleWindow();
                if (window is not null && window.AttachedSheet is null)
                {
                    alert.BeginSheetForResponse(window, response =>
                        tcs.TrySetResult((int)(response - FirstButtonReturn)));
                }
                else
                {
                    // No visible window, or it already has a sheet that would queue this one
                    // out of sight: run app-modal so the user always sees the prompt.
                    if (window?.AttachedSheet is not null)
                        _logger.LogWarning($"Main window already has a sheet attached; showing '{title}' as a modal alert");
                    var response = alert.RunModal();
                    tcs.TrySetResult((int)(response - FirstButtonReturn));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to show alert '{title}': {ex.Message}", ex);
                tcs.TrySetException(ex);
            }
        });

        return tcs.Task;
    }

    private static NSWindow? ResolveVisibleWindow()
    {
        var app = NSApplication.SharedApplication;
        if (app.MainWindow is { IsVisible: true } main)
            return main;
        if (app.KeyWindow is { IsVisible: true } key && key is not NSPanel)
            return key;

        NSWindow? largest = null;
        foreach (NSWindow window in app.DangerousWindows)
        {
            if (!window.IsVisible || window is NSPanel || window.ContentView is null)
                continue;
            if (largest is null || window.Frame.Width * window.Frame.Height > largest.Frame.Width * largest.Frame.Height)
                largest = window;
        }

        return largest;
    }
}
