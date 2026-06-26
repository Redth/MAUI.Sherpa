using MauiSherpa.Bundle.Models;

namespace MauiSherpa.Bundle.Loading;

/// <summary>
/// Loads a bundle from a path. This is the seam that isolates the on-disk
/// format from the rest of the pipeline.
/// <para>
/// Two formats are recognized: raw JSON (<see cref="JsonBundleLoader"/>) and the
/// encrypted SQLCipher database (<see cref="SqlCipherBundleLoader"/>). The
/// pipeline asks each loader's <see cref="CanLoad"/> in turn and hands the first
/// match the path plus the (optional) password.
/// </para>
/// </summary>
public interface IBundleLoader
{
    /// <summary>True if this loader recognizes the file (by content sniffing — see <see cref="BundleFormat"/>).</summary>
    bool CanLoad(string path);

    /// <summary>
    /// Reads and deserializes the bundle at <paramref name="path"/>. The
    /// <paramref name="password"/> is required by encrypted loaders and ignored
    /// by plain-text ones.
    /// </summary>
    Task<SherpaBundle> LoadAsync(string path, string? password, CancellationToken ct = default);
}

/// <summary>Raised for any bundle-loading or validation failure.</summary>
public sealed class SherpaBundleException : Exception
{
    public SherpaBundleException(string message) : base(message) { }
    public SherpaBundleException(string message, Exception inner) : base(message, inner) { }
}
