using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public enum MauiCliDetailsAction
{
    None,
    Install,
    Update,
    Recheck
}

public sealed class MauiCliDetailsSession
{
    public required MauiCliToolStatus Status { get; init; }
    public MauiCliToolUpdateInfo? UpdateInfo { get; init; }
    public required Action<MauiCliDetailsAction> RequestAction { get; init; }
}

public sealed class MauiCliDetailsPage : HybridViewPage
{
    protected override string FormTitle => "MAUI CLI";
    protected override string BlazorRoute => "/modal/maui-cli-details";

    public MauiCliDetailsAction SelectedAction { get; private set; }

    public MauiCliDetailsPage(
        ModalParameterService modalParams,
        MauiCliToolStatus status,
        MauiCliToolUpdateInfo? updateInfo)
    {
        var session = new MauiCliDetailsSession
        {
            Status = status,
            UpdateInfo = updateInfo,
            RequestAction = SelectAction
        };

        modalParams.Clear();
        modalParams.Set("Session", session);

#if MACOSAPP
        MacOSPage.SetModalSheetWidth(this, 580);
        MacOSPage.SetModalSheetHeight(this, 340);
#elif LINUXGTK
        GtkPage.SetModalWidth(this, 580);
        GtkPage.SetModalHeight(this, 340);
#endif
    }

    private void SelectAction(MauiCliDetailsAction action)
    {
        if (action == MauiCliDetailsAction.None)
            return;

        SelectedAction = action;
        Dispatcher.Dispatch(CompleteClose);
    }
}
