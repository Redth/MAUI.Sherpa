using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Services;

public class MacOSPlatformService : IPlatformService
{
    public bool IsWindows => false;
    public bool IsMacCatalyst => false;
    public bool IsMacOS => true;
    public string PlatformName => "macOS";
}
