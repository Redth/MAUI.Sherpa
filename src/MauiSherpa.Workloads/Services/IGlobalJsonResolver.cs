using MauiSherpa.Workloads.Models;

namespace MauiSherpa.Workloads.Services;

/// <summary>
/// Resolves a folder's <c>global.json</c> context: finds the nearest <c>global.json</c> walking up
/// from the folder (same algorithm as the <c>dotnet</c> host) and maps its <c>sdk.version</c> +
/// <c>sdk.rollForward</c> to a dotnetup channel. Purely local — no network or process calls.
/// </summary>
public interface IGlobalJsonResolver
{
    /// <summary>
    /// Walks up from <paramref name="folderPath"/> to the filesystem root, parses the first
    /// <c>global.json</c> found, and derives the channel + pinned-ness. The returned
    /// <see cref="GlobalJsonResolution"/> has the feed-dependent fields
    /// (<see cref="GlobalJsonResolution.ResolvedVersion"/>, <see cref="GlobalJsonResolution.InstalledVersion"/>,
    /// <see cref="GlobalJsonResolution.Satisfied"/>, <see cref="GlobalJsonResolution.AlreadyTracked"/>)
    /// left unset for a caller to enrich.
    /// </summary>
    GlobalJsonResolution Resolve(string folderPath);
}
