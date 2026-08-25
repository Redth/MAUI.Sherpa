namespace MauiSherpa.Bundles;

public sealed class BundleAssetMaterializer
{
    public async Task<IReadOnlyDictionary<string, string>> MaterializeAsync(
        SherpaBundle bundle,
        BundlePlatformConfiguration configuration,
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(configuration);
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            throw new ArgumentException("A workspace root is required.", nameof(workspaceRoot));

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var assetsDirectory = Path.Combine(workspaceRoot, ".sherpa", "assets");
        Directory.CreateDirectory(assetsDirectory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                assetsDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        foreach (var assetId in configuration.Install.AssetIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!bundle.Assets.TryGetValue(assetId, out var asset))
                throw new BundleValidationException([$"Asset '{assetId}' was not found."]);
            if (!IsSafeFileName(asset.FileName))
                throw new BundleValidationException([$"Asset '{assetId}' has an unsafe file name."]);

            byte[] content;
            try
            {
                content = Convert.FromBase64String(asset.ContentBase64);
            }
            catch (FormatException ex)
            {
                throw new BundleValidationException([$"Asset '{assetId}' content is not valid base64: {ex.Message}"]);
            }

            var path = Path.Combine(assetsDirectory, $"{Sanitize(assetId)}-{asset.FileName}");
            await File.WriteAllBytesAsync(path, content, cancellationToken).ConfigureAwait(false);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            var variableName = string.IsNullOrWhiteSpace(asset.OutputVariable) ? $"{assetId}Path" : asset.OutputVariable;
            if (result.TryGetValue(variableName, out var existingPath) &&
                !string.Equals(existingPath, path, StringComparison.OrdinalIgnoreCase))
            {
                throw new BundleValidationException([$"Asset output variable '{variableName}' is produced by multiple assets."]);
            }

            result[variableName] = path;
        }

        return result;
    }

    private static bool IsSafeFileName(string fileName) =>
        !string.IsNullOrWhiteSpace(fileName) &&
        string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal) &&
        fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static string Sanitize(string value) =>
        new(value.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_').ToArray());
}
