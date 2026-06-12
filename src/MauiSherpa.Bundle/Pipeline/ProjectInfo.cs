using System.Xml.Linq;
using MauiSherpa.Bundle.Loading;
using MauiSherpa.Bundle.Models;

namespace MauiSherpa.Bundle.Pipeline;

/// <summary>The resolved target project and its target frameworks (spec §6).</summary>
public sealed class ProjectInfo
{
    public required string CsprojPath { get; init; }
    public required string Directory { get; init; }
    public IReadOnlyList<string> TargetFrameworks { get; init; } = Array.Empty<string>();

    /// <summary>Finds the TFM for a platform (e.g. <c>net10.0-android</c>), or null.</summary>
    public string? GetTargetFramework(SherpaPlatform platform)
    {
        var needle = platform switch
        {
            SherpaPlatform.Android => "-android",
            SherpaPlatform.IOS => "-ios",
            SherpaPlatform.MacCatalyst => "-maccatalyst",
            SherpaPlatform.MacOS => "-macos",
            SherpaPlatform.Windows => "-windows",
            _ => null,
        };
        if (needle is null)
            return null;

        return TargetFrameworks.FirstOrDefault(tfm =>
            tfm.Contains(needle, StringComparison.OrdinalIgnoreCase)
            // "-ios" must not match "-maccatalyst"/"-macos"; those use distinct needles already.
            && !(platform == SherpaPlatform.IOS && tfm.Contains("-maccatalyst", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Resolves the project to build (spec §6.1): the explicit <c>-project</c>
    /// path, or the single <c>.csproj</c> in <paramref name="workingDirectory"/>.
    /// </summary>
    public static ProjectInfo Resolve(string? explicitPath, string workingDirectory)
    {
        string csproj;
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            csproj = Path.GetFullPath(explicitPath);
            if (!File.Exists(csproj))
                throw new SherpaBundleException($"Project file not found: {csproj}");
        }
        else
        {
            var candidates = System.IO.Directory.GetFiles(workingDirectory, "*.csproj", SearchOption.TopDirectoryOnly);
            csproj = candidates.Length switch
            {
                1 => candidates[0],
                0 => throw new SherpaBundleException(
                    $"No .csproj found in '{workingDirectory}'. Pass -project:<path>."),
                _ => throw new SherpaBundleException(
                    $"Multiple .csproj files in '{workingDirectory}'. Pass -project:<path> to disambiguate."),
            };
        }

        return new ProjectInfo
        {
            CsprojPath = csproj,
            Directory = Path.GetDirectoryName(csproj)!,
            TargetFrameworks = ReadTargetFrameworks(csproj),
        };
    }

    private static IReadOnlyList<string> ReadTargetFrameworks(string csproj)
    {
        try
        {
            var doc = XDocument.Load(csproj);
            // Namespace-agnostic element lookup (SDK-style projects have no namespace).
            string? Value(string name) => doc.Descendants()
                .FirstOrDefault(e => string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
                ?.Value;

            var multi = Value("TargetFrameworks");
            if (!string.IsNullOrWhiteSpace(multi))
                return multi.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var single = Value("TargetFramework");
            return string.IsNullOrWhiteSpace(single)
                ? Array.Empty<string>()
                : new[] { single.Trim() };
        }
        catch (Exception ex)
        {
            throw new SherpaBundleException($"Could not read target frameworks from '{csproj}': {ex.Message}", ex);
        }
    }
}
