using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class MauiProfilingCliServiceTests
{
    [Fact]
    public async Task InteractionProfile_UsesNewlinesForBeginAndStop()
    {
        var process = new ControllableProcessExecutionService();
        var toolService = CreateToolService();
        using var service = new MauiProfilingCliService(
            process,
            toolService.Object,
            Mock.Of<ILoggingService>());

        var runTask = service.RunAsync(CreateRequest(MauiProfileMode.Interaction));

        service.State.Should().Be(MauiProfileRunState.AwaitingRecording);

        await service.BeginRecordingAsync();
        service.State.Should().Be(MauiProfileRunState.Recording);

        await service.StopRecordingAsync();
        service.State.Should().Be(MauiProfileRunState.Finalizing);
        process.Inputs.Should().Equal(string.Empty, string.Empty);

        process.Emit(ProfileResultJson);
        process.Complete(new ProcessResult(
            0,
            ProfileResultJson,
            string.Empty,
            TimeSpan.FromSeconds(1),
            ProcessState.Completed));

        var result = await runTask;

        result.Success.Should().BeTrue();
        result.Profile!.DeviceId.Should().Be("emulator-5554");
        service.State.Should().Be(MauiProfileRunState.Completed);
    }

    [Fact]
    public async Task RunAsync_MapsStructuredFailure()
    {
        var process = new ControllableProcessExecutionService();
        using var service = new MauiProfilingCliService(
            process,
            CreateToolService().Object,
            Mock.Of<ILoggingService>());

        var runTask = service.RunAsync(CreateRequest(MauiProfileMode.Startup));
        process.Emit("""
            {"code":"E2111","category":"platform","severity":"error","message":"No running Android device."}
            """);
        process.Complete(new ProcessResult(
            1,
            string.Empty,
            string.Empty,
            TimeSpan.Zero,
            ProcessState.Failed));

        var result = await runTask;

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("E2111");
        service.State.Should().Be(MauiProfileRunState.Failed);
    }

    [Fact]
    public async Task Cancel_UsesAbortPath()
    {
        var process = new ControllableProcessExecutionService();
        using var service = new MauiProfilingCliService(
            process,
            CreateToolService().Object,
            Mock.Of<ILoggingService>());

        var runTask = service.RunAsync(CreateRequest(MauiProfileMode.Startup));
        service.Cancel();

        var result = await runTask;

        process.CancelCalled.Should().BeTrue();
        result.WasCancelled.Should().BeTrue();
        service.State.Should().Be(MauiProfileRunState.Cancelled);
    }

    [Fact]
    public async Task RunAsync_RecoversProfileWhenCliFailsAfterWritingArtifacts()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"sherpa-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var outputPath = Path.Combine(directory, "capture.nettrace");
            File.WriteAllText(outputPath, "trace");
            File.WriteAllText(Path.Combine(directory, "capture.mibc"), "mibc");
            File.WriteAllText(Path.Combine(directory, "capture.etlx"), "etlx");

            var process = new ControllableProcessExecutionService();
            using var service = new MauiProfilingCliService(
                process,
                CreateToolService().Object,
                Mock.Of<ILoggingService>());

            var request = CreateRequest(MauiProfileMode.Startup) with
            {
                Format = MauiProfileOutputFormat.Mibc,
                OutputPath = outputPath
            };

            var runTask = service.RunAsync(request);
            process.Emit(CliSerializationCrashEnvelope);
            process.Complete(new ProcessResult(
                1,
                CliSerializationCrashEnvelope,
                string.Empty,
                TimeSpan.FromSeconds(30),
                ProcessState.Failed));

            var result = await runTask;

            result.Success.Should().BeTrue();
            result.Error.Should().BeNull();
            result.Profile!.RecoveredFromDisk.Should().BeTrue();
            Path.GetFileName(result.Profile.OutputPath).Should().Be("capture.mibc");
            Path.GetFileName(result.Profile.RawTracePath!).Should().Be("capture.nettrace");
            service.State.Should().Be(MauiProfileRunState.Completed);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ReportsSerializationDefectWhenNothingWasCaptured()
    {
        var process = new ControllableProcessExecutionService();
        using var service = new MauiProfilingCliService(
            process,
            CreateToolService().Object,
            Mock.Of<ILoggingService>());

        var runTask = service.RunAsync(CreateRequest(MauiProfileMode.Startup));
        process.Emit(CliSerializationCrashEnvelope);
        process.Complete(new ProcessResult(
            1,
            CliSerializationCrashEnvelope,
            string.Empty,
            TimeSpan.Zero,
            ProcessState.Failed));

        var result = await runTask;

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("SHERPA_PROFILE_CLI_RESULT_SERIALIZATION");
        result.Error.Remediation!.Command.Should().Be("dotnet tool update -g Microsoft.Maui.Cli");
        service.State.Should().Be(MauiProfileRunState.Failed);
    }

    [Fact]
    public async Task RunAsync_KeepsUnrelatedCliErrorsEvenWhenArtifactsExist()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"sherpa-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var outputPath = Path.Combine(directory, "capture.nettrace");
            File.WriteAllText(outputPath, "partial trace");

            var process = new ControllableProcessExecutionService();
            using var service = new MauiProfilingCliService(
                process,
                CreateToolService().Object,
                Mock.Of<ILoggingService>());

            var runTask = service.RunAsync(
                CreateRequest(MauiProfileMode.Startup) with { OutputPath = outputPath });
            process.Emit("""
                {"code":"E2111","category":"platform","severity":"error","message":"No running Android device."}
                """);
            process.Complete(new ProcessResult(
                1,
                string.Empty,
                string.Empty,
                TimeSpan.Zero,
                ProcessState.Failed));

            var result = await runTask;

            result.Success.Should().BeFalse();
            result.Profile.Should().BeNull();
            result.Error!.Code.Should().Be("E2111");
            service.State.Should().Be(MauiProfileRunState.Failed);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_DoesNotRecoverAfterCancellation()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"sherpa-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var outputPath = Path.Combine(directory, "capture.nettrace");
            File.WriteAllText(outputPath, "partial trace");

            var process = new ControllableProcessExecutionService();
            using var service = new MauiProfilingCliService(
                process,
                CreateToolService().Object,
                Mock.Of<ILoggingService>());

            var runTask = service.RunAsync(
                CreateRequest(MauiProfileMode.Startup) with { OutputPath = outputPath });
            service.Cancel();

            var result = await runTask;

            result.WasCancelled.Should().BeTrue();
            result.Profile.Should().BeNull();
            service.State.Should().Be(MauiProfileRunState.Cancelled);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Mock<IMauiCliToolService> CreateToolService()
    {
        var mock = new Mock<IMauiCliToolService>();
        mock.Setup(x => x.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MauiCliToolStatus(
                MauiCliToolState.Available,
                "/fake/maui",
                "1.2.3"));
        return mock;
    }

    private static MauiProfileRequest CreateRequest(MauiProfileMode mode) => new()
    {
        ProjectPath = "/repo/App.csproj",
        Platform = ProfilingTargetPlatform.Android,
        DeviceId = "emulator-5554",
        Mode = mode,
        OutputPath = "/tmp/capture.nettrace"
    };

    // Verbatim stdout from Microsoft.Maui.Cli 0.1.0-preview.12: the CLI writes the trace,
    // then reports its own result-serialization defect as a normal error envelope.
    private const string CliSerializationCrashEnvelope = """
        {
          "code": "E1001",
          "category": "tool",
          "severity": "error",
          "message": "JsonTypeInfo metadata for type 'Microsoft.Maui.Cli.Commands.MauiProfileResult' was not provided by TypeInfoResolver of type 'Microsoft.Maui.Cli.Output.MauiCliJsonContext'. If using source generation, ensure that all root types passed to the serializer have been annotated with 'JsonSerializableAttribute', along with any types that might be serialized polymorphically."
        }
        """;

    private const string ProfileResultJson = """
        {
          "project_path":"/repo/App.csproj",
          "project_name":"App",
          "framework":"net10.0-android",
          "platform":"android",
          "device_id":"emulator-5554",
          "device_name":"Pixel",
          "configuration":"Release",
          "format":"speedscope",
          "output_path":"/tmp/capture.speedscope.json",
          "raw_trace_path":"/tmp/capture.nettrace",
          "used_stopping_event":false
        }
        """;

    private sealed class ControllableProcessExecutionService : IProcessExecutionService
    {
        private readonly TaskCompletionSource<ProcessResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> Inputs { get; } = [];
        public bool CancelCalled { get; private set; }
        public ProcessRequest? Request { get; private set; }
        public ProcessState CurrentState { get; private set; } = ProcessState.Pending;
        public int? ProcessId => 123;
        public event EventHandler<ProcessOutputEventArgs>? OutputReceived;
        public event EventHandler<ProcessStateChangedEventArgs>? StateChanged;

        public Task<ProcessResult> ExecuteAsync(
            ProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            CurrentState = ProcessState.Running;
            return _completion.Task;
        }

        public Task WriteInputAsync(
            string input,
            bool appendNewLine = true,
            CancellationToken cancellationToken = default)
        {
            Inputs.Add(input);
            return Task.CompletedTask;
        }

        public void Emit(string output) =>
            OutputReceived?.Invoke(this, new ProcessOutputEventArgs(output));

        public void Complete(ProcessResult result)
        {
            CurrentState = result.FinalState;
            _completion.TrySetResult(result);
        }

        public void Cancel()
        {
            CancelCalled = true;
            Complete(new ProcessResult(
                130,
                string.Empty,
                string.Empty,
                TimeSpan.Zero,
                ProcessState.Cancelled));
        }

        public void Kill()
        {
        }

        public string GetFullOutput() => string.Empty;
    }
}
