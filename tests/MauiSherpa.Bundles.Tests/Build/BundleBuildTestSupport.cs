namespace MauiSherpa.Bundles.Tests.Build;

/// <summary>
/// A disposable temp-directory sandbox for <see cref="BundleBuildService"/> tests. Every test gets
/// its own uniquely named root so tests can run in parallel without interfering with each other,
/// and no real dotnet/MAUI tooling is ever invoked - only plain files/directories are created.
/// </summary>
internal sealed class BundleBuildWorkspace : IDisposable
{
    public BundleBuildWorkspace()
    {
        RootPath = Path.Combine(AppContext.BaseDirectory, "bundle-build-tests", Guid.NewGuid().ToString("N"));
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

    /// <summary>
    /// Creates a minimal .csproj declaring the given semicolon-joined target framework(s), the
    /// same shape <see cref="BundleBuildService.InferTargetFramework"/> reads.
    /// </summary>
    public string CreateProject(string relativePath, string targetFrameworks) => CreateFile(
        relativePath,
        $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFrameworks>{targetFrameworks}</TargetFrameworks>
          </PropertyGroup>
        </Project>
        """);

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

/// <summary>Fake <see cref="IBundleProcessRunner"/> that never touches a real process.</summary>
internal sealed class FakeBundleProcessRunner(Func<BundleProcessRequest, BundleProcessResult> handler)
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
        return Task.FromResult(handler(request));
    }
}

/// <summary>
/// Fails any test that (incorrectly) invokes a process, used to prove dry-run and validation
/// failures never reach process execution.
/// </summary>
internal sealed class RejectingBundleProcessRunner : IBundleProcessRunner
{
    public Task<BundleProcessResult> RunAsync(
        BundleProcessRequest request,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException($"Process '{request.FileName}' should not have been started.");
}
