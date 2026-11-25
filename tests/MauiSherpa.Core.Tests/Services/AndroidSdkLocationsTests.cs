using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class AndroidSdkLocationsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"sherpa-sdk-tests-{Guid.NewGuid():N}");

    public AndroidSdkLocationsTests() => Directory.CreateDirectory(root);

    private string CreateSdk(string name)
    {
        var path = Path.Combine(root, name);
        var bin = Path.Combine(path, "cmdline-tools", "latest", "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, OperatingSystem.IsWindows() ? "sdkmanager.bat" : "sdkmanager"), "");
        return path;
    }

    [Fact]
    public void Inspect_DistinguishesSdkEmptyIncompleteAndUnrelatedFolders()
    {
        AndroidSdkLocations.Inspect(CreateSdk("sdk")).Status.Should().Be(AndroidSdkLocationStatus.Installed);
        AndroidSdkLocations.Inspect(root).Status.Should().Be(AndroidSdkLocationStatus.Unrelated);
        var empty = Path.Combine(root, "empty");
        Directory.CreateDirectory(empty);
        AndroidSdkLocations.Inspect(empty).Status.Should().Be(AndroidSdkLocationStatus.NotInstalled);
        Directory.CreateDirectory(Path.Combine(empty, "platform-tools"));
        AndroidSdkLocations.Inspect(empty).Status.Should().Be(AndroidSdkLocationStatus.Incomplete);
        File.WriteAllText(Path.Combine(empty, "notes.txt"), "");
        AndroidSdkLocations.Inspect(empty).Status.Should().Be(AndroidSdkLocationStatus.Unrelated);
    }

    [Fact]
    public void InstallTarget_RejectsExistingSdkUnrelatedFolderAndRelativePath()
    {
        var sdk = CreateSdk("sdk");
        var existing = () => AndroidSdkLocations.ValidateInstallTarget(sdk);
        existing.Should().Throw<InvalidOperationException>().WithMessage("*already exists*");
        var unrelated = () => AndroidSdkLocations.ValidateInstallTarget(root);
        unrelated.Should().Throw<InvalidOperationException>().WithMessage("*unrelated*");
        var relative = () => AndroidSdkLocations.Normalize("relative-sdk");
        relative.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task InvalidActivation_DoesNotPersistOrReplacePreviousSdk()
    {
        var sdk = new Mock<IAndroidSdkService>();
        var storage = new Mock<ISecureStorageService>();
        sdk.SetupGet(s => s.SdkPath).Returns("/previous/sdk");
        var settings = new AndroidSdkSettingsService(sdk.Object, storage.Object, Mock.Of<ILoggingService>());
        var activate = () => settings.SetCustomSdkPathAsync(root);
        await activate.Should().ThrowAsync<InvalidOperationException>();
        storage.Verify(s => s.SetAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        sdk.Verify(s => s.SetSdkPathAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task PersistenceFailure_DoesNotActivateNewSdk()
    {
        var path = CreateSdk("new-sdk");
        var sdk = new Mock<IAndroidSdkService>();
        var storage = new Mock<ISecureStorageService>();
        storage.Setup(s => s.SetAsync("android_sdk_custom_path", path)).ThrowsAsync(new IOException("Storage unavailable"));
        var settings = new AndroidSdkSettingsService(sdk.Object, storage.Object, Mock.Of<ILoggingService>());
        await settings.Invoking(s => s.SetCustomSdkPathAsync(path)).Should().ThrowAsync<IOException>();
        sdk.Verify(s => s.SetSdkPathAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task BusySdk_BlocksSwitching()
    {
        var sdk = new Mock<IAndroidSdkService>();
        sdk.SetupGet(s => s.IsBusy).Returns(true);
        var settings = new AndroidSdkSettingsService(sdk.Object, Mock.Of<ISecureStorageService>(), Mock.Of<ILoggingService>());
        await settings.Invoking(s => s.SetCustomSdkPathAsync(CreateSdk("sdk"))).Should().ThrowAsync<InvalidOperationException>().WithMessage("*finish*");
        sdk.Verify(s => s.SetSdkPathAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SuccessfulSelection_PersistsAndRestoresAfterRestart()
    {
        var path = CreateSdk("selected");
        var values = new Dictionary<string, string>();
        var storage = new Mock<ISecureStorageService>();
        storage.Setup(s => s.GetAsync(It.IsAny<string>())).ReturnsAsync((string key) => values.GetValueOrDefault(key));
        storage.Setup(s => s.SetAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((key, value) => values[key] = value).Returns(Task.CompletedTask);
        var sdk = new Mock<IAndroidSdkService>();
        sdk.Setup(s => s.SetSdkPathAsync(path)).ReturnsAsync(true);
        sdk.SetupGet(s => s.SdkPath).Returns(path);
        var settings = new AndroidSdkSettingsService(sdk.Object, storage.Object, Mock.Of<ILoggingService>());
        var changed = 0;
        settings.SdkPathChanged += () => changed++;
        await settings.SetCustomSdkPathAsync(path);
        changed.Should().Be(1);
        values["android_sdk_custom_path"].Should().Be(path);

        var restarted = new AndroidSdkSettingsService(sdk.Object, storage.Object, Mock.Of<ILoggingService>());
        await restarted.InitializeAsync();
        restarted.CustomSdkPath.Should().Be(path);
        sdk.Verify(s => s.SetSdkPathAsync(path), Times.Exactly(2));
    }

    [Fact]
    public async Task FailedActivation_RestoresPersistedSelection()
    {
        var oldPath = CreateSdk("old");
        var newPath = CreateSdk("new");
        var sdk = new Mock<IAndroidSdkService>();
        var storage = new Mock<ISecureStorageService>();
        storage.Setup(s => s.GetAsync("android_sdk_custom_path")).ReturnsAsync(oldPath);
        sdk.Setup(s => s.SetSdkPathAsync(newPath)).ReturnsAsync(false);
        var settings = new AndroidSdkSettingsService(sdk.Object, storage.Object, Mock.Of<ILoggingService>());
        await settings.Invoking(s => s.SetCustomSdkPathAsync(newPath)).Should().ThrowAsync<InvalidOperationException>();
        storage.Verify(s => s.SetAsync("android_sdk_custom_path", oldPath), Times.Once);
    }

    [Fact]
    public async Task FailedInstallation_DoesNotReplaceActiveManager()
    {
        var service = new AndroidSdkService(Mock.Of<ILoggingService>());
        var path = CreateSdk("active");
        (await service.SetSdkPathAsync(path)).Should().BeTrue();
        var previous = service.SdkPath;
        (await service.InstallSdkAsync(root)).Should().BeFalse();
        service.SdkPath.Should().Be(previous);
        service.IsBusy.Should().BeFalse();
        (await service.SetSdkPathAsync(Path.Combine(root, "missing"))).Should().BeFalse();
        service.SdkPath.Should().Be(previous);
    }

    [Fact]
    public async Task KnownLocations_IncludeAndDeduplicateRememberedSdk()
    {
        var path = CreateSdk("custom");
        var storage = new Mock<ISecureStorageService>();
        storage.Setup(s => s.GetAsync("android_sdk_custom_path")).ReturnsAsync(path);
        storage.Setup(s => s.GetAsync("android_sdk_known_paths")).ReturnsAsync(System.Text.Json.JsonSerializer.Serialize(new[] { path, path }));
        var sdk = new Mock<IAndroidSdkService>();
        sdk.SetupGet(s => s.SdkPath).Returns(path);
        var settings = new AndroidSdkSettingsService(sdk.Object, storage.Object, Mock.Of<ILoggingService>());
        var locations = await settings.GetKnownLocationsAsync();
        locations.Where(l => l.Path == path).Should().ContainSingle().Which.IsActive.Should().BeTrue();
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
