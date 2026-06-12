using MauiSherpa.Bundle.Loading;

namespace MauiSherpa.Bundle.Steps;

/// <summary>Helpers for materializing base64-encoded signing assets to disk.</summary>
internal static class SigningAssets
{
    public static byte[] DecodeBase64(string? content, string what)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new SherpaBundleException($"{what} has empty Content.");
        try
        {
            return Convert.FromBase64String(content.Trim());
        }
        catch (FormatException ex)
        {
            throw new SherpaBundleException($"{what} Content is not valid base64: {ex.Message}", ex);
        }
    }

    public static string WriteAsset(string scratchDir, string subDir, string fileName, byte[] bytes)
    {
        var dir = Path.Combine(scratchDir, subDir);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>
    /// Extracts the embedded plist text from a (CMS-signed) provisioning profile
    /// and pulls out a string value such as <c>UUID</c> or <c>Name</c>.
    /// </summary>
    public static string? ReadProfilePlistValue(byte[] profileBytes, string key)
    {
        var text = System.Text.Encoding.ASCII.GetString(profileBytes);
        var start = text.IndexOf("<?xml", StringComparison.Ordinal);
        var end = text.IndexOf("</plist>", StringComparison.Ordinal);
        if (start < 0 || end < 0)
            return null;

        var plist = text.Substring(start, end - start);
        var keyTag = $"<key>{key}</key>";
        var keyIdx = plist.IndexOf(keyTag, StringComparison.Ordinal);
        if (keyIdx < 0)
            return null;

        var valueStart = plist.IndexOf("<string>", keyIdx, StringComparison.Ordinal);
        if (valueStart < 0)
            return null;
        valueStart += "<string>".Length;
        var valueEnd = plist.IndexOf("</string>", valueStart, StringComparison.Ordinal);
        return valueEnd < 0 ? null : plist[valueStart..valueEnd];
    }
}
