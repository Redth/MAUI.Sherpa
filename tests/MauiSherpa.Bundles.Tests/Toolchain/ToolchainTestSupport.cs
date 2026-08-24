namespace MauiSherpa.Bundles.Tests.Toolchain;

internal sealed class ToolchainTestWorkspace : IDisposable
{
    public ToolchainTestWorkspace()
    {
        RootPath = Path.Combine(AppContext.BaseDirectory, "toolchain-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public string CreateFile(string relativePath, string content = "")
    {
        var path = Path.Combine(RootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public string CreateDirectory(string relativePath)
    {
        var path = Path.Combine(RootPath, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
            Directory.Delete(RootPath, recursive: true);
    }
}

internal sealed class RecordingBundleProcessRunner(Func<BundleProcessRequest, CancellationToken, BundleProcessResult> handler)
    : IBundleProcessRunner
{
    public List<BundleProcessRequest> Requests { get; } = [];

    public Task<BundleProcessResult> RunAsync(
        BundleProcessRequest request,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(handler(request, cancellationToken));
    }
}
