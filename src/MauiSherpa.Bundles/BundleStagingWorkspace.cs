namespace MauiSherpa.Bundles;

public sealed class BundleStagingWorkspace : IAsyncDisposable
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", "bin", "obj", "artifacts"
    };

    private BundleStagingWorkspace(string rootPath)
    {
        RootPath = rootPath;
    }

    public string RootPath { get; }

    public static async Task<BundleStagingWorkspace> CreateAsync(
        string sourceDirectory,
        IEnumerable<BundleReplacement> replacements,
        IReadOnlyDictionary<string, string> variables,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        var sourceRoot = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException($"Source directory '{sourceRoot}' was not found.");

        var stagingRoot = Path.Combine(Path.GetTempPath(), "maui-sherpa", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);
        TryRestrictDirectory(stagingRoot);
        var workspace = new BundleStagingWorkspace(stagingRoot);
        try
        {
            await CopyDirectoryAsync(sourceRoot, stagingRoot, cancellationToken).ConfigureAwait(false);
            await workspace.ApplyReplacementsAsync(replacements, variables, cancellationToken).ConfigureAwait(false);
            return workspace;
        }
        catch
        {
            await workspace.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!Directory.Exists(RootPath))
            return;

        await Task.Run(() => Directory.Delete(RootPath, recursive: true)).ConfigureAwait(false);
    }

    public async Task ApplyReplacementsAsync(
        IEnumerable<BundleReplacement> replacements,
        IReadOnlyDictionary<string, string> variables,
        CancellationToken cancellationToken)
    {
        foreach (var replacement in replacements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!BundleValidator.IsSafeRelativePath(replacement.Path))
                throw new BundleValidationException([$"Replacement path '{replacement.Path}' must be a safe relative path."]);

            var path = Path.GetFullPath(Path.Combine(RootPath, replacement.Path));
            if (!IsWithinRoot(path, RootPath))
                throw new BundleValidationException([$"Replacement path '{replacement.Path}' escapes the staging workspace."]);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Replacement file '{replacement.Path}' was not found.", path);

            var original = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var expanded = BundleVariableResolver.Expand(original, variables);
            var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            await File.WriteAllTextAsync(temporaryPath, expanded, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
    }

    private static async Task CopyDirectoryAsync(
        string sourceRoot,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        foreach (var sourcePath in Directory.EnumerateFileSystemEntries(
                     sourceRoot,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(sourcePath);
            if (ExcludedDirectories.Contains(name))
                continue;

            var attributes = File.GetAttributes(sourcePath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Symbolic links are not allowed in bundle staging: '{sourcePath}'.");

            var destinationPath = Path.Combine(destinationRoot, name);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                Directory.CreateDirectory(destinationPath);
                await CopyDirectoryAsync(sourcePath, destinationPath, cancellationToken).ConfigureAwait(false);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using var source = new FileStream(
                sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var destination = new FileStream(
                destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsWithinRoot(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) &&
               relative != ".." &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static void TryRestrictDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
            return;
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
