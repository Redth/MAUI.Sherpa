using MauiSherpa.Bundle.Models;

namespace MauiSherpa.Bundle.Loading;

/// <summary>
/// Loads an encrypted SQLCipher bundle (the format written by the app's bundle
/// builder). Recognizes any existing file that is not plain JSON. The password
/// comes from the caller (the CLI <c>-password</c> flag); when none is supplied
/// it falls back to the <see cref="PasswordEnvironmentVariable"/> so secrets need
/// not appear in the process's argument list.
/// </summary>
public sealed class SqlCipherBundleLoader : IBundleLoader
{
    /// <summary>Environment variable consulted when no <c>-password</c> is passed.</summary>
    public const string PasswordEnvironmentVariable = "SHERPA_BUNDLE_PASSWORD";

    public bool CanLoad(string path)
        => File.Exists(path) && !BundleFormat.LooksLikeJson(path);

    public Task<SherpaBundle> LoadAsync(string path, string? password, CancellationToken ct = default)
    {
        var key = string.IsNullOrEmpty(password)
            ? Environment.GetEnvironmentVariable(PasswordEnvironmentVariable)
            : password;

        if (string.IsNullOrEmpty(key))
            throw new SherpaBundleException(
                $"'{Path.GetFileName(path)}' is an encrypted bundle. " +
                $"Provide -password:<value> or set the {PasswordEnvironmentVariable} environment variable.");

        return SqlCipherBundleStore.LoadAsync(path, key, ct);
    }
}
