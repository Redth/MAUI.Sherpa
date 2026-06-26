using MauiSherpa.Workloads.Models;

namespace MauiSherpa.Workloads.Services;

/// <summary>
/// Builds dotnetup command argument lists. Kept pure (no process execution) so the exact
/// flags Sherpa passes are unit-testable and verified against the real CLI surface.
///
/// Verified against dotnetup v0.1.4-preview.6.26323.4:
/// machine-readable listing is <c>list --format Json</c> (NOT <c>--json</c>); mutating
/// commands are non-interactive by default; Terminal Mode is selected by
/// <c>--set-default-install</c>; <c>--no-progress</c> suppresses spinners for captured output.
/// </summary>
public static class DotnetUpArguments
{
    /// <summary><c>dotnetup list --format Json</c></summary>
    public static IReadOnlyList<string> List(bool noVerify = false)
    {
        var args = new List<string> { "list", "--format", "Json" };
        if (noVerify)
            args.Add("--no-verify");
        return args;
    }

    /// <summary><c>dotnetup --info</c></summary>
    public static IReadOnlyList<string> Info() => new[] { "--info" };

    /// <summary>
    /// <c>dotnetup sdk install [&lt;channel&gt;] [--set-default-install] [--no-progress]</c>.
    /// When <paramref name="setDefaultInstall"/> is true this is the Terminal-Mode path:
    /// it installs and updates the user's PATH/DOTNET_ROOT in one step.
    /// </summary>
    public static IReadOnlyList<string> SdkInstall(
        string? channel = null,
        bool setDefaultInstall = false,
        bool updateGlobalJson = false,
        bool noProgress = true)
    {
        var args = new List<string> { "sdk", "install" };
        if (!string.IsNullOrWhiteSpace(channel))
            args.Add(channel);
        if (setDefaultInstall)
            args.Add("--set-default-install");
        if (updateGlobalJson)
            args.Add("--update-global-json");
        if (noProgress)
            args.Add("--no-progress");
        return args;
    }

    /// <summary><c>dotnetup sdk update [--update-global-json] [--no-progress]</c></summary>
    public static IReadOnlyList<string> SdkUpdate(bool updateGlobalJson = false, bool noProgress = true)
    {
        var args = new List<string> { "sdk", "update" };
        if (updateGlobalJson)
            args.Add("--update-global-json");
        if (noProgress)
            args.Add("--no-progress");
        return args;
    }

    /// <summary><c>dotnetup sdk uninstall &lt;channel&gt; [--source &lt;All|Explicit|GlobalJson&gt;]</c></summary>
    public static IReadOnlyList<string> SdkUninstall(string channel, DotnetUpInstallSource? source = null)
    {
        if (string.IsNullOrWhiteSpace(channel))
            throw new ArgumentException("A channel is required to uninstall.", nameof(channel));

        var args = new List<string> { "sdk", "uninstall", channel };
        if (source is { } s && s != DotnetUpInstallSource.Unknown)
        {
            args.Add("--source");
            args.Add(s.ToString());
        }
        return args;
    }

    /// <summary>
    /// <c>dotnetup runtime install [&lt;spec&gt;] [--no-progress]</c>.
    /// A spec is a channel (e.g. "10.0") or "component@version" (component: runtime, aspnetcore).
    /// </summary>
    public static IReadOnlyList<string> RuntimeInstall(string? spec = null, bool noProgress = true)
    {
        var args = new List<string> { "runtime", "install" };
        if (!string.IsNullOrWhiteSpace(spec))
            args.Add(spec);
        if (noProgress)
            args.Add("--no-progress");
        return args;
    }

    /// <summary><c>dotnetup runtime update [--no-progress]</c></summary>
    public static IReadOnlyList<string> RuntimeUpdate(bool noProgress = true)
    {
        var args = new List<string> { "runtime", "update" };
        if (noProgress)
            args.Add("--no-progress");
        return args;
    }

    /// <summary><c>dotnetup runtime uninstall &lt;spec&gt;</c></summary>
    public static IReadOnlyList<string> RuntimeUninstall(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            throw new ArgumentException("A runtime spec is required to uninstall.", nameof(spec));
        return new List<string> { "runtime", "uninstall", spec };
    }

    /// <summary><c>dotnetup update [--no-progress]</c> — updates every tracked component.</summary>
    public static IReadOnlyList<string> UpdateAll(bool noProgress = true)
    {
        var args = new List<string> { "update" };
        if (noProgress)
            args.Add("--no-progress");
        return args;
    }

    /// <summary><c>dotnetup print-env-script --shell &lt;shell&gt;</c></summary>
    public static IReadOnlyList<string> PrintEnvScript(string shell)
    {
        if (string.IsNullOrWhiteSpace(shell))
            throw new ArgumentException("A shell is required.", nameof(shell));
        return new List<string> { "print-env-script", "--shell", shell };
    }
}
