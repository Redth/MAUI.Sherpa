using Microsoft.Maui.ApplicationModel;

namespace MauiSherpa.Services;

public class PlatformService : MauiSherpa.Core.Interfaces.IPlatformService
{
    public bool IsWindows => DeviceInfo.Platform == DevicePlatform.WinUI;
    public bool IsMacCatalyst => DeviceInfo.Platform == DevicePlatform.MacCatalyst;
    public bool IsMacOS => false;
    public bool IsLinux => OperatingSystem.IsLinux();
    public bool HasNativeToolbar => IsWindows || IsLinux;
    public string PlatformName => DeviceInfo.Platform.ToString();
}
