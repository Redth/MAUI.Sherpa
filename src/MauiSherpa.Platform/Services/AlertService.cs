using Microsoft.Maui.ApplicationModel;

namespace MauiSherpa.Platform.Services;

public class AlertService : MauiSherpa.Core.Interfaces.IAlertService
{
    public async Task ShowAlertAsync(string title, string message, string? cancel = null)
    {
        await Application.Current!.Windows[0].Page.DisplayAlertAsync(title, message, cancel ?? "OK");
    }

    public async Task<bool> ShowConfirmAsync(string title, string message, string? confirm = null, string? cancel = null)
    {
        return await Application.Current!.Windows[0].Page.DisplayAlertAsync(
            title,
            message,
            confirm ?? "Yes",
            cancel ?? "No");
    }

    public async Task ShowToastAsync(string message)
    {
        await Application.Current!.Windows[0].Page.DisplayAlertAsync("Notification", message, "OK");
    }
}
