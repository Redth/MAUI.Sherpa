using System.Text.Json;
using System.Text.RegularExpressions;
using MauiSherpa.Workloads.Models;

namespace MauiSherpa.Workloads.Services;

/// <summary>
/// Parses the machine-readable output of the dotnetup CLI:
/// <list type="bullet">
///   <item><c>dotnetup list --format Json</c> → <see cref="DotnetUpListResult"/></item>
///   <item><c>dotnetup --info</c> (text) → <see cref="DotnetUpToolInfo"/></item>
/// </list>
/// These helpers are pure so they can be unit-tested against captured fixtures.
/// </summary>
public static class DotnetUpParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Parses the JSON emitted by <c>dotnetup list --format Json</c>.
    /// Tolerates missing arrays and unknown component/source values.
    /// </summary>
    public static DotnetUpListResult ParseList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new DotnetUpListResult();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var specs = new List<DotnetUpInstallSpec>();
        if (root.TryGetProperty("installSpecs", out var specsEl) && specsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in specsEl.EnumerateArray())
            {
                var componentRaw = GetString(el, "component") ?? string.Empty;
                specs.Add(new DotnetUpInstallSpec
                {
                    Component = ParseComponent(componentRaw),
                    ComponentRaw = componentRaw,
                    VersionOrChannel = GetString(el, "versionOrChannel") ?? string.Empty,
                    Source = ParseSource(GetString(el, "source")),
                    GlobalJsonPath = GetString(el, "globalJsonPath"),
                    InstallRoot = GetString(el, "installRoot") ?? string.Empty,
                    Architecture = GetString(el, "architecture")
                });
            }
        }

        var installations = new List<DotnetUpInstallation>();
        if (root.TryGetProperty("installations", out var instEl) && instEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in instEl.EnumerateArray())
            {
                var componentRaw = GetString(el, "component") ?? string.Empty;
                installations.Add(new DotnetUpInstallation
                {
                    Component = ParseComponent(componentRaw),
                    ComponentRaw = componentRaw,
                    Version = GetString(el, "version") ?? string.Empty,
                    InstallRoot = GetString(el, "installRoot") ?? string.Empty,
                    Architecture = GetString(el, "architecture"),
                    IsValid = GetBool(el, "isValid") ?? true,
                    FrameworkName = GetString(el, "frameworkName")
                });
            }
        }

        return new DotnetUpListResult
        {
            InstallSpecs = specs,
            Installations = installations
        };
    }

    /// <summary>
    /// Parses the text emitted by <c>dotnetup --info</c> to extract tool diagnostics.
    /// Returns null when the version line cannot be located.
    /// </summary>
    public static DotnetUpToolInfo? ParseInfo(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var version = MatchField(text, "Version");
        if (string.IsNullOrEmpty(version))
            return null;

        return new DotnetUpToolInfo
        {
            Version = version!,
            Commit = MatchField(text, "Commit"),
            Architecture = MatchField(text, "Architecture"),
            Rid = MatchField(text, "RID")
        };
    }

    /// <summary>
    /// Returns the set of distinct .NET SDK versions managed by dotnetup, parsed from a list result.
    /// </summary>
    public static IReadOnlyList<string> GetManagedSdkVersions(DotnetUpListResult list) =>
        list.Installations
            .Where(i => i.Component == DotnetUpComponent.Sdk && i.IsValid)
            .Select(i => i.Version)
            .Where(v => !string.IsNullOrEmpty(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string? MatchField(string text, string label)
    {
        // Lines look like "  Version:      0.1.4-preview.6.26323.4" (value may have trailing spaces).
        var m = Regex.Match(
            text,
            $@"^\s*{Regex.Escape(label)}\s*:\s*(?<val>\S.*?)\s*$",
            RegexOptions.Multiline);
        return m.Success ? m.Groups["val"].Value.Trim() : null;
    }

    private static DotnetUpComponent ParseComponent(string raw) =>
        raw.Trim().ToLowerInvariant() switch
        {
            "sdk" => DotnetUpComponent.Sdk,
            "runtime" => DotnetUpComponent.Runtime,
            "aspnetcore" => DotnetUpComponent.AspNetCore,
            "windowsdesktop" => DotnetUpComponent.WindowsDesktop,
            _ => DotnetUpComponent.Unknown
        };

    private static DotnetUpInstallSource ParseSource(string? raw) =>
        (raw?.Trim().ToLowerInvariant()) switch
        {
            "explicit" => DotnetUpInstallSource.Explicit,
            "globaljson" => DotnetUpInstallSource.GlobalJson,
            "all" => DotnetUpInstallSource.All,
            _ => DotnetUpInstallSource.Unknown
        };

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static bool? GetBool(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && (p.ValueKind == JsonValueKind.True || p.ValueKind == JsonValueKind.False)
            ? p.GetBoolean()
            : null;
}
