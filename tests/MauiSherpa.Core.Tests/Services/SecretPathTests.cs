using FluentAssertions;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class SecretPathTests
{
    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("managed", "/managed")]
    [InlineData("/managed/", "/managed")]
    [InlineData("\\managed\\certificates", "/managed/certificates")]
    public void NormalizeFolderPath_NormalizesSupportedFolders(string? input, string expected)
    {
        SecretPath.NormalizeFolderPath(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("/managed//certificates")]
    [InlineData("/managed/../certificates")]
    [InlineData("/managed/./certificates")]
    public void NormalizeFolderPath_RejectsAmbiguousFolders(string input)
    {
        var act = () => SecretPath.NormalizeFolderPath(input);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromFlatKey_SplitsOnLastSeparator()
    {
        var path = SecretPath.FromFlatKey("sherpa-secrets/api-key");

        path.FolderPath.Should().Be("/sherpa-secrets");
        path.Key.Should().Be("api-key");
        path.ToFlatKey().Should().Be("sherpa-secrets/api-key");
    }

    [Fact]
    public void CreateId_UsesNormalizedPath()
    {
        var left = LocalVaultItem.CreateId("LOCAL-PROVIDER-SECRET", "managed", "api-key");
        var right = LocalVaultItem.CreateId("local-provider-secret", "/managed/", "api-key");

        left.Should().Be(right);
    }
}
