using System.IO.Compression;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace MauiSherpa.Workloads.NuGet;

/// <summary>
/// Implementation of INuGetClient using NuGet.Protocol.
/// </summary>
public class NuGetClient : INuGetClient, IDisposable
{
    private readonly SourceCacheContext _cacheContext;
    private readonly ILogger _logger;
    private readonly SourceRepository _repository;
    private readonly string _cacheDirectory;

    /// <summary>
    /// Creates a new NuGetClient instance using the default nuget.org feed.
    /// </summary>
    public NuGetClient() : this("https://api.nuget.org/v3/index.json")
    {
    }

    /// <summary>
    /// Creates a new NuGetClient instance with a custom package source.
    /// </summary>
    /// <param name="packageSourceUrl">The NuGet package source URL.</param>
    public NuGetClient(string packageSourceUrl)
    {
        _cacheContext = new SourceCacheContext();
        _logger = NullLogger.Instance;
        _repository = Repository.Factory.GetCoreV3(packageSourceUrl);
        _cacheDirectory = Path.Combine(Path.GetTempPath(), "MauiSherpa.Workloads", "packages");
        Directory.CreateDirectory(_cacheDirectory);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NuGetVersion>> GetPackageVersionsAsync(
        string packageId,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        var resource = await _repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
        var versions = await resource.GetAllVersionsAsync(packageId, _cacheContext, _logger, cancellationToken);

        return versions
            .Where(v => includePrerelease || !v.IsPrerelease)
            .OrderByDescending(v => v)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<string> DownloadPackageAsync(
        string packageId,
        NuGetVersion version,
        CancellationToken cancellationToken = default)
    {
        var extractPath = Path.Combine(_cacheDirectory, $"{packageId}.{version}");

        // Return cached version if available
        if (Directory.Exists(extractPath))
            return extractPath;

        var resource = await _repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);

        using var packageStream = new MemoryStream();
        await resource.CopyNupkgToStreamAsync(
            packageId,
            version,
            packageStream,
            _cacheContext,
            _logger,
            cancellationToken);

        packageStream.Position = 0;

        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);
        archive.ExtractToDirectory(extractPath, overwriteFiles: true);

        return extractPath;
    }

    /// <inheritdoc />
    public async Task<string?> GetPackageFileContentAsync(
        string packageId,
        NuGetVersion version,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var resource = await _repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);

        using var packageStream = new MemoryStream();
        var success = await resource.CopyNupkgToStreamAsync(
            packageId,
            version,
            packageStream,
            _cacheContext,
            _logger,
            cancellationToken);

        if (!success)
            return null;

        packageStream.Position = 0;

        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(filePath) ?? archive.GetEntry(filePath.Replace('/', '\\'));

        if (entry == null)
        {
            // Try case-insensitive search
            entry = archive.Entries.FirstOrDefault(e =>
                e.FullName.Equals(filePath, StringComparison.OrdinalIgnoreCase) ||
                e.FullName.Equals(filePath.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase));
        }

        if (entry == null)
            return null;

        using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    public void Dispose()
    {
        _cacheContext.Dispose();
        GC.SuppressFinalize(this);
    }
}
