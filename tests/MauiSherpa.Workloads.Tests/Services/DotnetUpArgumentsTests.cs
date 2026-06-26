using FluentAssertions;
using MauiSherpa.Workloads.Models;
using MauiSherpa.Workloads.Services;

namespace MauiSherpa.Workloads.Tests.Services;

public class DotnetUpArgumentsTests
{
    [Fact]
    public void List_UsesFormatJson_NotJsonFlag()
    {
        var args = DotnetUpArguments.List();

        args.Should().Equal("list", "--format", "Json");
        args.Should().NotContain("--json");
    }

    [Fact]
    public void List_NoVerify_AppendsFlag()
    {
        DotnetUpArguments.List(noVerify: true)
            .Should().Equal("list", "--format", "Json", "--no-verify");
    }

    [Fact]
    public void Info_IsInfoFlag()
    {
        DotnetUpArguments.Info().Should().Equal("--info");
    }

    [Fact]
    public void SdkInstall_TerminalMode_SetsDefaultInstallAndNoProgress()
    {
        var args = DotnetUpArguments.SdkInstall("lts", setDefaultInstall: true);

        args.Should().Equal("sdk", "install", "lts", "--set-default-install", "--no-progress");
    }

    [Fact]
    public void SdkInstall_NoChannel_OmitsChannelToken()
    {
        var args = DotnetUpArguments.SdkInstall(channel: null, setDefaultInstall: false);

        args.Should().Equal("sdk", "install", "--no-progress");
    }

    [Fact]
    public void SdkInstall_UpdateGlobalJson_AddsFlag()
    {
        var args = DotnetUpArguments.SdkInstall("9.0.3xx", setDefaultInstall: true, updateGlobalJson: true);

        args.Should().Equal(
            "sdk", "install", "9.0.3xx", "--set-default-install", "--update-global-json", "--no-progress");
    }

    [Fact]
    public void SdkUpdate_DefaultsToNoProgress()
    {
        DotnetUpArguments.SdkUpdate().Should().Equal("sdk", "update", "--no-progress");
    }

    [Fact]
    public void SdkUninstall_WithSource_AddsSourceFlag()
    {
        DotnetUpArguments.SdkUninstall("9.0.3xx", DotnetUpInstallSource.Explicit)
            .Should().Equal("sdk", "uninstall", "9.0.3xx", "--source", "Explicit");
    }

    [Fact]
    public void SdkUninstall_UnknownSource_OmitsSourceFlag()
    {
        DotnetUpArguments.SdkUninstall("9.0.3xx", DotnetUpInstallSource.Unknown)
            .Should().Equal("sdk", "uninstall", "9.0.3xx");
    }

    [Fact]
    public void SdkUninstall_EmptyChannel_Throws()
    {
        var act = () => DotnetUpArguments.SdkUninstall("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RuntimeInstall_WithSpec_AddsSpec()
    {
        DotnetUpArguments.RuntimeInstall("aspnetcore@9.0")
            .Should().Equal("runtime", "install", "aspnetcore@9.0", "--no-progress");
    }

    [Fact]
    public void RuntimeUpdate_DefaultsToNoProgress()
    {
        DotnetUpArguments.RuntimeUpdate().Should().Equal("runtime", "update", "--no-progress");
    }

    [Fact]
    public void RuntimeUninstall_AddsSpec()
    {
        DotnetUpArguments.RuntimeUninstall("runtime@9.0")
            .Should().Equal("runtime", "uninstall", "runtime@9.0");
    }

    [Fact]
    public void UpdateAll_DefaultsToNoProgress()
    {
        DotnetUpArguments.UpdateAll().Should().Equal("update", "--no-progress");
    }

    [Fact]
    public void PrintEnvScript_AddsShell()
    {
        DotnetUpArguments.PrintEnvScript("zsh")
            .Should().Equal("print-env-script", "--shell", "zsh");
    }
}
