using MauiSherpa.Core.Models.Inspector;

namespace MauiSherpa.AppInspector.Services;

/// <summary>
/// One row in the file manager's directory tree - a storage root, or a directory inside one. The two
/// are the same shape; only the glyph and the "can I go up from here" rule differ.
/// </summary>
public class FileManagerNode
{
    public required string RootId { get; init; }

    /// <summary>"" for the root itself, otherwise the path inside it.</summary>
    public required string Path { get; init; }

    public required string Name { get; init; }

    public bool IsRoot => this.Path.Length == 0;

    /// <summary>Set on root nodes so the tree can flag storage the OS is free to empty.</summary>
    public bool MayBeClearedBySystem { get; init; }

    /// <summary>Stable across reloads, so expansion and selection survive a refresh.</summary>
    public string Key => $"{this.RootId}:{this.Path}";

    public override bool Equals(object? obj) => obj is FileManagerNode other && other.Key == this.Key;
    public override int GetHashCode() => this.Key.GetHashCode();

    public static FileManagerNode ForRoot(InspectorStorageRoot root) => new()
    {
        RootId = root.Id,
        Path = string.Empty,
        Name = root.DisplayName,
        MayBeClearedBySystem = root.MayBeClearedBySystem
    };
}
