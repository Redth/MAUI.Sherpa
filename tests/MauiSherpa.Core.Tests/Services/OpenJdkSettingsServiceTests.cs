using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

[Collection("OpenJdk environment")]
public class OpenJdkSettingsServiceTests : IDisposable
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "test-artifacts", $"jdk-settings-{Guid.NewGuid():N}");
    private readonly string? javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
    private readonly string? jdkHome = Environment.GetEnvironmentVariable("JDK_HOME");
    private readonly Mock<ISecureStorageService> storage = new();
    private readonly OpenJdkSettingsService service;

    public OpenJdkSettingsServiceTests()
    {
        Directory.CreateDirectory(root);
        service = new OpenJdkSettingsService(storage.Object, Mock.Of<ILoggingService>());
    }

    private string CreateHome(string name)
    {
        var home = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(home, "bin"));
        foreach (var tool in new[] { "java", "javac" })
            File.WriteAllText(Path.Combine(home, "bin", tool + (OperatingSystem.IsWindows() ? ".exe" : "")), "");
        return home;
    }

    [Fact]
    public async Task Selection_PersistsBeforeChangingProcessEnvironment()
    {
        var home = CreateHome("selected");
        await service.InitializeAsync();
        var previousJavaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        storage.Setup(s => s.SetAsync("openjdk_custom_path", home)).ThrowsAsync(new IOException("Storage unavailable"));
        await service.Invoking(s => s.SetCustomJdkPathAsync(home)).Should().ThrowAsync<IOException>();
        Environment.GetEnvironmentVariable("JAVA_HOME").Should().Be(previousJavaHome);
        service.CustomJdkPath.Should().BeNull();
        storage.Setup(s => s.SetAsync("openjdk_custom_path", home)).Returns(Task.CompletedTask);
        await service.SetCustomJdkPathAsync(home);
        Environment.GetEnvironmentVariable("JAVA_HOME").Should().Be(home);
        Environment.GetEnvironmentVariable("JDK_HOME").Should().Be(home);
        (await service.GetEffectiveJdkPathAsync()).Should().Be(home);
    }

    [Fact]
    public async Task Selection_RejectsInvalidHomeAndMovedSavedHomeIsNotReady()
    {
        var home = CreateHome("saved");
        storage.Setup(s => s.GetAsync("openjdk_custom_path")).ReturnsAsync(home);
        await service.InitializeAsync();
        File.Delete(Path.Combine(home, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java"));
        (await service.GetEffectiveJdkPathAsync()).Should().BeNull();
        await service.Invoking(s => s.SetCustomJdkPathAsync(root)).Should().ThrowAsync<ArgumentException>();
        service.CustomJdkPath.Should().Be(home);
        storage.Verify(s => s.SetAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task MissingJdk_BlocksSdkInstallBeforeCreatingDestination()
    {
        var missing = new Mock<IOpenJdkSettingsService>();
        var sdk = new AndroidSdkService(Mock.Of<ILoggingService>(), missing.Object);
        var target = Path.Combine(root, "sdk");
        var messages = new List<string>();
        (await sdk.InstallSdkAsync(target, new InlineProgress(messages))).Should().BeFalse();
        Directory.Exists(target).Should().BeFalse();
        messages.Should().Contain(m => m.Contains("Install or select OpenJDK"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("JAVA_HOME", javaHome);
        Environment.SetEnvironmentVariable("JDK_HOME", jdkHome);
        Directory.Delete(root, true);
    }

    private sealed class InlineProgress(List<string> messages) : IProgress<string>
    {
        public void Report(string value) => messages.Add(value);
    }
}

[CollectionDefinition("OpenJdk environment", DisableParallelization = true)]
public sealed class OpenJdkEnvironmentCollection { }
