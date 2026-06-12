namespace MauiSherpa.Bundle.Models;

/// <summary>Target platforms (spec §1 <c>-platform</c>, §3).</summary>
public enum SherpaPlatform
{
    Android,
    IOS,
    MacOS,
    MacCatalyst,
    Windows,
}

/// <summary>Pipeline steps, in execution order (spec §1 <c>-step</c>).</summary>
public enum SherpaStep
{
    Setup,
    Build,
    Deploy,
}

public static class SherpaPlatformExtensions
{
    /// <summary>The token used on the CLI and as bundle keys (e.g. <c>ios</c>, <c>maccatalyst</c>).</summary>
    public static string ToToken(this SherpaPlatform platform) => platform switch
    {
        SherpaPlatform.Android => "android",
        SherpaPlatform.IOS => "ios",
        SherpaPlatform.MacOS => "macos",
        SherpaPlatform.MacCatalyst => "maccatalyst",
        SherpaPlatform.Windows => "windows",
        _ => platform.ToString().ToLowerInvariant(),
    };

    /// <summary>The PascalCase key used in <c>sherpa-output.json</c> and bundle blocks.</summary>
    public static string ToDisplayName(this SherpaPlatform platform) => platform switch
    {
        SherpaPlatform.IOS => "iOS",
        SherpaPlatform.MacOS => "MacOS",
        SherpaPlatform.MacCatalyst => "MacCatalyst",
        _ => platform.ToString(),
    };

    public static bool TryParse(string token, out SherpaPlatform platform)
    {
        switch (token.Trim().ToLowerInvariant())
        {
            case "android": platform = SherpaPlatform.Android; return true;
            case "ios": platform = SherpaPlatform.IOS; return true;
            case "macos": platform = SherpaPlatform.MacOS; return true;
            case "maccatalyst": platform = SherpaPlatform.MacCatalyst; return true;
            case "windows": platform = SherpaPlatform.Windows; return true;
            default: platform = default; return false;
        }
    }
}

public static class SherpaStepExtensions
{
    public static bool TryParse(string token, out SherpaStep step)
    {
        switch (token.Trim().ToLowerInvariant())
        {
            case "setup": step = SherpaStep.Setup; return true;
            case "build": step = SherpaStep.Build; return true;
            case "deploy": step = SherpaStep.Deploy; return true;
            default: step = default; return false;
        }
    }
}
