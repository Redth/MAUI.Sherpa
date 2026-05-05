using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Wraps <see cref="IDeviceFileService"/> (ADB) behind <see cref="IFileExplorerProvider"/>
/// so the shared FileExplorerTab component can use it.
/// </summary>
public class AdbFileExplorerProvider : IFileExplorerProvider
{
    private readonly IDeviceFileService _fileService;
    private readonly string _serial;

    public string InitialPath => "/sdcard";
    public bool SupportsUpload => true;
    public bool SupportsDelete => true;

    public AdbFileExplorerProvider(IDeviceFileService fileService, string serial)
    {
        _fileService = fileService;
        _serial = serial;
    }

    public Task<IReadOnlyList<DeviceFileEntry>> ListAsync(string path, CancellationToken ct = default)
        => _fileService.ListAsync(_serial, path, ct);

    public Task DownloadAsync(string remotePath, string localPath, CancellationToken ct = default)
        => _fileService.PullAsync(_serial, remotePath, localPath, ct: ct);

    public Task UploadAsync(string localPath, string remotePath, CancellationToken ct = default)
        => _fileService.PushAsync(_serial, localPath, remotePath, ct: ct);

    public Task DeleteAsync(string remotePath, CancellationToken ct = default)
        => _fileService.DeleteAsync(_serial, remotePath, ct);
}
