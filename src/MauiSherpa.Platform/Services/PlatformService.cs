using Microsoft.Maui.ApplicationModel;

namespace MauiSherpa.Platform.Services;

public class PlatformService : MauiSherpa.Core.Interfaces.IPlatformService
{
    public bool IsWindows => DeviceInfo.Platform == DevicePlatform.WinUI;
    public bool IsMacCatalyst => DeviceInfo.Platform == DevicePlatform.MacCatalyst;
    public string PlatformName => DeviceInfo.Platform.ToString();
}
