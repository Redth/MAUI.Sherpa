using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class MauiProfileArtifactRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"sherpa-recovery-{Guid.NewGuid():N}");

    public MauiProfileArtifactRecoveryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TryRecover_UsesMibcPrimaryAndNettraceCompanion()
    {
        WriteArtifact("capture.nettrace");
        WriteArtifact("capture.mibc");
        WriteArtifact("capture.etlx");

        var started = DateTimeOffset.UtcNow.AddMinutes(-2);
        var result = MauiProfileArtifactRecovery.TryRecover(
            CreateRequest(MauiProfileOutputFormat.Mibc),
            started,
            DateTimeOffset.UtcNow,
            "Building net10.0-android for emulator-5554");

        result.Should().NotBeNull();
        result!.RecoveredFromDisk.Should().BeTrue();
        Path.GetFileName(result.OutputPath).Should().Be("capture.mibc");
        Path.GetFileName(result.RawTracePath!).Should().Be("capture.nettrace");
        result.Format.Should().Be("mibc");
        result.Framework.Should().Be("net10.0-android");
        result.DeviceName.Should().Be("Pixel 9");
        result.ProjectName.Should().Be("MauiApp");
        result.UsedStoppingEvent.Should().BeTrue();
    }

    [Fact]
    public void TryRecover_PrefersSpeedscopeWhenRequested()
    {
        WriteArtifact("capture.nettrace");
        WriteArtifact("capture.speedscope.json");

        var result = MauiProfileArtifactRecovery.TryRecover(
            CreateRequest(MauiProfileOutputFormat.Speedscope),
            DateTimeOffset.UtcNow.AddMinutes(-2),
            DateTimeOffset.UtcNow);

        Path.GetFileName(result!.OutputPath).Should().Be("capture.speedscope.json");
        Path.GetFileName(result.RawTracePath!).Should().Be("capture.nettrace");
        result.Format.Should().Be("speedscope");
    }

    [Fact]
    public void TryRecover_FallsBackToRawTraceWhenConversionMissing()
    {
        WriteArtifact("capture.nettrace");

        var result = MauiProfileArtifactRecovery.TryRecover(
            CreateRequest(MauiProfileOutputFormat.Speedscope),
            DateTimeOffset.UtcNow.AddMinutes(-2),
            DateTimeOffset.UtcNow);

        Path.GetFileName(result!.OutputPath).Should().Be("capture.nettrace");
        result.RawTracePath.Should().BeNull();
        result.Format.Should().Be("nettrace");
    }

    [Fact]
    public void TryRecover_ReturnsNullWhenNoArtifactsExist()
    {
        var result = MauiProfileArtifactRecovery.TryRecover(
            CreateRequest(MauiProfileOutputFormat.Speedscope),
            DateTimeOffset.UtcNow.AddMinutes(-2),
            DateTimeOffset.UtcNow);

        result.Should().BeNull();
    }

    [Fact]
    public void TryRecover_IgnoresEmptyArtifacts()
    {
        File.WriteAllBytes(Path.Combine(_root, "capture.nettrace"), []);

        var result = MauiProfileArtifactRecovery.TryRecover(
            CreateRequest(MauiProfileOutputFormat.NetTrace),
            DateTimeOffset.UtcNow.AddMinutes(-2),
            DateTimeOffset.UtcNow);

        result.Should().BeNull();
    }

    [Fact]
    public void TryRecover_IgnoresArtifactsWrittenBeforeTheRun()
    {
        var path = WriteArtifact("capture.nettrace");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-3));

        var result = MauiProfileArtifactRecovery.TryRecover(
            CreateRequest(MauiProfileOutputFormat.NetTrace),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        result.Should().BeNull();
    }

    [Fact]
    public void TryRecover_LeavesFrameworkEmptyWhenOutputHasNoMoniker()
    {
        WriteArtifact("capture.nettrace");

        var result = MauiProfileArtifactRecovery.TryRecover(
            CreateRequest(MauiProfileOutputFormat.NetTrace),
            DateTimeOffset.UtcNow.AddMinutes(-2),
            DateTimeOffset.UtcNow,
            "no framework here");

        result!.Framework.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("Unhandled exception. System.IO.IOException: disk full", false)]
    [InlineData(
        "System.InvalidOperationException: JsonTypeInfo metadata for type " +
        "'Microsoft.Maui.Cli.Commands.MauiProfileResult' was not provided by TypeInfoResolver " +
        "of type 'Microsoft.Maui.Cli.Output.MauiCliJsonContext'.",
        true)]
    public void IsResultSerializationFailure_DetectsKnownCliDefect(string? output, bool expected)
    {
        MauiProfileArtifactRecovery.IsResultSerializationFailure(output).Should().Be(expected);
    }

    private string WriteArtifact(string fileName)
    {
        var path = Path.Combine(_root, fileName);
        File.WriteAllText(path, "artifact");
        return path;
    }

    private MauiProfileRequest CreateRequest(MauiProfileOutputFormat format) => new()
    {
        ProjectPath = "/repo/MauiApp.csproj",
        Platform = ProfilingTargetPlatform.Android,
        DeviceId = "emulator-5554",
        DeviceName = "Pixel 9",
        IsEmulator = true,
        Mode = MauiProfileMode.Startup,
        Format = format,
        OutputPath = Path.Combine(_root, "capture.nettrace")
    };
}
