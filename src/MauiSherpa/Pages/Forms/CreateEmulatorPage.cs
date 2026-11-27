using MauiSherpa.Core.Interfaces;
#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Forms;

public record EmulatorCreateResult(
    string Name,
    string SystemImage,
    bool NeedsInstall,
    string? Device,
    int? RamSizeMb,
    int? InternalStorageMb,
    string? SdCardSize,
    string? Skin);

public class CreateEmulatorPage : HybridFormPage<EmulatorCreateResult>
{
    protected override string FormTitle => "Create Emulator";
    protected override string SubmitButtonText => "Create";
    protected override string BlazorRoute => "/modal/create-emulator";

    public CreateEmulatorPage(
        HybridFormBridgeHolder bridgeHolder,
        List<string> installedImages,
        List<string> availableImages,
        List<AvdDeviceDefinition> deviceDefinitions,
        List<string> skins)
        : base(bridgeHolder)
    {
        Bridge.Parameters["InstalledImages"] = installedImages;
        Bridge.Parameters["AvailableImages"] = availableImages;
        Bridge.Parameters["DeviceDefinitions"] = deviceDefinitions;
        Bridge.Parameters["Skins"] = skins;

#if MACOSAPP
        MacOSPage.SetModalSheetMinWidth(this, 560);
#elif LINUXGTK
        GtkPage.SetModalMinWidth(this, 560);
#endif
    }
}
