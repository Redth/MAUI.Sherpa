using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class AndroidSdkPackageVersioningTests
{
    private static SdkPackageInfo Pkg(string path, string? version, bool installed = false) =>
        new(path, Description: path, Version: version, Location: installed ? "/sdk/" + path : null, IsInstalled: installed);

    // ---- GetGroup ----

    [Theory]
    [InlineData("emulator", "emulator")]
    [InlineData("platform-tools", "platform-tools")]
    [InlineData("build-tools;36.1.0", "build-tools")]
    [InlineData("system-images;android-33;google_apis;arm64-v8a", "system-images")]
    [InlineData("cmdline-tools;latest", "cmdline-tools")]
    [InlineData("", "")]
    public void GetGroup_ReturnsLeadingSegment(string path, string expected)
    {
        AndroidSdkPackageVersioning.GetGroup(path).Should().Be(expected);
    }

    // ---- Classification ----

    [Theory]
    [InlineData("emulator")]
    [InlineData("platform-tools")]
    [InlineData("tools")]
    [InlineData("docs")]
    [InlineData("ndk-bundle")]
    [InlineData("extras;google;m2repository")]
    [InlineData("build;templates")]
    [InlineData("patcher;v4")]
    [InlineData("cmdline-tools;latest")]
    public void Classify_FixedPathGroups(string path)
    {
        AndroidSdkPackageVersioning.Classify(path)
            .Should().Be(SdkPackageVersioningStrategy.FixedPath);
    }

    [Theory]
    [InlineData("platforms;android-33")]
    [InlineData("sources;android-36")]
    [InlineData("system-images;android-33;google_apis;arm64-v8a")]
    [InlineData("skiaparser;3")]
    [InlineData("add-ons;addon-google_apis-google-24")]
    [InlineData("some-unknown-future-group;variant")]
    public void Classify_RevisionGroups(string path)
    {
        AndroidSdkPackageVersioning.Classify(path)
            .Should().Be(SdkPackageVersioningStrategy.Revision);
    }

    [Theory]
    [InlineData("build-tools;36.1.0")]
    [InlineData("build-tools;37.0.0")]
    [InlineData("ndk;27.0.12077973")]
    [InlineData("cmake;3.22.1")]
    [InlineData("cmdline-tools;13.0")]
    public void Classify_SideBySideGroups(string path)
    {
        AndroidSdkPackageVersioning.Classify(path)
            .Should().Be(SdkPackageVersioningStrategy.SideBySide);
    }

    [Fact]
    public void Classify_CmdlineToolsLatestVsNumbered()
    {
        AndroidSdkPackageVersioning.Classify("cmdline-tools;latest")
            .Should().Be(SdkPackageVersioningStrategy.FixedPath);
        AndroidSdkPackageVersioning.Classify("cmdline-tools;13.0")
            .Should().Be(SdkPackageVersioningStrategy.SideBySide);
    }

    [Theory]
    [InlineData("emulator", true)]
    [InlineData("platforms;android-33", true)]
    [InlineData("build-tools;36.1.0", false)]
    [InlineData("ndk;27.0.12077973", false)]
    [InlineData("cmake;3.22.1", false)]
    public void SupportsInPlaceUpdate(string path, bool expected)
    {
        AndroidSdkPackageVersioning.SupportsInPlaceUpdate(path).Should().Be(expected);
    }

    // ---- GetInPlaceUpdate / HasInPlaceUpdate (positives) ----

    [Fact]
    public void GetInPlaceUpdate_Emulator_FindsNewerVersion()
    {
        var installed = Pkg("emulator", "36.4.10", installed: true);
        var available = new[]
        {
            Pkg("emulator", "36.4.10"),
            Pkg("emulator", "36.6.11"),
        };

        var update = AndroidSdkPackageVersioning.GetInPlaceUpdate(installed, available);

        update.Should().NotBeNull();
        update!.Version.Should().Be("36.6.11");
    }

    [Fact]
    public void GetInPlaceUpdate_PlatformTools_FindsNewerVersion()
    {
        var installed = Pkg("platform-tools", "36.0.0", installed: true);
        var available = new[] { Pkg("platform-tools", "37.0.0") };

        AndroidSdkPackageVersioning.HasInPlaceUpdate(installed, available).Should().BeTrue();
    }

    [Fact]
    public void GetInPlaceUpdate_Platforms_RevisionBump()
    {
        var installed = Pkg("platforms;android-33", "3", installed: true);
        var available = new[] { Pkg("platforms;android-33", "4") };

        var update = AndroidSdkPackageVersioning.GetInPlaceUpdate(installed, available);

        update.Should().NotBeNull();
        update!.Version.Should().Be("4");
    }

    [Fact]
    public void GetInPlaceUpdate_SystemImages_RevisionBump()
    {
        var installed = Pkg("system-images;android-33;google_apis;arm64-v8a", "17", installed: true);
        var available = new[] { Pkg("system-images;android-33;google_apis;arm64-v8a", "18") };

        AndroidSdkPackageVersioning.HasInPlaceUpdate(installed, available).Should().BeTrue();
    }

    [Fact]
    public void GetInPlaceUpdate_PicksHighestStableWhenMultipleNewer()
    {
        var installed = Pkg("emulator", "36.0.0", installed: true);
        var available = new[]
        {
            Pkg("emulator", "36.4.10"),
            Pkg("emulator", "36.6.11"),
            Pkg("emulator", "36.2.0"),
        };

        var update = AndroidSdkPackageVersioning.GetInPlaceUpdate(installed, available);

        update!.Version.Should().Be("36.6.11");
    }

    // ---- GetInPlaceUpdate (negatives) ----

    [Fact]
    public void GetInPlaceUpdate_SideBySide_NeverUpdates()
    {
        // build-tools;36.1.0 installed; build-tools;37.0.0 is a DISTINCT package, not an update.
        var installed = Pkg("build-tools;36.1.0", "36.1.0", installed: true);
        var available = new[]
        {
            Pkg("build-tools;36.1.0", "36.1.0"),
            Pkg("build-tools;37.0.0", "37.0.0"),
        };

        AndroidSdkPackageVersioning.GetInPlaceUpdate(installed, available).Should().BeNull();
    }

    [Fact]
    public void GetInPlaceUpdate_EqualVersion_NoUpdate()
    {
        var installed = Pkg("emulator", "36.6.11", installed: true);
        var available = new[] { Pkg("emulator", "36.6.11") };

        AndroidSdkPackageVersioning.GetInPlaceUpdate(installed, available).Should().BeNull();
    }

    [Fact]
    public void GetInPlaceUpdate_OlderAvailable_NoUpdate()
    {
        var installed = Pkg("emulator", "36.6.11", installed: true);
        var available = new[] { Pkg("emulator", "36.4.10") };

        AndroidSdkPackageVersioning.GetInPlaceUpdate(installed, available).Should().BeNull();
    }

    [Fact]
    public void GetInPlaceUpdate_PrereleaseFilteredByDefault()
    {
        var installed = Pkg("emulator", "36.4.10", installed: true);
        var available = new[] { Pkg("emulator", "36.6.11-rc1") };

        // Default requireStable: the rc build is ignored.
        AndroidSdkPackageVersioning.GetInPlaceUpdate(installed, available).Should().BeNull();

        // When stability isn't required, it is returned.
        AndroidSdkPackageVersioning.GetInPlaceUpdate(installed, available, requireStable: false)
            .Should().NotBeNull();
    }

    [Fact]
    public void GetInPlaceUpdate_NewApiLevel_NotAnUpdate()
    {
        // A brand-new platform (different Path) is not an update to an older platform.
        var installed = Pkg("platforms;android-33", "3", installed: true);
        var available = new[] { Pkg("platforms;android-34", "1") };

        AndroidSdkPackageVersioning.GetInPlaceUpdate(installed, available).Should().BeNull();
    }

    [Fact]
    public void GetInPlaceUpdate_NullOrEmptyInputs_NoUpdate()
    {
        var installed = Pkg("emulator", null, installed: true);
        AndroidSdkPackageVersioning.GetInPlaceUpdate(installed, new[] { Pkg("emulator", "37.0.0") })
            .Should().BeNull();
    }

    // ---- GetUpdates ----

    [Fact]
    public void GetUpdates_ReturnsOnlyGenuineInPlaceUpdates()
    {
        var installed = new[]
        {
            Pkg("emulator", "36.4.10", installed: true),                         // update -> 36.6.11
            Pkg("platform-tools", "36.0.0", installed: true),                    // update -> 37.0.0
            Pkg("build-tools;36.1.0", "36.1.0", installed: true),                // NOT an update (side-by-side)
            Pkg("platforms;android-33", "3", installed: true),                   // already newest revision
        };
        var available = new[]
        {
            Pkg("emulator", "36.6.11"),
            Pkg("platform-tools", "37.0.0"),
            Pkg("build-tools;37.0.0", "37.0.0"),
            Pkg("platforms;android-33", "3"),
        };

        var updates = AndroidSdkPackageVersioning.GetUpdates(installed, available).ToList();

        updates.Select(u => u.Path).Should().BeEquivalentTo("emulator", "platform-tools");
    }

    // ---- CompareVersions ----

    [Theory]
    [InlineData("10", "3", 1)]
    [InlineData("3", "10", -1)]
    [InlineData("18", "17", 1)]
    [InlineData("36.6.11", "36.4.10", 1)]
    [InlineData("36.4.10", "36.6.11", -1)]
    [InlineData("37.0.0", "36.0.0", 1)]
    [InlineData("36.6.11", "36.6.11", 0)]
    public void CompareVersions_OrdersCorrectly(string a, string b, int expectedSign)
    {
        Math.Sign(AndroidSdkPackageVersioning.CompareVersions(a, b)).Should().Be(expectedSign);
    }

    [Fact]
    public void CompareVersions_NullHandling()
    {
        AndroidSdkPackageVersioning.CompareVersions(null, null).Should().Be(0);
        AndroidSdkPackageVersioning.CompareVersions(null, "1").Should().BeLessThan(0);
        AndroidSdkPackageVersioning.CompareVersions("1", null).Should().BeGreaterThan(0);
    }

    // ---- IsStableVersion ----

    [Theory]
    [InlineData("36.6.11", true)]
    [InlineData("3", true)]
    [InlineData("37.0.0", true)]
    [InlineData("36.0.0-rc1", false)]
    [InlineData("1.0.0-beta", false)]
    [InlineData("2.1-alpha02", false)]
    [InlineData("36.6.11-canary", false)]
    public void IsStableVersion_DetectsPrereleases(string version, bool expected)
    {
        AndroidSdkPackageVersioning.IsStableVersion(version).Should().Be(expected);
    }

    [Fact]
    public void IsStableVersion_DoesNotFalseMatchSubstrings()
    {
        // "rc" should not match inside a plain numeric version; these are all stable.
        AndroidSdkPackageVersioning.IsStableVersion("36.0.0").Should().BeTrue();
        AndroidSdkPackageVersioning.IsStableVersion("17").Should().BeTrue();
    }

    [Fact]
    public void IsStableVersion_NullOrEmpty_IsFalse()
    {
        AndroidSdkPackageVersioning.IsStableVersion(null).Should().BeFalse();
        AndroidSdkPackageVersioning.IsStableVersion("").Should().BeFalse();
    }
}
