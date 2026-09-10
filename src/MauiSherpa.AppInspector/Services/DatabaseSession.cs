using MauiSherpa.AppInspector.Pages.Inspector.Files;
using MauiSherpa.Core.Models.Inspector;

namespace MauiSherpa.AppInspector.Services;

/// <summary>
/// What the database workspace is looking at, shared by its panels.
/// </summary>
/// <remarks>
/// Every question here is asked of the running app, against the live file. Nothing is cached beyond
/// what is on screen: the app under test is writing to this database while it is being read, and a
/// grid showing what was true a minute ago is worse than one that re-reads.
/// </remarks>
public sealed class DatabaseSession
{
    private InspectorUiClient? _client;

    public event Action? Changed;

    /// <summary>The file being browsed, or null when no database is open.</summary>
    public FileSession? File { get; private set; }

    public InspectorDatabaseSchema? Schema { get; private set; }
    public InspectorDatabaseTable? Table { get; private set; }
    public InspectorDatabaseRows? Rows { get; private set; }

    public bool IsLoading { get; private set; }

    /// <summary>Why the database would not open, if it would not.</summary>
    public string? Error { get; private set; }

    /// <summary>How many rows any one read asks for. The results say whether it bit.</summary>
    public int MaxRows { get; set; } = 500;

    public bool IsOpen => this.File is not null;

    /// <summary>The grid can only edit rows it can name, and a view has no rowid to name them by.</summary>
    public bool CanEditRows => this.Table is { HasRowId: true, Kind: "table" };

    public async Task Open(InspectorUiClient client, FileSession file)
    {
        _client = client;
        this.File = file;
        this.Schema = null;
        this.Table = null;
        this.Rows = null;
        this.Error = null;

        await this.ReloadSchema();
    }

    public void Close()
    {
        this.Design = null;
        _openQueries.Clear();
        _openQueries.Add(FilePanelIds.DatabaseQuery);
        this.QueryText.Clear();
        this.File = null;
        this.Schema = null;
        this.Table = null;
        this.Rows = null;
        this.Error = null;
        this.Changed?.Invoke();
    }

    public async Task ReloadSchema()
    {
        if (this.File is not { } file || _client is null)
            return;

        this.IsLoading = true;
        this.Error = null;
        this.Changed?.Invoke();

        try
        {
            this.Schema = await _client.GetDatabaseSchemaAsync(file.RootId, file.Path);

            // Hold the selection across a refresh where the table is still there, so running DDL
            // does not bounce the grid back to nothing.
            var keep = this.Table?.Name;
            this.Table = this.Schema.Tables.FirstOrDefault(x => x.Name == keep)
                ?? this.Schema.Tables.FirstOrDefault();
        }
        catch (Exception ex)
        {
            this.Schema = null;
            this.Error = ex.Message;
        }
        finally
        {
            this.IsLoading = false;
            this.Changed?.Invoke();
        }

        if (this.Table is not null)
            await this.ReloadRows();
    }

    public async Task Select(InspectorDatabaseTable table)
    {
        this.Table = table;
        await this.ReloadRows();
    }

    public async Task ReloadRows()
    {
        if (this.File is not { } file || this.Table is not { } table || _client is null)
            return;

        this.IsLoading = true;
        this.Changed?.Invoke();

        try
        {
            this.Rows = await _client.GetDatabaseRowsAsync(file.RootId, file.Path, table.Name, this.MaxRows);
        }
        catch (Exception ex)
        {
            this.Rows = new InspectorDatabaseRows { Error = ex.Message };
        }
        finally
        {
            this.IsLoading = false;
            this.Changed?.Invoke();
        }
    }

    public Task<InspectorDatabaseResult> Query(string sql)
        => this.Ask(client => client.QueryDatabaseAsync(this.File!.RootId, this.File.Path, sql, this.MaxRows));

    public Task<InspectorDatabaseResult> UpdateCell(long rowId, string column, string? value)
        => this.Ask(client => client.UpdateDatabaseRowAsync(
            this.File!.RootId, this.File.Path, this.Table!.Name, rowId, [new InspectorDatabaseCell(column, value)]));

    public Task<InspectorDatabaseResult> InsertRow(IReadOnlyList<InspectorDatabaseCell> values)
        => this.Ask(client => client.InsertDatabaseRowAsync(
            this.File!.RootId, this.File.Path, this.Table!.Name, values));

    public Task<InspectorDatabaseResult> DeleteRow(long rowId)
        => this.Ask(client => client.DeleteDatabaseRowAsync(
            this.File!.RootId, this.File.Path, this.Table!.Name, rowId));

    /// <summary>
    /// The one path for anything asked of the database. A refusal - no agent, no connection - is
    /// turned into the same shape a failed statement has, because to the pane showing it they are
    /// the same thing: a sentence to put in the bar under the editor.
    /// </summary>
    private async Task<InspectorDatabaseResult> Ask(Func<InspectorUiClient, Task<InspectorDatabaseResult>> ask)
    {
        if (_client is null || this.File is null)
            return new InspectorDatabaseResult { Error = "No database is open." };

        try
        {
            return await ask(_client);
        }
        catch (Exception ex)
        {
            return new InspectorDatabaseResult { Error = ex.Message };
        }
    }

    // ── query windows ──

    /// <summary>Raised when the set of open panels changes, so the dock layout can follow.</summary>
    public event Action? PanelsChanged;

    private readonly List<string> _openQueries = [FilePanelIds.DatabaseQuery];

    /// <summary>The query windows currently on screen, in the order they were opened.</summary>
    public IReadOnlyList<string> OpenQueries => _openQueries;

    /// <summary>Which designer is open, if any, and what it is designing.</summary>
    public DatabaseDesign? Design { get; private set; }

    /// <summary>
    /// Opens another query window, and answers with the panel it landed in.
    /// </summary>
    /// <returns>Null when every window this workspace can hold is already open.</returns>
    public string? OpenQueryWindow()
    {
        var free = FilePanelIds.DatabaseQueries.FirstOrDefault(x => !_openQueries.Contains(x));
        if (free is null)
            return null;

        _openQueries.Add(free);
        this.PanelsChanged?.Invoke();
        return free;
    }

    public void CloseQueryWindow(string panelId)
    {
        // The first one stays: a workspace with no query window has no way back to having one.
        if (panelId == FilePanelIds.DatabaseQuery || !_openQueries.Remove(panelId))
            return;

        this.PanelsChanged?.Invoke();
    }

    /// <summary>What each window has typed in it, kept here so a closed tab does not lose it.</summary>
    public Dictionary<string, string> QueryText { get; } = [];

    // ── the designer ──

    public void StartNewTable()
    {
        this.Design = DatabaseDesign.ForNewTable();
        this.PanelsChanged?.Invoke();
    }

    public void EditTable(InspectorDatabaseTable table)
    {
        this.Design = DatabaseDesign.ForExisting(table);
        this.PanelsChanged?.Invoke();
    }

    public void CloseDesign()
    {
        if (this.Design is null)
            return;

        this.Design = null;
        this.PanelsChanged?.Invoke();
    }

    /// <summary>
    /// Runs the designer's DDL and, if it worked, re-reads the schema so the tables list and the
    /// grid are showing what the file now contains.
    /// </summary>
    public async Task<InspectorDatabaseResult> ApplyDesign(string sql)
    {
        var result = await this.Query(sql);
        if (result.Ok)
        {
            this.CloseDesign();
            await this.ReloadSchema();
        }

        return result;
    }

    public void Notify() => this.Changed?.Invoke();
}
