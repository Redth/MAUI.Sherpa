using FluentAssertions;
using MauiSherpa.Workloads.Services;

namespace MauiSherpa.Workloads.Tests.Services;

public class DotnetUpRuntimeIdentifierTests
{
    [Theory]
    [InlineData("win-x64", "dotnetup-win-x64.exe", "dotnetup.exe")]
    [InlineData("win-arm64", "dotnetup-win-arm64.exe", "dotnetup.exe")]
    [InlineData("osx-arm64", "dotnetup-osx-arm64", "dotnetup")]
    [InlineData("osx-x64", "dotnetup-osx-x64", "dotnetup")]
    [InlineData("linux-x64", "dotnetup-linux-x64", "dotnetup")]
    [InlineData("linux-musl-arm64", "dotnetup-linux-musl-arm64", "dotnetup")]
    public void FileNames_AreRidAndOsSpecific(string rid, string downloadName, string exeName)
    {
        DotnetUpRuntimeIdentifier.GetDownloadFileName(rid).Should().Be(downloadName);
        DotnetUpRuntimeIdentifier.GetExecutableFileName(rid).Should().Be(exeName);
    }

    [Fact]
    public void GetDownloadUrl_DefaultsToDailyQuality()
    {
        DotnetUpRuntimeIdentifier.GetDownloadUrl("osx-arm64")
            .Should().Be("https://aka.ms/dotnet/dotnetup/daily/dotnetup-osx-arm64");
    }

    [Fact]
    public void GetDownloadUrl_HonorsCustomQuality()
    {
        DotnetUpRuntimeIdentifier.GetDownloadUrl("win-x64", "ga")
            .Should().Be("https://aka.ms/dotnet/dotnetup/ga/dotnetup-win-x64.exe");
    }

    [Fact]
    public void GetChecksumUrl_AppendsSha512()
    {
        DotnetUpRuntimeIdentifier.GetChecksumUrl("osx-arm64")
            .Should().Be("https://aka.ms/dotnet/dotnetup/daily/dotnetup-osx-arm64.sha512");
    }

    [Theory]
    [InlineData("osx-arm64", true)]
    [InlineData("win-x64", true)]
    [InlineData("linux-musl-x64", true)]
    [InlineData("freebsd-x64", false)]
    [InlineData("osx-x86", false)]
    public void IsSupportedRid_MatchesPublishedList(string rid, bool expected)
    {
        DotnetUpRuntimeIdentifier.IsSupportedRid(rid).Should().Be(expected);
    }

    [Fact]
    public void DetectCurrent_ReturnsSupportedRid()
    {
        var rid = DotnetUpRuntimeIdentifier.DetectCurrent();

        DotnetUpRuntimeIdentifier.IsSupportedRid(rid).Should().BeTrue(
            because: $"detected RID '{rid}' should be one dotnetup publishes");
    }
}
