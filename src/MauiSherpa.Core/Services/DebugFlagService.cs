using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// In-memory debug flags for testing failure scenarios.
/// Singleton — flags reset automatically on app restart.
/// </summary>
public class DebugFlagService : IDebugFlagService
{
    public bool FailBuildToolsInstall { get; set; }
}
