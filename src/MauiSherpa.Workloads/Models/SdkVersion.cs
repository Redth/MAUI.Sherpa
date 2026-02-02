namespace MauiSherpa.Workloads.Models;

/// <summary>
/// Represents a .NET SDK version with its components parsed.
/// </summary>
public record SdkVersion
{
    /// <summary>
    /// The full version string (e.g., "9.0.100").
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// The major version number (e.g., 9).
    /// </summary>
    public int Major { get; init; }

    /// <summary>
    /// The minor version number (e.g., 0).
    /// </summary>
    public int Minor { get; init; }

    /// <summary>
    /// The patch/feature band number (e.g., 100).
    /// </summary>
    public int Patch { get; init; }

    /// <summary>
    /// The feature band (e.g., "9.0.100" for SDK "9.0.105").
    /// The feature band is the SDK version with the last two digits zeroed out.
    /// </summary>
    public string FeatureBand => $"{Major}.{Minor}.{(Patch / 100) * 100}";

    /// <summary>
    /// The runtime version this SDK targets (e.g., "9.0" for SDK "9.0.100").
    /// </summary>
    public string RuntimeVersion => $"{Major}.{Minor}";

    /// <summary>
    /// Whether this is a preview/RC release.
    /// </summary>
    public bool IsPreview { get; init; }

    /// <summary>
    /// The preview label if applicable (e.g., "preview.1", "rc.1").
    /// </summary>
    public string? PreviewLabel { get; init; }

    /// <summary>
    /// Parses an SDK version string into an SdkVersion object.
    /// </summary>
    public static SdkVersion Parse(string version)
    {
        var previewIndex = version.IndexOf('-');
        var versionPart = previewIndex >= 0 ? version[..previewIndex] : version;
        var previewLabel = previewIndex >= 0 ? version[(previewIndex + 1)..] : null;

        var parts = versionPart.Split('.');
        if (parts.Length < 3)
            throw new ArgumentException($"Invalid SDK version format: {version}", nameof(version));

        return new SdkVersion
        {
            Version = version,
            Major = int.Parse(parts[0]),
            Minor = int.Parse(parts[1]),
            Patch = int.Parse(parts[2]),
            IsPreview = previewLabel != null,
            PreviewLabel = previewLabel
        };
    }

    public override string ToString() => Version;
}
