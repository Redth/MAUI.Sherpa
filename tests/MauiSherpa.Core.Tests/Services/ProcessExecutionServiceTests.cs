using MauiSherpa.Core.Interfaces;
using MauiSherpa.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class ProcessExecutionServiceTests
{
    [Fact]
    public async Task CancelSendsSigintToLinuxProcess()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var platform = new Mock<IPlatformService>();
        platform.SetupGet(p => p.IsLinux).Returns(true);
        var service = new ProcessExecutionService(Mock.Of<ILoggingService>(), platform.Object);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.OutputReceived += (_, args) =>
        {
            if (args.Data == "ready")
                ready.TrySetResult();
        };
        var request = new ProcessRequest(
            "/bin/sh",
            ["-c", "trap 'echo interrupted; exit 0' INT; echo ready; while :; do sleep 0.1; done"]);
        var execution = service.ExecuteAsync(request);
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            service.Cancel();
            var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(result.WasCancelled);
            Assert.Contains("interrupted", result.Output);
        }
        finally
        {
            if (!execution.IsCompleted)
            {
                service.Kill();
                await execution.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }
}
