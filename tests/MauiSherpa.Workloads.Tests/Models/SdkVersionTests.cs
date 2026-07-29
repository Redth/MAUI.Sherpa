using FluentAssertions;
using MauiSherpa.Workloads.Models;

namespace MauiSherpa.Workloads.Tests.Models;

public class SdkVersionTests
{
    [Theory]
    [InlineData("10.0.100", 10, 0, 100)]
    [InlineData("9.0.105", 9, 0, 105)]
    [InlineData("8.0.300", 8, 0, 300)]
    [InlineData("7.0.400", 7, 0, 400)]
    public void Parse_ValidVersion_ReturnsCorrectComponents(string version, int major, int minor, int patch)
    {
        // Act
        var result = SdkVersion.Parse(version);

        // Assert
        result.Major.Should().Be(major);
        result.Minor.Should().Be(minor);
        result.Patch.Should().Be(patch);
        result.Version.Should().Be(version);
        result.IsPreview.Should().BeFalse();
        result.PreviewLabel.Should().BeNull();
    }

    [Theory]
    [InlineData("10.0.100-preview.1", 10, 0, 100, "preview.1")]
    [InlineData("9.0.100-rc.1", 9, 0, 100, "rc.1")]
    [InlineData("8.0.100-preview.7.23376.6", 8, 0, 100, "preview.7.23376.6")]
    public void Parse_PreviewVersion_ReturnsCorrectComponents(string version, int major, int minor, int patch, string previewLabel)
    {
        // Act
        var result = SdkVersion.Parse(version);

        // Assert
        result.Major.Should().Be(major);
        result.Minor.Should().Be(minor);
        result.Patch.Should().Be(patch);
        result.IsPreview.Should().BeTrue();
        result.PreviewLabel.Should().Be(previewLabel);
    }

    [Theory]
    [InlineData("10.0.100", "10.0.100")]
    [InlineData("10.0.105", "10.0.100")]
    [InlineData("9.0.300", "9.0.300")]
    [InlineData("9.0.305", "9.0.300")]
    public void FeatureBand_ReturnsCorrectValue(string version, string expectedFeatureBand)
    {
        // Act
        var result = SdkVersion.Parse(version);

        // Assert
        result.FeatureBand.Should().Be(expectedFeatureBand);
    }

    [Theory]
    [InlineData("10.0.100", "10.0")]
    [InlineData("9.0.105", "9.0")]
    [InlineData("8.0.300", "8.0")]
    public void RuntimeVersion_ReturnsCorrectValue(string version, string expectedRuntime)
    {
        // Act
        var result = SdkVersion.Parse(version);

        // Assert
        result.RuntimeVersion.Should().Be(expectedRuntime);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("1.0")]
    [InlineData("")]
    public void Parse_InvalidVersion_ThrowsArgumentException(string version)
    {
        // Act & Assert
        var action = () => SdkVersion.Parse(version);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToString_ReturnsVersion()
    {
        // Arrange
        var sdkVersion = SdkVersion.Parse("10.0.100");

        // Act
        var result = sdkVersion.ToString();

        // Assert
        result.Should().Be("10.0.100");
    }

    [Theory]
    [InlineData("10.0.100")]
    [InlineData("9.0.105")]
    [InlineData("8.0.300-preview.1")]
    public void TryParse_ValidVersion_ReturnsTrue(string version)
    {
        // Act
        var success = SdkVersion.TryParse(version, out var result);

        // Assert
        success.Should().BeTrue();
        result.Should().NotBeNull();
        result!.Version.Should().Be(version);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("1.0")]
    [InlineData("")]
    [InlineData("tools")]
    [InlineData("NuGetFallbackFolder")]
    public void TryParse_InvalidVersion_ReturnsFalse(string version)
    {
        // Act
        var success = SdkVersion.TryParse(version, out var result);

        // Assert
        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void SortDescending_OrdersPreviewsByPrereleaseLabel()
    {
        // Every 11.0.100-preview.* shares major.minor.patch, so ordering must fall through to the
        // prerelease labels rather than leaving the newest install to enumeration order.
        var versions = new[]
        {
            SdkVersion.Parse("11.0.100-preview.5.26302.115"),
            SdkVersion.Parse("11.0.100-preview.4.26230.115"),
            SdkVersion.Parse("11.0.100-preview.6.26359.118"),
        };

        var sorted = SdkVersion.SortDescending(versions);

        sorted.Select(v => v.Version).Should().Equal(
            "11.0.100-preview.6.26359.118",
            "11.0.100-preview.5.26302.115",
            "11.0.100-preview.4.26230.115");
    }

    [Fact]
    public void SortDescending_RanksStableAboveItsOwnPreviews()
    {
        var versions = new[]
        {
            SdkVersion.Parse("11.0.100-rc.1.25451.107"),
            SdkVersion.Parse("11.0.100"),
            SdkVersion.Parse("11.0.100-preview.6.26359.118"),
            SdkVersion.Parse("10.0.302"),
        };

        var sorted = SdkVersion.SortDescending(versions);

        sorted.Select(v => v.Version).Should().Equal(
            "11.0.100",
            "11.0.100-rc.1.25451.107",
            "11.0.100-preview.6.26359.118",
            "10.0.302");
    }

    [Fact]
    public void CompareTo_UsesFullSemanticVersion()
    {
        var newer = SdkVersion.Parse("11.0.100-preview.6.26359.118");
        var older = SdkVersion.Parse("11.0.100-preview.5.26302.115");

        newer.CompareTo(older).Should().BePositive();
        older.CompareTo(newer).Should().BeNegative();
        newer.CompareTo(null).Should().BePositive();
    }
}
