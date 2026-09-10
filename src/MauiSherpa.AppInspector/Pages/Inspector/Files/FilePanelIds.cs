namespace MauiSherpa.AppInspector.Pages.Inspector.Files;

/// <summary>
/// Dock panel ids for the file manager and the database workspace.
/// </summary>
/// <remarks>
/// <para>
/// Three places need the same strings: the hosts register them, the Files tab lays them out, and a
/// panel is told which one it is so a second query window knows it is not the first. They also end
/// up in any persisted layout, so treat them as a stored format - renaming one orphans every saved
/// layout that mentions it.
/// </para>
/// <para>
/// The query windows are a fixed set rather than as many as anyone asks for, and the docking control
/// is why: a tab's title comes from the panel <em>type</em> that was registered, so two tabs of one
/// type are two tabs both called "Query". A type per window is what gives the second one a name of
/// its own. Three is a guess at "more than anybody needs at once" - running out says so rather than
/// silently doing nothing.
/// </para>
/// </remarks>
public static class FilePanelIds
{
    // ── the file manager ──

    public const string Tree = "devflow-files-tree";
    public const string Listing = "devflow-files-listing";

    /// <summary>Whatever file is open - a picture, some text, or nothing yet.</summary>
    public const string Viewer = "devflow-files-viewer";

    // ── the database workspace ──

    public const string DatabaseTables = "devflow-db-tables";
    public const string DatabaseData = "devflow-db-data";

    /// <summary>The query window that is always there, and the one a composed statement lands in.</summary>
    public const string DatabaseQuery = "devflow-db-query";

    public static readonly string[] DatabaseQueries =
    [
        DatabaseQuery,
        "devflow-db-query-2",
        "devflow-db-query-3"
    ];

    // One panel type per kind of thing being designed, for the same reason the query windows are
    // numbered: the tab has to say what is in it. It also means a new table and an edit to another
    // can be half-written at the same time, which a single designer could not do.

    public const string DatabaseNewTable = "devflow-db-new-table";
    public const string DatabaseEditTable = "devflow-db-edit-table";

    /// <summary>The panel a design of this kind is drawn in.</summary>
    public static string ForDesign(bool creating) => creating ? DatabaseNewTable : DatabaseEditTable;
}
