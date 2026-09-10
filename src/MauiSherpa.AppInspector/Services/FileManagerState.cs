using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Inspector;

namespace MauiSherpa.AppInspector.Services;

/// <summary>
/// What the file manager is currently looking at, shared by the two dock panels. The tree selects a
/// directory and the listing shows it; neither panel knows about the other, they both know about this.
/// </summary>
public sealed class FileManagerState
{
    private InspectorUiClient? _client;

    /// <summary>Raised whenever the current root, path or entry list changes.</summary>
    public event Action? Changed;

    /// <summary>
    /// Raised when a directory's contents changed underneath us, so the tree can drop its cached
    /// children for that branch. Carries the root-relative path of the directory that moved.
    /// </summary>
    public event Func<string, Task>? DirectoryInvalidated;

    public IReadOnlyList<InspectorStorageRoot> Roots { get; private set; } = [];
    public InspectorStorageRoot? Root { get; private set; }

    /// <summary>Directory currently listed, relative to <see cref="Root"/>. Empty is the root itself.</summary>
    public string Path { get; private set; } = string.Empty;

    public IReadOnlyList<InspectorFileEntry> Entries { get; private set; } = [];
    public bool IsLoading { get; private set; }
    public bool IsReady { get; private set; }

    /// <summary>Why the current directory would not open, if it would not.</summary>
    public string? Error { get; private set; }

    /// <summary>Set when the agent has no file API at all, rather than a per-directory failure.</summary>
    public string? Unsupported { get; private set; }

    public InspectorUiClient Client => _client ?? throw new InvalidOperationException("The file manager has no agent connection yet.");

    public bool Can(string operation) => this.Root?.Supports(operation) ?? false;

    /// <summary>
    /// Points the file manager at an agent. Called again on reconnect, which is why everything is
    /// reset rather than merged - the new agent may not have the same roots.
    /// </summary>
    public async Task Attach(InspectorUiClient client)
    {
        // Reconnecting hands us a different client and the old one is disposed, so anything that was
        // open is pointed at a connection that no longer exists - including a database session, which
        // closes with it because the tab is listening for this.
        this.CloseFile();

        _client = client;
        this.Roots = [];
        this.Root = null;
        this.Path = string.Empty;
        this.Entries = [];
        this.Error = null;
        this.Unsupported = null;
        this.IsReady = false;

        try
        {
            this.Roots = await client.GetStorageRootsAsync();
            if (this.Roots.Count == 0)
            {
                this.Unsupported = "This agent does not expose any browsable storage. It is either an older DevFlow agent or the app has contributed no roots.";
                return;
            }

            await this.OpenRoot(this.Roots[0]);
        }
        catch (Exception ex)
        {
            this.Unsupported = ex.Message;
        }
        finally
        {
            this.IsReady = true;
            this.Changed?.Invoke();
        }
    }

    public Task OpenRoot(InspectorStorageRoot root, string path = "")
    {
        this.Root = root;
        return this.Load(path);
    }

    public Task Navigate(string path) => this.Load(path);

    public Task Reload() => this.Load(this.Path);

    /// <summary>Move up one directory. No-op at the root.</summary>
    public Task NavigateUp()
    {
        if (this.Path.Length == 0)
            return Task.CompletedTask;

        var slash = this.Path.LastIndexOf('/');
        return this.Load(slash < 0 ? string.Empty : this.Path[..slash]);
    }

    async Task Load(string path)
    {
        if (this.Root is not { } root || _client is null)
            return;

        // Navigating away leaves a viewer showing a file from a directory nobody is looking at any
        // more, with a save button that would still write to it.
        if (this.Open is not null && !string.Equals(this.Path, path, StringComparison.Ordinal))
            this.CloseFile();

        this.Path = path;
        this.IsLoading = true;
        this.Error = null;
        this.Changed?.Invoke();

        try
        {
            var listing = await _client.ListFilesAsync(root.Id, path);
            this.Entries = listing.Entries
                .OrderByDescending(e => e.IsDirectory)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            this.Entries = [];
            this.Error = ex is InspectorFileException ? ex.Message : $"Could not read that directory. {ex.Message}";
        }
        finally
        {
            this.IsLoading = false;
            this.Changed?.Invoke();
        }
    }

    // ── the open file ──

    /// <summary>Raised when a file is opened or closed, so the dock layout can follow.</summary>
    public event Action? OpenChanged;

    /// <summary>The file being viewed or edited, or null when the browser is showing on its own.</summary>
    public FileSession? Open { get; private set; }

    /// <summary>
    /// Opens a file in whichever viewer its name suggests.
    /// </summary>
    /// <remarks>
    /// A database is not fetched: it is browsed where it lives, by asking the app. Everything else
    /// is - a picture and a text file are small enough to hand over whole, and there is nothing to
    /// gain from a round trip per scroll.
    /// </remarks>
    public async Task OpenFile(InspectorFileEntry entry)
    {
        if (this.Root is not { } root || _client is null)
            return;

        var kind = FileKinds.For(entry.Name);
        var session = new FileSession(root.Id, entry.Path, entry.Name, kind);

        if (kind is FileKind.Image or FileKind.Text)
        {
            var cap = FileKinds.MaxOpenBytes(kind);
            if (entry.Size > cap)
            {
                // Refused before it is asked for rather than after a long transfer, and it says what
                // to do instead.
                session.Error = $"{entry.Name} is {Format(entry.Size)}, and this viewer opens files up "
                    + $"to {Format(cap)}. Download it to open it elsewhere.";
                this.Open = session;
                this.OpenChanged?.Invoke();
                return;
            }

            session.IsLoading = true;
            this.Open = session;
            this.OpenChanged?.Invoke();

            try
            {
                var file = await _client.DownloadFileAsync(root.Id, entry.Path);
                session.Content = file?.Content ?? [];
            }
            catch (Exception ex)
            {
                session.Error = ex.Message;
            }
            finally
            {
                session.IsLoading = false;
            }
        }
        else
        {
            this.Open = session;
        }

        this.OpenChanged?.Invoke();
    }

    public void CloseFile()
    {
        if (this.Open is null)
            return;

        this.Open = null;
        this.OpenChanged?.Invoke();
    }

    /// <summary>
    /// Writes an edited file back and re-reads the directory. The bytes replace what is there -
    /// there is no merge, and nothing here can tell whether the app changed it meanwhile.
    /// </summary>
    public async Task SaveOpenFile(byte[] content)
    {
        if (this.Open is not { } session || _client is null)
            return;

        await _client.UploadFileAsync(session.RootId, session.Path, content);
        session.Content = content;
        session.IsDirty = false;

        await this.Reload();
        this.OpenChanged?.Invoke();
    }

    /// <summary>Tell the tree that a directory's children are stale. Safe to call when nothing is listening.</summary>
    public Task InvalidateDirectory(string path)
        => this.DirectoryInvalidated?.Invoke(path) ?? Task.CompletedTask;

    static string Format(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{size:0.#} {units[unit]}";
    }

    /// <summary>Joins a directory path and a name the way the agent expects: forward slashes, no leading one.</summary>
    public static string Combine(string directory, string name)
        => string.IsNullOrEmpty(directory) ? name : $"{directory}/{name}";
}
