using System.Text;
using MauiSherpa.Core.Models.Inspector;

namespace MauiSherpa.AppInspector.Services;

/// <summary>
/// A table being designed - either a new one, or changes to one that already exists.
/// </summary>
/// <remarks>
/// <para>
/// The designer composes SQL rather than issuing calls, and shows it before it runs. That is on
/// purpose: SQLite's ALTER TABLE can only add a column, rename a column, rename the table, and drop
/// a column - anything else means rebuilding the table, and a reader should be able to see which of
/// those they are about to do rather than trusting a button.
/// </para>
/// <para>
/// Nothing here escapes user input into the statement by hand: identifiers are quoted with
/// <see cref="Quote"/>, and the SQL is shown for review before it runs.
/// </para>
/// </remarks>
public sealed class DatabaseDesign
{
    /// <summary>Types offered in the column editor. SQLite will accept anything; these are the useful ones.</summary>
    public static readonly string[] Types = ["TEXT", "INTEGER", "REAL", "BLOB", "NUMERIC"];

    private DatabaseDesign(bool creating, string? originalName)
    {
        this.IsNew = creating;
        this.OriginalName = originalName;
    }

    /// <summary>True for a table that does not exist yet.</summary>
    public bool IsNew { get; }

    /// <summary>What the table is called now, for an edit. Null for a new one.</summary>
    public string? OriginalName { get; }

    public string Name { get; set; } = "";

    public List<DesignColumn> Columns { get; } = [];

    /// <summary>Columns the table already has, so an edit can tell an addition from a change.</summary>
    public List<DesignColumn> Original { get; } = [];

    public string Title => this.IsNew ? "New table" : $"Edit {this.OriginalName}";

    public static DatabaseDesign ForNewTable()
    {
        var design = new DatabaseDesign(creating: true, originalName: null) { Name = "" };

        // A table with an INTEGER PRIMARY KEY is the one every editable grid wants: it is the rowid
        // under another name, so rows stay addressable.
        design.Columns.Add(new DesignColumn { Name = "id", Type = "INTEGER", PrimaryKey = true });
        return design;
    }

    public static DatabaseDesign ForExisting(InspectorDatabaseTable table)
    {
        var design = new DatabaseDesign(creating: false, originalName: table.Name) { Name = table.Name };

        foreach (var column in table.Columns)
        {
            var edited = new DesignColumn
            {
                Name = column.Name,
                Type = column.Type,
                NotNull = column.NotNull,
                PrimaryKey = column.PrimaryKey,
                Existing = true
            };

            design.Columns.Add(edited);

            // The snapshot shares the edited column's key, which is the whole point of the key: it
            // is what lets a rename be told from a drop-and-add once the name has changed.
            design.Original.Add(new DesignColumn
            {
                Key = edited.Key,
                Name = column.Name,
                Type = column.Type,
                NotNull = column.NotNull,
                PrimaryKey = column.PrimaryKey,
                Existing = true
            });
        }

        return design;
    }

    /// <summary>What is wrong with the design, or empty when it can be applied.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(this.Name))
            problems.Add("The table needs a name.");

        var named = this.Columns.Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToList();
        if (named.Count == 0)
            problems.Add("A table needs at least one column.");

        var duplicate = named
            .GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
            problems.Add($"There is more than one column called '{duplicate.Key}'.");

        if (this.Columns.Any(c => string.IsNullOrWhiteSpace(c.Name)))
            problems.Add("Every column needs a name.");

        // SQLite cannot add a NOT NULL column without a default: the rows already there would have
        // nothing to put in it. Saying so here beats the error arriving from the engine.
        foreach (var added in this.Columns.Where(c => !c.Existing && c.NotNull && string.IsNullOrWhiteSpace(c.DefaultValue)))
        {
            if (!this.IsNew)
                problems.Add($"'{added.Name}' is NOT NULL, so it needs a default before it can be added to an existing table.");
        }

        return problems;
    }

    /// <summary>The statements this design would run, in order.</summary>
    public string BuildSql()
        => this.IsNew ? this.BuildCreate() : this.BuildAlter();

    private string BuildCreate()
    {
        var sql = new StringBuilder();
        sql.Append("CREATE TABLE ").Append(Quote(this.Name.Trim())).AppendLine(" (");

        var lines = this.Columns
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .Select(c => "    " + Definition(c))
            .ToList();

        sql.AppendLine(string.Join(",\n", lines));
        sql.Append(");");
        return sql.ToString();
    }

    /// <summary>
    /// The subset of changes SQLite's ALTER TABLE can express: a rename, added columns, and renamed
    /// columns. A type or constraint change on an existing column is not one of them, and is
    /// reported rather than silently dropped.
    /// </summary>
    private string BuildAlter()
    {
        var statements = new List<string>();
        var table = this.OriginalName!;

        foreach (var column in this.Columns.Where(c => c.Existing && !string.IsNullOrWhiteSpace(c.Name)))
        {
            var before = this.Original.FirstOrDefault(o => o.Key == column.Key);
            if (before is not null && !string.Equals(before.Name, column.Name.Trim(), StringComparison.Ordinal))
            {
                statements.Add(
                    $"ALTER TABLE {Quote(table)} RENAME COLUMN {Quote(before.Name)} TO {Quote(column.Name.Trim())};");
            }
        }

        foreach (var column in this.Columns.Where(c => !c.Existing && !string.IsNullOrWhiteSpace(c.Name)))
            statements.Add($"ALTER TABLE {Quote(table)} ADD COLUMN {Definition(column)};");

        foreach (var dropped in this.Original.Where(o => this.Columns.All(c => c.Key != o.Key)))
            statements.Add($"ALTER TABLE {Quote(table)} DROP COLUMN {Quote(dropped.Name)};");

        if (!string.Equals(table, this.Name.Trim(), StringComparison.Ordinal))
            statements.Add($"ALTER TABLE {Quote(table)} RENAME TO {Quote(this.Name.Trim())};");

        return statements.Count == 0
            ? "-- Nothing has changed."
            : string.Join("\n", statements);
    }

    /// <summary>
    /// Changes the design carries that ALTER TABLE cannot express, so the panel can say so rather
    /// than generating SQL that quietly does less than the form shows.
    /// </summary>
    public IReadOnlyList<string> Unsupported()
    {
        if (this.IsNew)
            return [];

        var unsupported = new List<string>();

        foreach (var column in this.Columns.Where(c => c.Existing))
        {
            var before = this.Original.FirstOrDefault(o => o.Key == column.Key);
            if (before is null)
                continue;

            if (!string.Equals(before.Type, column.Type, StringComparison.OrdinalIgnoreCase))
                unsupported.Add($"SQLite cannot change '{before.Name}' from {before.Type} to {column.Type}. Rebuild the table instead.");

            if (before.NotNull != column.NotNull)
                unsupported.Add($"SQLite cannot add or remove NOT NULL on '{before.Name}'. Rebuild the table instead.");

            if (before.PrimaryKey != column.PrimaryKey)
                unsupported.Add($"SQLite cannot change the primary key on '{before.Name}'. Rebuild the table instead.");
        }

        return unsupported;
    }

    private static string Definition(DesignColumn column)
    {
        var definition = new StringBuilder();
        definition.Append(Quote(column.Name.Trim())).Append(' ').Append(column.Type);

        if (column.PrimaryKey)
            definition.Append(" PRIMARY KEY");

        if (column.NotNull && !column.PrimaryKey)
            definition.Append(" NOT NULL");

        if (!string.IsNullOrWhiteSpace(column.DefaultValue))
            definition.Append(" DEFAULT ").Append(column.DefaultValue.Trim());

        return definition.ToString();
    }

    /// <summary>
    /// An identifier, quoted. The doubling is for the table genuinely called <c>my"table</c>; the
    /// statement is shown before it runs either way.
    /// </summary>
    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";
}

public sealed class DesignColumn
{
    /// <summary>
    /// Identity that survives a rename, so an edit can tell "renamed" from "dropped and added". A
    /// column and its pre-edit snapshot share one.
    /// </summary>
    public Guid Key { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = "";
    public string Type { get; set; } = "TEXT";
    public bool NotNull { get; set; }
    public bool PrimaryKey { get; set; }
    public string? DefaultValue { get; set; }

    /// <summary>Whether the table already has this column, which decides ADD versus rename.</summary>
    public bool Existing { get; set; }
}
