namespace MauiSherpa.Workloads.Models;

public record DotnetSdkManagerSummary
{
    public IReadOnlyList<DotnetMajorMinorGroupSummary> InstalledGroups { get; init; } = [];
    public IReadOnlyList<DotnetTrackedSdkChannelSummary> TrackedSdkChannels { get; init; } = [];
    public IReadOnlyList<DotnetTrackedNonSdkSpecSummary> TrackedNonSdkSpecs { get; init; } = [];
    public DotnetUpdateAggregateSummary Updates { get; init; } = new();
    public DotnetProjectWorkloadMatchSummary? ProjectWorkloads { get; init; }
}

public record DotnetMajorMinorGroupSummary
{
    public required string MajorMinor { get; init; }
    public string DisplayName => $".NET {MajorMinor}";
    public string? NewestSdkVersion { get; init; }
    public required DotnetInstalledComponentCounts Counts { get; init; }
    public bool HasInvalidComponents { get; init; }
    public int WorkloadFeatureBandCount { get; init; }
    public IReadOnlyList<DotnetUpInstallation> Installations { get; init; } = [];
    public IReadOnlyList<DotnetWorkloadInventory> WorkloadInventories { get; init; } = [];
}

public record DotnetInstalledComponentCounts
{
    public int Total { get; init; }
    public int Sdk { get; init; }
    public int Runtime { get; init; }
    public int AspNetCore { get; init; }
    public int WindowsDesktop { get; init; }
    public int Unknown { get; init; }
}

public record DotnetTrackedSdkChannelSummary
{
    public required string Channel { get; init; }
    public DotnetUpInstallSource Source { get; init; }
    public string? GlobalJsonPath { get; init; }
    public required string InstallRoot { get; init; }
    public string? Architecture { get; init; }
    public bool IsPinned { get; init; }
}

public record DotnetTrackedNonSdkSpecSummary
{
    public required DotnetUpComponent Component { get; init; }
    public required string VersionOrChannel { get; init; }
    public DotnetUpInstallSource Source { get; init; }
    public string? GlobalJsonPath { get; init; }
    public required string InstallRoot { get; init; }
    public string? Architecture { get; init; }
    public bool IsPinned { get; init; }
}

public record DotnetUpdateAggregateSummary
{
    public bool IsChecked { get; init; }
    public int PreviewCount { get; init; }
    public int AvailableUpdateCount { get; init; }
    public int SdkUpdateCount { get; init; }
    public int RuntimeUpdateCount { get; init; }
    public int UnresolvedCount { get; init; }
    public bool HasUpdates => AvailableUpdateCount > 0;
    public bool HasSdkUpdates => SdkUpdateCount > 0;
    public bool HasRuntimeUpdates => RuntimeUpdateCount > 0;
    public bool HasUnresolvedNonPinnedChannels => UnresolvedCount > 0;
    public IReadOnlyList<DotnetUpdatePreview> Previews { get; init; } = [];
}

public record DotnetProjectWorkloadMatchSummary
{
    public required string InstalledVersion { get; init; }
    public required SdkFeatureBand FeatureBand { get; init; }
    public string? InstallRoot { get; init; }
    public string? Architecture { get; init; }
    public IReadOnlyList<DotnetWorkloadInventory> MatchingInventories { get; init; } = [];
    public DotnetWorkloadInventory? SelectedInventory { get; init; }
}
