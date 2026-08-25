using System.Security.Cryptography;
using System.Text;

namespace MauiSherpa.Bundles;

public sealed record SherpaPackTextPart(string Name, string Value);

public static class SherpaPackText
{
    public const string DefaultPrefix = "SHERPA_PACK";
    public const int DefaultMaximumValueLength = 44_000;
    private const string SinglePrefix = "spk2.";
    private const string ChunkPrefix = "spk2c.";
    private const int ReservedChunkHeaderLength = 100;

    public static string Encode(ReadOnlySpan<byte> pack) =>
        SinglePrefix + Base64UrlEncode(pack);

    public static byte[] Decode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!value.StartsWith(SinglePrefix, StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported Expedition Pack text format.");
        return Base64UrlDecode(value[SinglePrefix.Length..]);
    }

    public static IReadOnlyList<SherpaPackTextPart> Split(
        ReadOnlySpan<byte> pack,
        string prefix = DefaultPrefix,
        int maximumValueLength = DefaultMaximumValueLength)
    {
        ValidatePrefix(prefix);
        if (maximumValueLength <= ReservedChunkHeaderLength)
            throw new ArgumentOutOfRangeException(nameof(maximumValueLength));

        var encoded = Encode(pack);
        if (encoded.Length <= maximumValueLength)
            return [new SherpaPackTextPart(prefix, encoded)];

        var digest = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(encoded)));
        var chunkLength = maximumValueLength - ReservedChunkHeaderLength;
        var count = (encoded.Length + chunkLength - 1) / chunkLength;
        var parts = new List<SherpaPackTextPart>(count);
        for (var index = 0; index < count; index++)
        {
            var start = index * chunkLength;
            var length = Math.Min(chunkLength, encoded.Length - start);
            var header = $"{ChunkPrefix}{index + 1}.{count}.{digest}.";
            var value = header + encoded.Substring(start, length);
            if (value.Length > maximumValueLength)
                throw new InvalidOperationException("Expedition Pack chunk exceeded its configured size.");
            parts.Add(new SherpaPackTextPart($"{prefix}_{index + 1}", value));
        }
        return parts;
    }

    public static byte[] AssembleFromEnvironment(
        Func<string, string?> getEnvironmentVariable,
        string prefix = DefaultPrefix)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        ValidatePrefix(prefix);
        var single = getEnvironmentVariable(prefix);
        var firstChunk = getEnvironmentVariable($"{prefix}_1");
        if (!string.IsNullOrEmpty(single) && !string.IsNullOrEmpty(firstChunk))
            throw new InvalidDataException($"Set either {prefix} or numbered {prefix}_N values, not both.");
        if (!string.IsNullOrEmpty(single))
            return Decode(single);
        if (string.IsNullOrEmpty(firstChunk))
            throw new InvalidDataException($"Neither {prefix} nor {prefix}_1 is set.");

        var first = ParseChunk(firstChunk);
        if (first.Index != 1)
            throw new InvalidDataException($"{prefix}_1 contains chunk {first.Index}.");

        var builder = new StringBuilder();
        builder.Append(first.Content);
        for (var index = 2; index <= first.Count; index++)
        {
            var value = getEnvironmentVariable($"{prefix}_{index}");
            if (string.IsNullOrEmpty(value))
                throw new InvalidDataException($"Missing Expedition Pack chunk {prefix}_{index} of {first.Count}.");
            var part = ParseChunk(value);
            if (part.Index != index || part.Count != first.Count || part.Digest != first.Digest)
                throw new InvalidDataException($"Expedition Pack chunk {prefix}_{index} has inconsistent metadata.");
            builder.Append(part.Content);
        }

        if (!string.IsNullOrEmpty(getEnvironmentVariable($"{prefix}_{first.Count + 1}")))
            throw new InvalidDataException($"Unexpected Expedition Pack chunk {prefix}_{first.Count + 1}.");

        var encoded = builder.ToString();
        var digest = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(encoded)));
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(digest),
                Encoding.ASCII.GetBytes(first.Digest)))
        {
            throw new InvalidDataException("Expedition Pack chunks failed their digest check.");
        }
        return Decode(encoded);
    }

    private static PackChunk ParseChunk(string value)
    {
        if (!value.StartsWith(ChunkPrefix, StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported Expedition Pack chunk format.");
        var fields = value[ChunkPrefix.Length..].Split('.', 4);
        if (fields.Length != 4 ||
            !int.TryParse(fields[0], out var index) ||
            !int.TryParse(fields[1], out var count) ||
            index <= 0 ||
            count <= 1 ||
            index > count ||
            fields[2].Length != 43 ||
            string.IsNullOrEmpty(fields[3]))
        {
            throw new InvalidDataException("Invalid Expedition Pack chunk header.");
        }
        return new PackChunk(index, count, fields[2], fields[3]);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var standard = value.Replace('-', '+').Replace('_', '/');
        standard += (standard.Length % 4) switch
        {
            0 => string.Empty,
            2 => "==",
            3 => "=",
            _ => throw new InvalidDataException("Invalid Expedition Pack Base64URL content.")
        };
        try
        {
            return Convert.FromBase64String(standard);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("Invalid Expedition Pack Base64URL content.", ex);
        }
    }

    private static void ValidatePrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix) ||
            !(char.IsAsciiLetter(prefix[0]) || prefix[0] == '_') ||
            prefix.Skip(1).Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '_')))
        {
            throw new ArgumentException("Environment prefix must contain only letters, digits, and underscores.", nameof(prefix));
        }
    }

    private sealed record PackChunk(int Index, int Count, string Digest, string Content);
}
