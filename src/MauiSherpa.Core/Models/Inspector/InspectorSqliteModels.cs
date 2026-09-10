using System.Text.Json.Serialization;

namespace MauiSherpa.Core.Models.Inspector;

// ─────────────────────────── SQLite ──────────────────────────────────────
//
// The database is read where it lives, in the app's own process. Copying it out would read a
// snapshot, miss whatever is still in the write-ahead log, and overwrite the app's own writes when
// it went back - so every one of these is a question asked of the running app.

public sealed class InspectorDatabaseSchema
{
    public InspectorDatabaseTable[] Tables { get; set; } = [];
    public long FileSize { get; set; }
    public string SqliteVersion { get; set; } = "";
}

public sealed class InspectorDatabaseTable
{
    public string Name { get; set; } = "";

    /// <summary>"table" or "view" - the two things sqlite_master holds that can be selected from.</summary>
    public string Kind { get; set; } = "";

    public InspectorDatabaseColumn[] Columns { get; set; } = [];
    public InspectorDatabaseIndex[] Indexes { get; set; } = [];

    /// <summary>
    /// Whether a row here can be named for editing. A view has no rowid, and neither does a WITHOUT
    /// ROWID table - the grid is told which it is looking at rather than finding out by having a
    /// save fail.
    /// </summary>
    public bool HasRowId { get; set; }

    public bool IsView => string.Equals(Kind, "view", StringComparison.OrdinalIgnoreCase);
}

public sealed class InspectorDatabaseColumn
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool NotNull { get; set; }
    public bool PrimaryKey { get; set; }
}

public sealed class InspectorDatabaseIndex
{
    public string Name { get; set; } = "";

    /// <summary>In index order. An expression index reads "(expression)" for that position.</summary>
    public string[] Columns { get; set; } = [];

    public bool Unique { get; set; }

    /// <summary>SQLite made it for a constraint rather than a CREATE INDEX. It cannot be dropped.</summary>
    public bool Automatic { get; set; }

    /// <summary>It has a WHERE clause, so it covers only some of the rows.</summary>
    public bool Partial { get; set; }
}

/// <summary>
/// What one statement did.
/// </summary>
/// <remarks>
/// <see cref="Error"/> is a field rather than a thrown exception. A syntax error is not a failure of
/// the agent - it is the normal outcome of typing SQL, and it happens more often than success does
/// while a query is being written. Carried here, it lands in a bar under the editor where it
/// belongs, rather than arriving as an exception.
/// </remarks>
public sealed class InspectorDatabaseResult
{
    public string[] Columns { get; set; } = [];

    /// <summary>
    /// Every value is a string, including the numbers - what a grid does with a cell is show it.
    /// Null stays null, because "no value" and the word "NULL" are different cells.
    /// </summary>
    public string?[][] Rows { get; set; } = [];

    /// <summary>What a write did. -1 when the statement was a query rather than a change.</summary>
    public int RowsAffected { get; set; }

    public bool Truncated { get; set; }
    public long ElapsedMs { get; set; }
    public string? Error { get; set; }

    [JsonIgnore]
    public bool Ok => Error is null;
}

/// <summary>One table's rows, each carrying the rowid that names it.</summary>
public sealed class InspectorDatabaseRows
{
    public string[] Columns { get; set; } = [];

    /// <summary>Parallel to <see cref="Rows"/>. Empty when the table has no rowid to address by.</summary>
    public long[] RowIds { get; set; } = [];

    public string?[][] Rows { get; set; } = [];
    public bool Truncated { get; set; }
    public long ElapsedMs { get; set; }
    public string? Error { get; set; }

    [JsonIgnore]
    public bool IsEditable => RowIds.Length > 0 || Rows.Length == 0;
}

/// <param name="Value">Null is SQL NULL, which is why this is not simply an empty string.</param>
public sealed record InspectorDatabaseCell(string Column, string? Value);
