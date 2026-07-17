using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public sealed class ProjectDotnetContextSession
{
    public required string FolderPath { get; init; }
    public bool HasChanges { get; set; }
}

public sealed class ProjectDotnetContextPage : HybridViewPage
{
    protected override string FormTitle => "Project .NET Context";
    protected override string BlazorRoute => "/modal/project-dotnet-context";

    public ProjectDotnetContextSession Session { get; }
    public bool HasChanges => Session.HasChanges;

    public ProjectDotnetContextPage(ModalParameterService modalParams, string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        Session = new ProjectDotnetContextSession
        {
            FolderPath = Path.GetFullPath(folderPath)
        };

        modalParams.Clear();
        modalParams.Set("Session", Session);

        var window = Application.Current?.Windows.FirstOrDefault();
        var width = (int)Math.Clamp(Math.Round((window?.Width ?? 1200) * 0.9), 820, 1400);
        var height = (int)Math.Clamp(Math.Round((window?.Height ?? 800) * 0.9), 620, 1000);

#if MACOSAPP
        MacOSPage.SetModalSheetWidth(this, width);
        MacOSPage.SetModalSheetHeight(this, height);
#elif LINUXGTK
        GtkPage.SetModalWidth(this, width);
        GtkPage.SetModalHeight(this, height);
#endif
    }
}
