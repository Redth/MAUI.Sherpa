using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;
using MauiSherpa.Pages.Modals;
using Shiny.Mediator;

namespace MauiSherpa.Services;

public sealed class OpenJdkModalService(
    IOpenJdkService jdks,
    IOpenJdkSettingsService settings,
    IAndroidSdkService sdk,
    IFormModalService forms,
    HybridFormBridgeHolder holder,
    IOperationModalService operations,
    IAlertService alerts,
    ILoggingService logger,
    IMediator mediator)
{
    private bool busy;

    public async Task<bool> EnsureAsync()
    {
        if (await settings.GetEffectiveJdkPathAsync() != null) return true;
        await ShowAsync(true);
        return await settings.GetEffectiveJdkPathAsync() != null;
    }

    public async Task ShowAsync(bool install = false, int? majorVersion = null)
    {
        if (busy) return;
        if (sdk.IsBusy)
        {
            await alerts.ShowAlertAsync("OpenJDK", "Wait for the current Android SDK operation to finish before changing Java.");
            return;
        }
        busy = true;
        try
        {
            var selection = await forms.ShowAsync<OpenJdkSelection>(new OpenJdkPage(holder, install, majorVersion));
            if (selection == null) return;
            var path = selection.Path;
            if (selection.MajorVersion is { } major)
            {
                var result = await operations.RunAsync("Install Microsoft OpenJDK",
                    $"Install the latest Microsoft OpenJDK {major} release for this computer.",
                    async ctx =>
                    {
                        var progress = new Progress<string>(message => { ctx.LogInfo(message); ctx.SetStatus(message); });
                        path = await jdks.InstallAsync(major, progress, ctx.CancellationToken);
                        return true;
                    });
                if (!result.Success) return;
            }
            if (path == null) throw new InvalidOperationException("No JDK was selected.");
            await settings.SetCustomJdkPathAsync(path);
            await mediator.FlushStores("android:jdkpath");
            await jdks.CheckForUpdatesAsync();
            await alerts.ShowToastAsync(selection.MajorVersion != null ? "Microsoft OpenJDK installed and selected." : "Active OpenJDK changed.");
        }
        catch (Exception ex)
        {
            logger.LogError("OpenJDK setup failed.", ex);
            await alerts.ShowAlertAsync("OpenJDK Setup Failed", ex.Message);
        }
        finally { busy = false; }
    }
}
