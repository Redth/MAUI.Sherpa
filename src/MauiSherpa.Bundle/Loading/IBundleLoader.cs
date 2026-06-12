using MauiSherpa.Bundle.Models;

namespace MauiSherpa.Bundle.Loading;

/// <summary>
/// Loads a bundle from a path. This is the seam that isolates the on-disk
/// format from the rest of the pipeline.
/// <para>
/// Today the only format is raw JSON (<see cref="JsonBundleLoader"/>). A future
/// encrypted-binary format becomes a new <see cref="IBundleLoader"/> that
/// decrypts the bytes and then defers to
/// <see cref="SherpaBundleSerializer.Deserialize(System.ReadOnlySpan{byte})"/> —
/// nothing else in the pipeline changes.
/// </para>
/// </summary>
public interface IBundleLoader
{
    /// <summary>True if this loader recognizes the file (by extension/magic bytes).</summary>
    bool CanLoad(string path);

    /// <summary>Reads and deserializes the bundle at <paramref name="path"/>.</summary>
    SherpaBundle Load(string path);
}

/// <summary>Raised for any bundle-loading or validation failure.</summary>
public sealed class SherpaBundleException : Exception
{
    public SherpaBundleException(string message) : base(message) { }
    public SherpaBundleException(string message, Exception inner) : base(message, inner) { }
}
