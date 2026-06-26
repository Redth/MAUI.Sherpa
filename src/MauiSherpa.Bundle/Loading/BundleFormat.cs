namespace MauiSherpa.Bundle.Loading;

/// <summary>
/// Sniffs a bundle file to decide which loader owns it. Plain-JSON bundles begin
/// with <c>{</c> (after an optional UTF-8 BOM and whitespace); an encrypted
/// SQLCipher bundle has no readable header at all — even the SQLite magic is
/// encrypted — so "doesn't look like JSON" is treated as encrypted.
/// </summary>
internal static class BundleFormat
{
    public static bool LooksLikeJson(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> buffer = stackalloc byte[64];
            var read = fs.Read(buffer);
            var span = buffer[..read];

            // Tolerate a UTF-8 BOM even though the spec calls for UTF-8 without one.
            if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
                span = span[3..];

            foreach (var b in span)
            {
                if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
                    continue;
                return b == (byte)'{';
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
