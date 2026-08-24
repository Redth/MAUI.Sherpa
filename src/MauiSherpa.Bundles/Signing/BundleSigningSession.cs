using System.Security.Cryptography;

namespace MauiSherpa.Bundles;

/// <summary>
/// Prepares Apple code-signing material for a single platform run of a Sherpa bundle. On Apple
/// platforms (iOS, Mac Catalyst, macOS) this imports any configured <see cref="BundleAssetKind.AppleCertificate"/>
/// assets into a random-password temporary keychain created under the staging workspace and installs any
/// <see cref="BundleAssetKind.AppleProvisioningProfile"/> assets into the current user's provisioning
/// profile directory. Android and Windows runs are no-ops: those assets are already securely staged by
/// <see cref="BundleAssetMaterializer"/> and require no OS keychain/profile-store mutation.
/// </summary>
public sealed class BundleSigningSession : IAsyncDisposable
{
    private const string CodesignKeychainVariable = "CodesignKeychain";

    private readonly IBundleProcessRunner _processRunner;
    private readonly List<string> _installedProvisioningProfilePaths = [];
    private readonly List<string> _diagnostics = [];
    private readonly Dictionary<string, string> _variables = new(StringComparer.OrdinalIgnoreCase);
    private string? _keychainPath;
    private bool _disposed;

    private BundleSigningSession(IBundleProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    /// <summary>Variables produced by this session (for example <c>CodesignKeychain</c>) to merge into the build/deploy variable sets.</summary>
    public IReadOnlyDictionary<string, string> Variables => _variables;

    /// <summary>The temporary keychain created for this session, or <see langword="null"/> when no certificates were imported.</summary>
    public string? CodesignKeychain => _keychainPath;

    public IReadOnlyList<string> Diagnostics => _diagnostics;

    public static BundleSigningSession CreateEmpty(IBundleProcessRunner processRunner)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        return new BundleSigningSession(processRunner);
    }

    public static Task<BundleSigningSession> PrepareAsync(
        SherpaBundle bundle,
        BundlePlatformConfiguration configuration,
        BundlePlatform platform,
        string workspaceRoot,
        IReadOnlyDictionary<string, string> variables,
        IReadOnlySet<string> secretValues,
        bool dryRun,
        IBundleProcessRunner processRunner,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default) =>
        PrepareAsync(
            bundle,
            configuration,
            platform,
            workspaceRoot,
            variables,
            secretValues,
            dryRun,
            processRunner,
            host: null,
            progress,
            cancellationToken);

    internal static async Task<BundleSigningSession> PrepareAsync(
        SherpaBundle bundle,
        BundlePlatformConfiguration configuration,
        BundlePlatform platform,
        string workspaceRoot,
        IReadOnlyDictionary<string, string> variables,
        IReadOnlySet<string> secretValues,
        bool dryRun,
        IBundleProcessRunner processRunner,
        BundleSigningHost? host,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(secretValues);
        ArgumentNullException.ThrowIfNull(processRunner);
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            throw new ArgumentException("A workspace root is required.", nameof(workspaceRoot));
        cancellationToken.ThrowIfCancellationRequested();

        var session = new BundleSigningSession(processRunner);
        if (!IsApplePlatform(platform))
            return session;

        var certificates = new List<PendingCertificate>();
        var profiles = new List<PendingProfile>();

        foreach (var assetId in configuration.Install.AssetIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!bundle.Assets.TryGetValue(assetId, out var asset))
                continue;
            if (asset.Kind is not (BundleAssetKind.AppleCertificate or BundleAssetKind.AppleProvisioningProfile))
                continue;

            var path = ResolveAssetPath(assetId, asset, variables);

            if (asset.Kind == BundleAssetKind.AppleCertificate)
            {
                if (string.IsNullOrWhiteSpace(asset.PasswordVariable))
                {
                    throw new BundleValidationException(
                        [$"Certificate asset '{assetId}' must specify PasswordVariable to import into the signing keychain."]);
                }
                if (!variables.TryGetValue(asset.PasswordVariable, out var password) || string.IsNullOrEmpty(password))
                {
                    throw new BundleValidationException(
                        [$"Certificate asset '{assetId}' references password variable '{asset.PasswordVariable}', which did not resolve to a value."]);
                }
                certificates.Add(new PendingCertificate(assetId, path, password));
            }
            else
            {
                profiles.Add(new PendingProfile(assetId, asset, path));
            }
        }

        if (certificates.Count == 0 && profiles.Count == 0)
            return session;

        if (dryRun)
        {
            foreach (var certificate in certificates)
                session._diagnostics.Add($"Would import certificate asset '{certificate.AssetId}' into a temporary signing keychain.");
            foreach (var profile in profiles)
                session._diagnostics.Add($"Would install provisioning profile asset '{profile.AssetId}'.");
            return session;
        }

        var resolvedHost = host ?? new BundleSigningHost();
        if (!resolvedHost.IsMacOS())
        {
            throw new PlatformNotSupportedException(
                $"Preparing Apple signing material for {platform} requires macOS.");
        }

        try
        {
            if (certificates.Count > 0)
            {
                await session.ImportCertificatesAsync(
                    workspaceRoot, certificates, secretValues, progress, cancellationToken).ConfigureAwait(false);
                session._variables[CodesignKeychainVariable] = session._keychainPath!;
            }

            if (profiles.Count > 0)
                session.InstallProvisioningProfiles(profiles, resolvedHost);

            return session;
        }
        catch
        {
            // Best-effort cleanup of whatever partial state was created before the failure, without
            // swallowing or replacing the original exception.
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task ImportCertificatesAsync(
        string workspaceRoot,
        List<PendingCertificate> certificates,
        IReadOnlySet<string> secretValues,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var signingDirectory = Path.Combine(workspaceRoot, ".sherpa", "signing");
        Directory.CreateDirectory(signingDirectory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                signingDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var keychainPath = Path.Combine(signingDirectory, $"sherpa-{Guid.NewGuid():N}.keychain-db");
        var keychainPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var redactedSecrets = new HashSet<string>(secretValues, StringComparer.Ordinal) { keychainPassword };
        foreach (var certificate in certificates)
            redactedSecrets.Add(certificate.Password);

        await RunSecurityAsync(
            ["create-keychain", "-p", keychainPassword, keychainPath],
            redactedSecrets, "create the temporary signing keychain", progress, cancellationToken).ConfigureAwait(false);
        _keychainPath = keychainPath;

        // Avoid the keychain auto-locking mid-build; this keychain is scoped to the disposable workspace.
        await RunSecurityAsync(
            ["set-keychain-settings", "-lut", "21600", keychainPath],
            redactedSecrets, "configure the temporary signing keychain", progress, cancellationToken).ConfigureAwait(false);
        await RunSecurityAsync(
            ["unlock-keychain", "-p", keychainPassword, keychainPath],
            redactedSecrets, "unlock the temporary signing keychain", progress, cancellationToken).ConfigureAwait(false);

        foreach (var certificate in certificates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RunSecurityAsync(
                [
                    "import", certificate.Path,
                    "-k", keychainPath,
                    "-P", certificate.Password,
                    "-T", "/usr/bin/codesign",
                    "-T", "/usr/bin/security"
                ],
                redactedSecrets,
                $"import certificate asset '{certificate.AssetId}'",
                progress,
                cancellationToken).ConfigureAwait(false);
            _diagnostics.Add($"Imported certificate asset '{certificate.AssetId}' into the temporary signing keychain.");
        }

        // Required on modern macOS so codesign can use the imported keys without a UI prompt.
        await RunSecurityAsync(
            ["set-key-partition-list", "-S", "apple-tool:,apple:", "-s", "-k", keychainPassword, keychainPath],
            redactedSecrets,
            "authorize codesign access to the temporary signing keychain",
            progress,
            cancellationToken).ConfigureAwait(false);

        if (!OperatingSystem.IsWindows() && File.Exists(keychainPath))
            File.SetUnixFileMode(keychainPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private void InstallProvisioningProfiles(List<PendingProfile> profiles, BundleSigningHost host)
    {
        var directory = host.GetProvisioningProfilesDirectory();
        Directory.CreateDirectory(directory);

        foreach (var profile in profiles)
        {
            var extension = Path.GetExtension(profile.Asset.FileName);
            if (string.IsNullOrEmpty(extension))
                extension = ".mobileprovision";
            var destination = GetUniqueDestination(directory, extension);
            File.Copy(profile.Path, destination, overwrite: false);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            _installedProvisioningProfilePaths.Add(destination);
            _diagnostics.Add($"Installed provisioning profile asset '{profile.AssetId}'.");
        }
    }

    private static string GetUniqueDestination(string directory, string extension)
    {
        for (var attempt = 0; attempt < 10_000; attempt++)
        {
            var candidate = Path.Combine(directory, $"{Guid.NewGuid():N}{extension}");
            if (!File.Exists(candidate))
                return candidate;
        }

        throw new InvalidOperationException(
            $"Could not find a unique file name for a provisioning profile in '{directory}'.");
    }

    private async Task RunSecurityAsync(
        IReadOnlyList<string> arguments,
        IReadOnlySet<string> secretValues,
        string action,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(
            new BundleProcessRequest
            {
                FileName = "security",
                Arguments = arguments,
                SecretValues = secretValues
            },
            progress,
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            var details = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            details = string.IsNullOrWhiteSpace(details) ? "The command returned a non-zero exit code." : details.Trim();
            throw new InvalidOperationException($"Failed to {action}: {details}");
        }
    }

    private static string ResolveAssetPath(
        string assetId, BundleEmbeddedAsset asset, IReadOnlyDictionary<string, string> variables)
    {
        var variableName = string.IsNullOrWhiteSpace(asset.OutputVariable) ? $"{assetId}Path" : asset.OutputVariable;
        if (!variables.TryGetValue(variableName, out var path) || string.IsNullOrWhiteSpace(path))
        {
            throw new BundleValidationException(
                [$"Asset '{assetId}' was not materialized to a file path (expected variable '{variableName}')."]);
        }

        return path;
    }

    private static bool IsApplePlatform(BundlePlatform platform) =>
        platform is BundlePlatform.Ios or BundlePlatform.MacOS or BundlePlatform.MacCatalyst;

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;
        _disposed = true;

        // Cleanup is best-effort: failures here must never mask an earlier real error from signing
        // preparation or from the build/deploy phases that ran while this session was alive.
        foreach (var path in _installedProvisioningProfilePaths)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        if (_keychainPath is { } keychainPath)
        {
            try
            {
                if (File.Exists(keychainPath))
                    File.Delete(keychainPath);
            }
            catch
            {
            }
        }

        return ValueTask.CompletedTask;
    }

    private sealed record PendingCertificate(string AssetId, string Path, string Password);

    private sealed record PendingProfile(string AssetId, BundleEmbeddedAsset Asset, string Path);
}
