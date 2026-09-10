namespace MauiSherpa.AppInspector.Services;

/// <summary>
/// One file, open in a viewer.
/// </summary>
/// <remarks>
/// Mutable, and shared by the panel showing it and the state that owns it - the bytes arrive after
/// the panel is already on screen, and an editor marks it dirty from inside. A record would mean
/// swapping the instance on every keystroke and re-rendering the editor with it.
/// </remarks>
public sealed class FileSession(string rootId, string path, string name, FileKind kind)
{
    public string RootId { get; } = rootId;

    /// <summary>Root-relative, forward-slashed - what every file route takes.</summary>
    public string Path { get; } = path;

    public string Name { get; } = name;
    public FileKind Kind { get; } = kind;

    /// <summary>Null until the bytes arrive. A database never has any: it is read where it lives.</summary>
    public byte[]? Content { get; set; }

    public bool IsLoading { get; set; }

    /// <summary>Why the file would not open, if it would not.</summary>
    public string? Error { get; set; }

    /// <summary>Set by an editor that has unsaved changes, so closing can say so.</summary>
    public bool IsDirty { get; set; }

    /// <summary>The bytes as a data URI, for an &lt;img&gt; or the image editor.</summary>
    public string? DataUri => this.Content is { Length: > 0 } bytes
        ? $"data:{FileKinds.ImageContentType(this.Name)};base64,{Convert.ToBase64String(bytes)}"
        : null;

    /// <summary>The bytes as text. UTF-8, and a BOM is stripped so it does not show up as a glyph.</summary>
    public string Text => this.Content is { } bytes
        ? System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF')
        : "";
}
