using System.Text.Json.Serialization;
using MauiSherpa.Core.Models.Inspector;

namespace MauiSherpa.Core.Services;

// ── request bodies ──
//
// Named types rather than anonymous ones so the source generator can see them: an anonymous object
// can only be serialized by reflecting over it, which is exactly what trimming and AOT remove.

internal sealed class SqlitePathBody
{
    public string Path { get; set; } = "";
}

internal sealed class SqliteQueryBody
{
    public string Path { get; set; } = "";
    public string Sql { get; set; } = "";
    public int? MaxRows { get; set; }
}

internal sealed class SqliteRowBody
{
    public string Path { get; set; } = "";
    public string Table { get; set; } = "";
    public long? RowId { get; set; }
    public InspectorDatabaseCell[]? Values { get; set; }
}

/// <summary>Claiming, keeping or giving up the right to change the app under test.</summary>
internal sealed class MutationLeaseBody
{
    public string Action { get; set; } = "status";
    public string? LeaseId { get; set; }
    public string? HolderKind { get; set; }
    public string? Label { get; set; }
}

internal sealed class MutationLeaseStatusBody
{
    public bool Ok { get; set; }
    public bool Allowed { get; set; }
    public bool YouHold { get; set; }
    public bool HeldByOther { get; set; }
    public string? LeaseId { get; set; }
    public string? HolderLabel { get; set; }
}

internal sealed class StorageRootsEnvelope
{
    public List<InspectorStorageRoot>? Roots { get; set; }
}

internal sealed class FileMoveBody
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public bool Overwrite { get; set; }
}

/// <summary>An empty body, for the routes that take one only because they are a PUT.</summary>
internal sealed class EmptyBody;

/// <summary>
/// The file-manager and database calls, serialized by the source generator rather than by
/// reflecting over the types at runtime - so these routes keep working under trimming and AOT.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MutationLeaseBody))]
[JsonSerializable(typeof(MutationLeaseStatusBody))]
[JsonSerializable(typeof(StorageRootsEnvelope))]
[JsonSerializable(typeof(InspectorFileListing))]
[JsonSerializable(typeof(InspectorDatabaseSchema))]
[JsonSerializable(typeof(InspectorDatabaseResult))]
[JsonSerializable(typeof(InspectorDatabaseRows))]
[JsonSerializable(typeof(SqlitePathBody))]
[JsonSerializable(typeof(SqliteQueryBody))]
[JsonSerializable(typeof(SqliteRowBody))]
[JsonSerializable(typeof(FileMoveBody))]
[JsonSerializable(typeof(EmptyBody))]
internal sealed partial class InspectorJsonContext : JsonSerializerContext;
