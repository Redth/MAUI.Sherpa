using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Wraps <see cref="IAppInspectorClient"/> file storage APIs behind
/// <see cref="IFileExplorerProvider"/> so the shared FileExplorerTab component can use it.
/// </summary>
public class DevFlowFileExplorerProvider : IFileExplorerProvider
{
    private readonly IAppInspectorClient _client;
    private readonly string _root;

    public string InitialPath => "/";
    public bool SupportsUpload => true;
    public bool SupportsDelete => true;

    public DevFlowFileExplorerProvider(IAppInspectorClient client, string root = "appData")
    {
        _client = client;
        _root = root;
    }

    public async Task<IReadOnlyList<DeviceFileEntry>> ListAsync(string path, CancellationToken ct = default)
    {
        var relativePath = path.TrimStart('/');
        var entries = await _client.ListFilesAsync(relativePath, _root, ct);
        return entries.Select(e => new DeviceFileEntry(
            e.Name,
            "/" + e.Path.TrimStart('/'),
            e.IsDirectory,
            e.Size ?? 0,
            null,
            e.LastModified
        )).ToList().AsReadOnly();
    }

    public async Task DownloadAsync(string remotePath, string localPath, CancellationToken ct = default)
    {
        var relativePath = remotePath.TrimStart('/');
        var content = await _client.DownloadFileAsync(relativePath, _root, ct);
        if (content == null)
            throw new InvalidOperationException($"Failed to download file: {remotePath}");

        var bytes = Convert.FromBase64String(content.Content);
        await File.WriteAllBytesAsync(localPath, bytes, ct);
    }

    public async Task UploadAsync(string localPath, string remotePath, CancellationToken ct = default)
    {
        var relativePath = remotePath.TrimStart('/');
        var bytes = await File.ReadAllBytesAsync(localPath, ct);
        await _client.UploadFileAsync(relativePath, bytes, _root, ct);
    }

    public async Task DeleteAsync(string remotePath, CancellationToken ct = default)
    {
        var relativePath = remotePath.TrimStart('/');
        await _client.DeleteFileAsync(relativePath, _root, ct);
    }
}
