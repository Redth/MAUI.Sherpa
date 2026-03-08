using System.CommandLine;
using AppleAppStoreConnect;

namespace MauiSherpa.Cli.Helpers;

/// <summary>
/// Shared helper for Apple App Store Connect API authentication in CLI commands.
/// Supports credentials via CLI options or environment variables.
/// </summary>
public static class AppleConnectHelper
{
    public static readonly Option<string?> KeyIdOption = new("--key-id")
    {
        Description = "App Store Connect API Key ID (or set APPLE_KEY_ID env var)",
    };

    public static readonly Option<string?> IssuerIdOption = new("--issuer-id")
    {
        Description = "App Store Connect Issuer ID (or set APPLE_ISSUER_ID env var)",
    };

    public static readonly Option<string?> P8FileOption = new("--p8-file")
    {
        Description = "Path to .p8 private key file (or set APPLE_P8_FILE env var)",
    };

    /// <summary>
    /// Adds the shared Apple auth options to a command.
    /// </summary>
    public static void AddAuthOptions(Command cmd)
    {
        cmd.Add(KeyIdOption);
        cmd.Add(IssuerIdOption);
        cmd.Add(P8FileOption);
    }

    /// <summary>
    /// Creates an AppStoreConnectClient from CLI options or environment variables.
    /// </summary>
    public static async Task<AppStoreConnectClient> CreateClientAsync(
        string? keyId, string? issuerId, string? p8File)
    {
        keyId ??= Environment.GetEnvironmentVariable("APPLE_KEY_ID");
        issuerId ??= Environment.GetEnvironmentVariable("APPLE_ISSUER_ID");
        p8File ??= Environment.GetEnvironmentVariable("APPLE_P8_FILE");

        if (string.IsNullOrWhiteSpace(keyId))
            throw new InvalidOperationException("Apple Key ID is required. Use --key-id or set APPLE_KEY_ID.");
        if (string.IsNullOrWhiteSpace(issuerId))
            throw new InvalidOperationException("Apple Issuer ID is required. Use --issuer-id or set APPLE_ISSUER_ID.");
        if (string.IsNullOrWhiteSpace(p8File))
            throw new InvalidOperationException("P8 key file is required. Use --p8-file or set APPLE_P8_FILE.");

        p8File = ExpandPath(p8File);
        if (!File.Exists(p8File))
            throw new FileNotFoundException($"P8 key file not found: {p8File}");

        var p8Content = await File.ReadAllTextAsync(p8File);
        var privateKeyBase64 = ConvertP8ToBase64(p8Content);
        var normalizedKeyId = NormalizeKeyId(keyId);
        var normalizedIssuerId = NormalizeIssuerId(issuerId);

        var config = new AppStoreConnectConfiguration(
            normalizedKeyId,
            normalizedIssuerId,
            privateKeyBase64.Trim());

        return new AppStoreConnectClient(config);
    }

    /// <summary>
    /// Resolves a bundle ID identifier (e.g. com.company.app) to its ASC resource ID.
    /// </summary>
    public static async Task<(string ResourceId, string Identifier, string Name)> ResolveBundleIdAsync(
        AppStoreConnectClient client, string bundleIdOrIdentifier)
    {
        var response = await client.ListBundleIdsAsync(
            filterId: null, filterIdentifier: null, filterName: null,
            filterPlatform: null, filterSeedId: null,
            include: null, sort: null, limit: 100,
            limitProfiles: null, limitBundleIdCapabilities: null,
            fieldsBundleIds: null, fieldsProfiles: null,
            fieldBundleIdCapabilities: null, fieldsApps: null,
            cancellationToken: default);

        foreach (var b in response.Data)
        {
            var id = b.Id;
            var identifier = b.Attributes?.Identifier ?? "";
            var name = b.Attributes?.Name ?? "";

            // Match by resource ID or bundle identifier
            if (id.Equals(bundleIdOrIdentifier, StringComparison.OrdinalIgnoreCase) ||
                identifier.Equals(bundleIdOrIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                return (id, identifier, name);
            }
        }

        throw new InvalidOperationException(
            $"Bundle ID '{bundleIdOrIdentifier}' not found in App Store Connect. " +
            "Use the bundle identifier (e.g. com.company.app) or the ASC resource ID.");
    }

    private static string ConvertP8ToBase64(string p8Content)
    {
        p8Content = p8Content
            .Trim()
            .Trim('"')
            .Trim()
            .Replace("\\r", "\r")
            .Replace("\\n", "\n");

        var lines = p8Content.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith("-----"))
            .Where(l => !string.IsNullOrEmpty(l));
        return string.Join("", lines);
    }

    private static string NormalizeKeyId(string keyId) =>
        new string(keyId.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    private static string NormalizeIssuerId(string issuerId)
    {
        var sanitized = new string(issuerId.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        return Guid.TryParse(sanitized, out var parsed) ? parsed.ToString() : sanitized;
    }

    private static string ExpandPath(string path)
    {
        if (path.StartsWith('~'))
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                path[1..].TrimStart('/'));
        return Path.GetFullPath(path);
    }
}
