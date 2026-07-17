using MauiSherpa.Pages.Forms;
using MauiSherpa.Workloads.Models;
#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public enum DotnetUpDetailsAction
{
    None,
    Install,
    Update,
    Reinstall
}

public sealed class DotnetUpDetailsSession
{
    public required bool IsInstalled { get; init; }
    public DotnetUpToolInfo? ToolInfo { get; init; }
    public DotnetUpToolUpdateInfo? ToolUpdateInfo { get; init; }
    public IReadOnlyList<string> ManagedRoots { get; init; } = [];
    public string? ExecutablePath { get; init; }
    public string? ToolDirectory { get; init; }
    public required Action<DotnetUpDetailsAction> RequestAction { get; init; }
}

public sealed class DotnetUpDetailsPage : HybridViewPage
{
    protected override string FormTitle => "dotnetup";
    protected override string BlazorRoute => "/modal/dotnetup-details";

    public DotnetUpDetailsAction SelectedAction { get; private set; }

    public DotnetUpDetailsPage(
        ModalParameterService modalParams,
        bool isInstalled,
        DotnetUpToolInfo? toolInfo,
        DotnetUpToolUpdateInfo? toolUpdateInfo,
        IReadOnlyList<string> managedRoots,
        string? executablePath,
        string? toolDirectory)
    {
        var session = new DotnetUpDetailsSession
        {
            IsInstalled = isInstalled,
            ToolInfo = toolInfo,
            ToolUpdateInfo = toolUpdateInfo,
            ManagedRoots = managedRoots,
            ExecutablePath = executablePath,
            ToolDirectory = toolDirectory,
            RequestAction = SelectAction
        };

        modalParams.Clear();
        modalParams.Set("Session", session);

#if MACOSAPP
        MacOSPage.SetModalSheetWidth(this, 720);
        MacOSPage.SetModalSheetHeight(this, 620);
#elif LINUXGTK
        GtkPage.SetModalWidth(this, 720);
        GtkPage.SetModalHeight(this, 620);
#endif
    }

    private void SelectAction(DotnetUpDetailsAction action)
    {
        if (action == DotnetUpDetailsAction.None)
            return;

        SelectedAction = action;
        Dispatcher.Dispatch(CompleteClose);
    }
}
