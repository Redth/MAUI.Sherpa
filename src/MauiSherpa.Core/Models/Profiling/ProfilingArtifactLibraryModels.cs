namespace MauiSherpa.Core.Models.Profiling;

public record ProfilingArtifactLibraryEntry(
    ProfilingArtifactMetadata Metadata,
    bool IsManagedPath,
    string? SourcePath,
    DateTimeOffset AddedAt,
    DateTimeOffset UpdatedAt
);

public record ProfilingArtifactLibraryQuery(
    string? SessionId = null,
    ProfilingArtifactKind? Kind = null,
    bool IncludeMissing = true
);

public record ProfilingArtifactLibrarySaveRequest(
    ProfilingArtifactMetadata Metadata,
    string? ArtifactPath = null,
    bool CopyToLibrary = false
);
