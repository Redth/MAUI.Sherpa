namespace MauiSherpa.Workloads.Services;

public static class WorkloadRecommendationPolicy
{
    private static readonly string[] DesktopWorkloads =
        ["maui", "ios", "macos", "tvos", "maccatalyst", "android", "wasm-tools"];

    private static readonly string[] LinuxWorkloads =
        ["maui-android", "android", "wasm-tools"];

    public static IReadOnlyList<string> GetRecommendedWorkloadIds(bool isLinux) =>
        isLinux ? LinuxWorkloads : DesktopWorkloads;
}
