using System.Text.Json;
using System.Text.Json.Serialization;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Models.Profiling;

public enum MauiProfileMode
{
    Startup,
    Interaction
}

public enum MauiProfileOutputFormat
{
    NetTrace,
    Speedscope,
    Mibc
}

public enum MauiProfileRunState
{
    Idle,
    Starting,
    AwaitingRecording,
    Recording,
    Finalizing,
    Completed,
    Failed,
    Cancelled
}

public enum MauiCliToolState
{
    Missing,
    Available,
    UpdateRequired
}

public sealed record MauiCliToolStatus(
    MauiCliToolState State,
    string? ExecutablePath = null,
    string? Version = null,
    string? Message = null)
{
    public bool IsAvailable => State == MauiCliToolState.Available;
}

public sealed record MauiCliToolUpdateInfo(
    string? InstalledVersion = null,
    string? LatestVersion = null,
    bool UpdateAvailable = false,
    string? Message = null);

public sealed record MauiCliDevice
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("identifier")]
    public required string Identifier { get; init; }

    [JsonPropertyName("emulator_id")]
    public string? EmulatorId { get; init; }

    [JsonPropertyName("platforms")]
    public required string[] Platforms { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("version_name")]
    public string? VersionName { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("is_emulator")]
    public bool IsEmulator { get; init; }

    [JsonPropertyName("is_running")]
    public bool IsRunning { get; init; }

    [JsonPropertyName("connection_type")]
    public string? ConnectionType { get; init; }

    public string Platform => Platforms.FirstOrDefault() ?? string.Empty;
}

public sealed record MauiProfileRequest
{
    public required string ProjectPath { get; init; }
    public required ProfilingTargetPlatform Platform { get; init; }
    public required string DeviceId { get; init; }
    public string? DeviceName { get; init; }
    public bool IsEmulator { get; init; }
    public required MauiProfileMode Mode { get; init; }
    public MauiProfileOutputFormat Format { get; init; } = MauiProfileOutputFormat.Speedscope;
    public required string OutputPath { get; init; }
    public string Configuration { get; init; } = "Release";
    public TimeSpan? Duration { get; init; }
    public string? TraceProfile { get; init; }
    public bool NoBuild { get; init; }
}

public sealed record MauiProfileResult
{
    public required string ProjectPath { get; init; }
    public required string ProjectName { get; init; }
    public required string Framework { get; init; }
    public required string Platform { get; init; }
    public required string DeviceId { get; init; }
    public required string DeviceName { get; init; }
    public required string Configuration { get; init; }
    public required string Format { get; init; }
    public required string OutputPath { get; init; }
    public string? RawTracePath { get; init; }
    public string? DsrouterKind { get; init; }
    public string? DiagnosticAddress { get; init; }
    public int? DiagnosticPort { get; init; }
    public bool UsedStoppingEvent { get; init; }
    public DateTimeOffset? StartedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }

    /// <summary>
    /// True when Sherpa reconstructed this result from the artifacts on disk because the
    /// CLI captured a profile but failed to report it.
    /// </summary>
    public bool RecoveredFromDisk { get; init; }
}

public sealed record MauiCliRemediation(
    string Type,
    string? Command,
    IReadOnlyList<string> ManualSteps);

public abstract record MauiCliMessage;

public sealed record MauiCliStatusMessage(
    string Status,
    string Message,
    int? Percentage = null) : MauiCliMessage;

public sealed record MauiCliErrorMessage(
    string Code,
    string Category,
    string Severity,
    string Message,
    string? NativeError = null,
    MauiCliRemediation? Remediation = null,
    string? DocsUrl = null,
    string? CorrelationId = null,
    JsonElement? Context = null) : MauiCliMessage;

public sealed record MauiProfileResultMessage(MauiProfileResult Result) : MauiCliMessage;

public sealed record MauiCliDeviceListMessage(
    IReadOnlyList<MauiCliDevice> Devices) : MauiCliMessage;

public sealed record MauiCliVersionMessage(
    string Version,
    string? Runtime = null,
    string? OperatingSystem = null) : MauiCliMessage;

public sealed record MauiCliUnknownMessage(JsonElement Payload) : MauiCliMessage;

public sealed record MauiProfileExecutionResult(
    ProcessResult Process,
    MauiProfileResult? Profile,
    MauiCliErrorMessage? Error,
    IReadOnlyList<MauiCliStatusMessage> StatusMessages)
{
    // A profile is only present when a usable artifact exists, so it is a better success
    // signal than the CLI exit code, which preview builds set even after a good capture.
    public bool Success => Profile is not null && Error is null && !WasCancelled;
    public bool WasCancelled => Process.WasCancelled || Process.ExitCode == 130;
}

public sealed class MauiCliMessageEventArgs(MauiCliMessage message) : EventArgs
{
    public MauiCliMessage Message { get; } = message;
}

public sealed class MauiProfileStateChangedEventArgs(
    MauiProfileRunState oldState,
    MauiProfileRunState newState) : EventArgs
{
    public MauiProfileRunState OldState { get; } = oldState;
    public MauiProfileRunState NewState { get; } = newState;
}
