namespace MauiSherpa.Bundles.Tests.Signing;

internal sealed class RecordingBundleProcessRunner(Func<BundleProcessRequest, BundleProcessResult>? handler = null)
    : IBundleProcessRunner
{
    private readonly Func<BundleProcessRequest, BundleProcessResult> _handler =
        handler ?? (_ => new BundleProcessResult(0, string.Empty, string.Empty));

    public List<BundleProcessRequest> Requests { get; } = [];

    public Task<BundleProcessResult> RunAsync(
        BundleProcessRequest request,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_handler(request));
    }
}

internal sealed class SigningTestWorkspace : IDisposable
{
    public SigningTestWorkspace()
    {
        RootPath = Path.Combine(AppContext.BaseDirectory, "signing-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
        ProfilesDirectory = Path.Combine(RootPath, "profiles");
        Directory.CreateDirectory(ProfilesDirectory);
    }

    public string RootPath { get; }

    public string ProfilesDirectory { get; }

    public string CreateFile(string relativePath, string content = "test-content")
    {
        var path = Path.Combine(RootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public BundleSigningHost CreateHost(bool isMacOS = true) =>
        new()
        {
            IsMacOS = () => isMacOS,
            GetProvisioningProfilesDirectory = () => ProfilesDirectory
        };

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
            Directory.Delete(RootPath, recursive: true);
    }
}

internal static class SigningTestData
{
    public static SherpaBundle CreateBundle(Dictionary<string, BundleEmbeddedAsset> assets) =>
        new()
        {
            Name = "signing-test-bundle",
            Assets = new Dictionary<string, BundleEmbeddedAsset>(assets, StringComparer.OrdinalIgnoreCase)
        };

    public static BundlePlatformConfiguration CreateConfiguration(params string[] assetIds) =>
        new()
        {
            Install = new BundleInstallConfiguration
            {
                AssetIds = assetIds.ToList()
            }
        };

    public static BundleEmbeddedAsset CertificateAsset(string? passwordVariable = "CertPassword") =>
        new()
        {
            Kind = BundleAssetKind.AppleCertificate,
            FileName = "cert.p12",
            ContentBase64 = Convert.ToBase64String([1, 2, 3]),
            PasswordVariable = passwordVariable
        };

    public static BundleEmbeddedAsset ProfileAsset(string fileName = "profile.mobileprovision") =>
        new()
        {
            Kind = BundleAssetKind.AppleProvisioningProfile,
            FileName = fileName,
            ContentBase64 = Convert.ToBase64String([4, 5, 6])
        };
}
