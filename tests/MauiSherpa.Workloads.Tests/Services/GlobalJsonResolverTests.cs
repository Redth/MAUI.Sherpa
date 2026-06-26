using FluentAssertions;
using MauiSherpa.Workloads.Models;
using MauiSherpa.Workloads.Services;

namespace MauiSherpa.Workloads.Tests.Services;

public class GlobalJsonResolverTests
{
    // ===== DeriveChannel: rollForward -> channel mapping =====

    [Theory]
    [InlineData("10.0.100", null, "10.0.1xx", false)]            // default = latestPatch
    [InlineData("10.0.100", "latestPatch", "10.0.1xx", false)]
    [InlineData("10.0.203", "latestPatch", "10.0.2xx", false)]   // band = hundreds digit of patch
    [InlineData("9.0.305", "latestPatch", "9.0.3xx", false)]
    [InlineData("10.0.100", "latestFeature", "10.0", false)]
    [InlineData("10.0.100", "latestMinor", "10", false)]
    [InlineData("10.0.100", "latestMajor", "latest", false)]
    public void DeriveChannel_RollingPolicies_MapToRollingChannel(
        string version, string? rollForward, string expectedChannel, bool expectedPinned)
    {
        var (channel, pinned) = GlobalJsonResolver.DeriveChannel(version, rollForward);

        channel.Should().Be(expectedChannel);
        pinned.Should().Be(expectedPinned);
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("patch")]
    [InlineData("feature")]
    [InlineData("minor")]
    [InlineData("major")]
    public void DeriveChannel_PinnedPolicies_ReturnExactVersionPinned(string rollForward)
    {
        var (channel, pinned) = GlobalJsonResolver.DeriveChannel("10.0.203", rollForward);

        channel.Should().Be("10.0.203");
        pinned.Should().BeTrue();
    }

    [Fact]
    public void DeriveChannel_IsCaseInsensitive()
    {
        GlobalJsonResolver.DeriveChannel("10.0.100", "LATESTFEATURE").Channel.Should().Be("10.0");
        GlobalJsonResolver.DeriveChannel("10.0.100", "Disable").IsPinned.Should().BeTrue();
    }

    // ===== Resolve: discovery + parsing =====

    [Fact]
    public void Resolve_NoGlobalJson_ReturnsNoGlobalJsonStatus()
    {
        using var temp = new TempDir();

        var result = new GlobalJsonResolver().Resolve(temp.Path);

        result.Status.Should().Be(GlobalJsonStatus.NoGlobalJson);
        result.GlobalJsonPath.Should().BeNull();
        result.Channel.Should().BeNull();
    }

    [Fact]
    public void Resolve_GlobalJsonWithoutSdkVersion_ReturnsNoSdkVersion()
    {
        using var temp = new TempDir();
        temp.WriteGlobalJson("""{ "sdk": { "rollForward": "latestFeature" } }""");

        var result = new GlobalJsonResolver().Resolve(temp.Path);

        result.Status.Should().Be(GlobalJsonStatus.NoSdkVersion);
        result.GlobalJsonPath.Should().NotBeNull();
        result.RollForward.Should().Be("latestFeature");
    }

    [Fact]
    public void Resolve_GlobalJsonWithVersion_DerivesChannelAndDefaultsRollForward()
    {
        using var temp = new TempDir();
        temp.WriteGlobalJson("""{ "sdk": { "version": "9.0.305" } }""");

        var result = new GlobalJsonResolver().Resolve(temp.Path);

        result.Status.Should().Be(GlobalJsonStatus.Resolved);
        result.RequestedVersion.Should().Be("9.0.305");
        result.RollForward.Should().Be("latestPatch"); // defaulted when omitted
        result.Channel.Should().Be("9.0.3xx");
        result.IsPinned.Should().BeFalse();
    }

    [Fact]
    public void Resolve_PinnedRollForward_MarksPinned()
    {
        using var temp = new TempDir();
        temp.WriteGlobalJson("""{ "sdk": { "version": "10.0.204", "rollForward": "disable" } }""");

        var result = new GlobalJsonResolver().Resolve(temp.Path);

        result.Channel.Should().Be("10.0.204");
        result.IsPinned.Should().BeTrue();
    }

    [Fact]
    public void Resolve_ParsesAllowPrerelease()
    {
        using var temp = new TempDir();
        temp.WriteGlobalJson("""{ "sdk": { "version": "10.0.100", "allowPrerelease": false } }""");

        var result = new GlobalJsonResolver().Resolve(temp.Path);

        result.AllowPrerelease.Should().BeFalse();
    }

    [Fact]
    public void Resolve_WalksUpToNearestGlobalJson()
    {
        using var temp = new TempDir();
        // global.json at the root of the temp dir
        temp.WriteGlobalJson("""{ "sdk": { "version": "8.0.400" } }""");
        var nested = Directory.CreateDirectory(Path.Combine(temp.Path, "src", "project"));

        var result = new GlobalJsonResolver().Resolve(nested.FullName);

        result.Status.Should().Be(GlobalJsonStatus.Resolved);
        result.GlobalJsonPath.Should().Be(Path.Combine(temp.Path, "global.json"));
        result.Channel.Should().Be("8.0.4xx");
    }

    [Fact]
    public void Resolve_NearestGlobalJsonWins()
    {
        using var temp = new TempDir();
        temp.WriteGlobalJson("""{ "sdk": { "version": "8.0.400" } }""");
        var nested = Directory.CreateDirectory(Path.Combine(temp.Path, "src"));
        File.WriteAllText(Path.Combine(nested.FullName, "global.json"),
            """{ "sdk": { "version": "9.0.100" } }""");

        var result = new GlobalJsonResolver().Resolve(nested.FullName);

        result.GlobalJsonPath.Should().Be(Path.Combine(nested.FullName, "global.json"));
        result.Channel.Should().Be("9.0.1xx");
    }

    [Fact]
    public void Resolve_MalformedGlobalJson_TreatedAsNoSdkVersion()
    {
        using var temp = new TempDir();
        temp.WriteGlobalJson("{ this is not valid json");

        var result = new GlobalJsonResolver().Resolve(temp.Path);

        result.Status.Should().Be(GlobalJsonStatus.NoSdkVersion);
        result.GlobalJsonPath.Should().NotBeNull();
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "gjr-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public void WriteGlobalJson(string content) =>
            File.WriteAllText(System.IO.Path.Combine(Path, "global.json"), content);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { /* best effort */ }
        }
    }
}
