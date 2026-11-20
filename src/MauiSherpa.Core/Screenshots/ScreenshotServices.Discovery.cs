using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.DevFlow;

namespace MauiSherpa.Core.Screenshots;

internal sealed partial class ScreenshotServices : IDevFlowDiscoveryService, IAppInspectorClientFactory
{
    Task<IAppInspectorClient> IAppInspectorClientFactory.CreateAsync(string host, int port, CancellationToken ct) =>
        throw mode.Blocked("creating a live app inspector client");
    Task<DevFlowDiscoveryResult> IDevFlowDiscoveryService.DiscoverAsync(string host, int brokerPort) =>
        Read(new DevFlowDiscoveryResult(true,
        [
            new() { Id = "screenshot-android", AppName = "Trail Notes", Platform = "Android", Tfm = "net10.0-android",
                Project = "/projects/TrailNotes/TrailNotes.csproj", Port = 9223, Version = "0.9.0", ConnectedAt = new(ScreenshotFixtures.Now.AddMinutes(-12)) },
            new() { Id = "screenshot-ios", AppName = "Trail Notes", Platform = "iOS", Tfm = "net10.0-ios",
                Project = "/projects/TrailNotes/TrailNotes.csproj", Port = 9224, Version = "0.9.0", ConnectedAt = new(ScreenshotFixtures.Now.AddMinutes(-7)) },
            new() { Id = "screenshot-desktop", AppName = "Summit Compass", Platform = platform.IsWindows ? "Windows" : IsAppleHost ? "macOS" : "Linux",
                Project = "/projects/SummitCompass/SummitCompass.csproj", Port = 9225, Version = "0.9.0", ConnectedAt = new(ScreenshotFixtures.Now.AddMinutes(-3)) }
        ]));
    Task<DevFlowAgentStatus?> IDevFlowDiscoveryService.GetStatusAsync(string host, int agentPort) => throw mode.Blocked("inspector connection");
}
