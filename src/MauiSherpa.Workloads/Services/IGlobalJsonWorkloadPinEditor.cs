using MauiSherpa.Workloads.Models;

namespace MauiSherpa.Workloads.Services;

public interface IGlobalJsonWorkloadPinEditor
{
    GlobalJsonWorkloadPinPreview Preview(string projectFolder, string? workloadVersion);
    Task ApplyAsync(GlobalJsonWorkloadPinPreview preview, CancellationToken cancellationToken = default);
}
