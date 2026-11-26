using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Screenshots;
using MauiSherpa.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace MauiSherpa.Core.Tests;

public class ScreenshotModeTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("website-v2", true)]
    [InlineData("website-v1", false)]
    public void UnsupportedActivationRefusesRealDataFallback(string value, bool supported)
    {
        var activate = () => ScreenshotMode.Parse(value, supported);
        activate.Should().Throw<InvalidOperationException>().WithMessage("*Refusing to start with real data*");
    }

    [Fact]
    public void NormalLaunchLeavesRegistrationsUnchanged()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IAndroidSdkService>());
        var descriptor = services.Single();
        ScreenshotMode.Parse(null).RegisterServices(services);
        services.Should().ContainSingle().Which.Should().BeSameAs(descriptor);
    }

    private static (ScreenshotMode Mode, ServiceProvider Services) Create(bool mac = false, bool windows = false)
    {
        var platform = new Mock<IPlatformService>();
        platform.SetupGet(p => p.IsMacOS).Returns(mac);
        platform.SetupGet(p => p.IsWindows).Returns(windows);
        platform.SetupGet(p => p.IsLinux).Returns(!mac && !windows);
        var services = new ServiceCollection();
        services.AddSingleton(platform.Object);
        services.AddSingleton<IAndroidSdkService>(_ => throw new Exception("Production SDK service was instantiated"));
        services.AddSingleton<IAndroidSdkPathService>(_ => throw new Exception("Production PATH service was instantiated"));
        services.AddSingleton<EncryptedSettingsService>(_ => throw new Exception("Production settings were instantiated"));
        services.AddSingleton<IBackgroundTaskHandler>(_ => throw new Exception("Production worker was instantiated"));
        var mode = ScreenshotMode.Parse("website-v1", true);
        mode.RegisterServices(services);
        return (mode, services.BuildServiceProvider());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task InventoriesAreFictionalCoherentAndPlatformGated(bool mac, bool windows)
    {
        var (mode, services) = Create(mac, windows);
        using (services)
        {
            var settings = await services.GetRequiredService<IEncryptedSettingsService>().GetSettingsAsync();
            settings.Preferences.DemoMode.Should().BeFalse();
            settings.Preferences.AutoBackupEnabled.Should().BeFalse();
            var identity = services.GetRequiredService<IAppleIdentityStateService>().SelectedIdentity!;
            identity.Name.Should().Be("Summit Labs");
            settings.AppleIdentities.Should().Contain(i => i.Id == identity.Id);
            var apple = services.GetRequiredService<IAppleConnectService>();
            var bundles = await apple.GetBundleIdsAsync();
            var certificates = await apple.GetCertificatesAsync();
            var profiles = await apple.GetProfilesAsync();
            profiles.Should().OnlyContain(p => bundles.Any(b => b.Identifier == p.BundleId));
            profiles.SelectMany(p => p.CertificateIds!).Should().OnlyContain(id => certificates.Any(c => c.Id == id));
            var keystores = await services.GetRequiredService<IKeystoreService>().ListKeystoresAsync();
            keystores.Should().HaveCountGreaterThan(1).And.OnlyContain(k => k.StoredInVault);
            services.GetRequiredService<IXcodeService>().IsSupported.Should().Be(mac);
            services.GetRequiredService<ISimulatorService>().IsSupported.Should().Be(mac);
            services.GetRequiredService<IDeviceMonitorService>().Current.AndroidDevices.Should().NotBeEmpty();
            var doctor = await services.GetRequiredService<IDoctorService>().RunDoctorAsync();
            doctor.HasWarnings.Should().BeTrue();
            doctor.HasErrors.Should().BeFalse();
            doctor.Dependencies.Any(d => d.Category == DependencyCategory.Xcode).Should().Be(mac);
            doctor.Dependencies.Should().Contain(d => d.Name == "JDK" && d.InstalledVersion == "21.0.8");
            doctor.Dependencies.Should().Contain(d => d.Name == "Android Platform" && d.InstalledVersion == "36");
            services.GetServices<IBackgroundTaskHandler>().Should().ContainSingle();
            services.GetService<EncryptedSettingsService>().Should().BeNull();
            mode.Violations.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task FirebaseSelectionUsesOnlyKnownFixtureCredentialsInMemory()
    {
        var (mode, services) = Create();
        using (services)
        {
            var push = services.GetRequiredService<IFirebasePushService>();
            var identity = services.GetRequiredService<IGoogleIdentityStateService>().SelectedIdentity!;
            await push.ClearCredentialsAsync();
            (await push.HasCredentialsAsync()).Should().BeFalse();
            await push.SaveServiceAccountAsync(identity.ServiceAccountJson!);
            (await push.GetProjectIdAsync()).Should().Be(identity.ProjectId);
            mode.Violations.Should().BeEmpty();

            var import = () => push.SaveServiceAccountAsync("{\"private_key\":\"not-a-fixture\"}");
            await import.Should().ThrowAsync<InvalidOperationException>();
        }
    }

    [Fact]
    public async Task StateChangesStayInMemoryAndResetWithANewSession()
    {
        var (_, first) = Create();
        var (_, second) = Create();
        using (first)
        using (second)
        {
            await first.GetRequiredService<IEncryptedSettingsService>().UpdateSettingsAsync(
                s => s with { Preferences = s.Preferences with { Theme = "Light" } });
            (await first.GetRequiredService<IEncryptedSettingsService>().GetSettingsAsync()).Preferences.Theme.Should().Be("Light");
            (await second.GetRequiredService<IEncryptedSettingsService>().GetSettingsAsync()).Preferences.Theme.Should().Be("Dark");
            var state = first.GetRequiredService<IAppleIdentityStateService>();
            var changed = false;
            state.OnSelectionChanged += () => changed = true;
            state.SetSelectedIdentity((await first.GetRequiredService<IAppleIdentityService>().GetIdentitiesAsync())[1]);
            changed.Should().BeTrue();
            second.GetRequiredService<IAppleIdentityStateService>().SelectedIdentity!.Id.Should().Be("summit-apple");
        }
    }

    [Fact]
    public void ExternalActionsThrowAndRemainVisibleInDiagnostics()
    {
        var (mode, services) = Create();
        using (services)
        {
            Action install = () => services.GetRequiredService<IAndroidSdkService>().InstallPackageAsync("platform-tools");
            Action process = () => services.GetRequiredService<IProcessExecutionService>().ExecuteAsync(new ProcessRequest("dotnet", []));
            Action revoke = () => services.GetRequiredService<IAppleConnectService>().RevokeCertificateAsync("cert-development");
            Action inspect = () => services.GetRequiredService<IAppInspectorClientFactory>().CreateAsync("localhost", 9223);
            install.Should().Throw<InvalidOperationException>().WithMessage("*Screenshot Mode blocked*");
            process.Should().Throw<InvalidOperationException>().WithMessage("*Screenshot Mode blocked*");
            revoke.Should().Throw<InvalidOperationException>().WithMessage("*Screenshot Mode blocked*");
            inspect.Should().Throw<InvalidOperationException>().WithMessage("*Screenshot Mode blocked*");
            mode.Violations.Should().HaveCount(4);
        }
    }

    [Fact]
    public void AndroidPathConfigurationIsIsolatedAndBlocked()
    {
        var (mode, services) = Create(mac: true);
        using (services)
        {
            var paths = services.GetRequiredService<IAndroidSdkPathService>();
            paths.DefaultShell.Should().Be(AndroidPathShell.Zsh);
            paths.CanConfigureMachinePath.Should().BeFalse();
            var options = new AndroidPathOptions(AndroidPathShell.Zsh, AndroidPathScope.User);
            Action check = () => paths.CheckAsync(options);
            Action apply = () => paths.ApplyAsync(new AndroidPathPlan(
                "/sample/sdk", options, "/sample/.zshrc", [], [], "", "", "", false, false));
            check.Should().Throw<InvalidOperationException>().WithMessage("*IAndroidSdkPathService.CheckAsync*");
            apply.Should().Throw<InvalidOperationException>().WithMessage("*IAndroidSdkPathService.ApplyAsync*");
            mode.Violations.Should().HaveCount(2);
        }
    }

    [Fact]
    public void UncoveredDataContractsFailTheRegistrationAudit()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IAppInspectorClient>());
        var register = () => ScreenshotMode.Parse("website-v1", true).RegisterServices(services);
        register.Should().Throw<InvalidOperationException>().WithMessage("*uncovered service registrations: IAppInspectorClient*");
    }
}
