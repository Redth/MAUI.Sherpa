using MauiSherpa.Bundle.Models;

namespace MauiSherpa.Bundle.Loading;

/// <summary>
/// Loads a raw-JSON <c>.sherpabundle</c> file (the current format). A BOM, if
/// present, is tolerated even though the spec calls for UTF-8 without one.
/// </summary>
public sealed class JsonBundleLoader : IBundleLoader
{
    public bool CanLoad(string path)
        => path.EndsWith(".sherpabundle", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    public SherpaBundle Load(string path)
    {
        if (!File.Exists(path))
            throw new SherpaBundleException($"Bundle file not found: {path}");

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            throw new SherpaBundleException($"Could not read bundle file '{path}': {ex.Message}", ex);
        }

        return SherpaBundleSerializer.Deserialize(StripBom(bytes));
    }

    private static ReadOnlySpan<byte> StripBom(byte[] bytes)
        => bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? bytes.AsSpan(3)
            : bytes;
}
