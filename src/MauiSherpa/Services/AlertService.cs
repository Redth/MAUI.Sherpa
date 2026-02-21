namespace MauiSherpa.Services;

public class AlertService : MauiSherpa.Core.Interfaces.IAlertService
{
    private readonly BlazorToastService _toastService;

    public AlertService(BlazorToastService toastService)
    {
        _toastService = toastService;
    }

    public async Task ShowAlertAsync(string title, string message, string? cancel = null)
    {
        await Application.Current!.Windows[0].Page!.DisplayAlert(title, message, cancel ?? "OK");
    }

    public async Task<bool> ShowConfirmAsync(string title, string message, string? confirm = null, string? cancel = null)
    {
        return await Application.Current!.Windows[0].Page!.DisplayAlert(
            title,
            message,
            confirm ?? "Yes",
            cancel ?? "No");
    }

    public Task ShowToastAsync(string message)
    {
        // Avoid MainThread Essentials static API on Linux GTK; use MAUI dispatcher directly.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher?.IsDispatchRequired ?? false)
            dispatcher.Dispatch(() => _toastService.ShowSuccess(message));
        else
            _toastService.ShowSuccess(message);
        return Task.CompletedTask;
    }
}
