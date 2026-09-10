namespace MauiSherpa.AppInspector.Services;

/// <summary>What a file gets opened in.</summary>
public enum FileKind
{
    /// <summary>Nothing here can show it; the file manager offers to save it instead.</summary>
    Unsupported,

    /// <summary>A picture, for the viewer and the editor.</summary>
    Image,

    /// <summary>Text of some sort, for Monaco - which is also told which language it is.</summary>
    Text,

    /// <summary>A SQLite file, for the database workspace.</summary>
    Database
}

/// <summary>
/// Which opener a file name suggests.
/// </summary>
/// <remarks>
/// By extension, which is a guess and is treated as one: a file called <c>.db</c> is very often not
/// a database, and the agent answers that question properly by reading the header. This only decides
/// what to try first.
/// </remarks>
public static class FileKinds
{
    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".avif"
    };

    private static readonly HashSet<string> Databases = new(StringComparer.OrdinalIgnoreCase)
    {
        ".db", ".db3", ".sqlite", ".sqlite3"
    };

    /// <summary>
    /// Extension to Monaco language id. Anything text-shaped that is not in here still opens - as
    /// plain text, which is the honest answer for a file nobody has taught the editor about.
    /// </summary>
    private static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        [".md"] = "markdown",
        [".markdown"] = "markdown",
        [".json"] = "json",
        [".xml"] = "xml",
        [".xaml"] = "xml",
        [".html"] = "html",
        [".htm"] = "html",
        [".css"] = "css",
        [".js"] = "javascript",
        [".ts"] = "typescript",
        [".cs"] = "csharp",
        [".sql"] = "sql",
        [".yml"] = "yaml",
        [".yaml"] = "yaml",
        [".sh"] = "shell",
        [".txt"] = "plaintext",
        [".log"] = "plaintext",
        [".csv"] = "plaintext",
        [".config"] = "xml",
        [".plist"] = "xml",
        [".ini"] = "ini"
    };

    /// <summary>
    /// The most this kind is worth fetching to look at.
    /// </summary>
    /// <remarks>
    /// Not the agent's transfer limit, which is far higher - this is about what the viewer can
    /// usefully show. Monaco is unhappy long before a text file gets large, and a picture past this
    /// is a picture nobody is inspecting in a side panel. Over the cap, downloading it is the
    /// honest answer.
    /// </remarks>
    public static long MaxOpenBytes(FileKind kind) => kind switch
    {
        FileKind.Text => 8L * 1024 * 1024,
        FileKind.Image => 32L * 1024 * 1024,

        // A database is never fetched - it is read where it lives - so its size is not this
        // question's business.
        _ => long.MaxValue
    };

    public static FileKind For(string fileName)
    {
        var extension = Path.GetExtension(fileName);

        if (Images.Contains(extension))
            return FileKind.Image;

        if (Databases.Contains(extension))
            return FileKind.Database;

        return Languages.ContainsKey(extension) ? FileKind.Text : FileKind.Unsupported;
    }

    /// <summary>The Monaco language id for a file, defaulting to plain text.</summary>
    public static string LanguageFor(string fileName)
        => Languages.GetValueOrDefault(Path.GetExtension(fileName), "plaintext");

    /// <summary>What an <c>&lt;img&gt;</c> data URI should claim the bytes are.</summary>
    public static string ImageContentType(string fileName)
        => Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".avif" => "image/avif",
            _ => "image/jpeg"
        };
}
