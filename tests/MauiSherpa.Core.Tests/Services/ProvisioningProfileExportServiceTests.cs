using System.Text;
using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public sealed class ProvisioningProfileExportServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        AppContext.BaseDirectory,
        $"profile-export-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAsync_PreservesExistingFilesAndProfilesWithTheSameName()
    {
        Directory.CreateDirectory(_directory);
        var existingPath = Path.Combine(_directory, "Shared - UUID-1.mobileprovision");
        await File.WriteAllTextAsync(existingPath, "existing");

        var firstPath = await ProvisioningProfileExportService.SaveAsync(
            _directory, CreateProfile("Shared", "id-1", "UUID-1"), [1]);
        var secondPath = await ProvisioningProfileExportService.SaveAsync(
            _directory, CreateProfile("Shared", "id-2", "UUID-2"), [2]);
        var repeatedPath = await ProvisioningProfileExportService.SaveAsync(
            _directory, CreateProfile("Shared", "id-1", "UUID-1"), [3]);

        firstPath.Should().EndWith("Shared - UUID-1 (1).mobileprovision");
        secondPath.Should().EndWith("Shared - UUID-2.mobileprovision");
        repeatedPath.Should().EndWith("Shared - UUID-1 (2).mobileprovision");
        (await File.ReadAllTextAsync(existingPath)).Should().Be("existing");
        (await File.ReadAllBytesAsync(firstPath)).Should().Equal(1);
        (await File.ReadAllBytesAsync(secondPath)).Should().Equal(2);
        (await File.ReadAllBytesAsync(repeatedPath)).Should().Equal(3);
    }

    [Fact]
    public async Task SaveAsync_ConcurrentExportsDoNotOverwriteOneAnother()
    {
        var profile = CreateProfile("Shared", "id", "UUID");
        var paths = await Task.WhenAll(Enumerable.Range(1, 16)
            .Select(number => ProvisioningProfileExportService.SaveAsync(
                _directory, profile, [(byte)number])));

        paths.Should().OnlyHaveUniqueItems();
        Directory.GetFiles(_directory).Should().HaveCount(16);
        var values = await Task.WhenAll(paths.Select(path => File.ReadAllBytesAsync(path)));
        values.Select(bytes => bytes.Single()).Should().BeEquivalentTo(
            Enumerable.Range(1, 16).Select(number => (byte)number));
    }

    [Theory]
    [InlineData("MAC_OS", ".provisionprofile")]
    [InlineData("MACOS", ".provisionprofile")]
    [InlineData("OSX", ".provisionprofile")]
    [InlineData("IOS", ".mobileprovision")]
    public async Task SaveAsync_UsesThePlatformExtension(string platform, string extension)
    {
        var profile = CreateProfile("Profile", "id", "uuid") with { Platform = platform };

        var path = await ProvisioningProfileExportService.SaveAsync(_directory, profile, [1]);

        path.Should().EndWith(extension);
    }

    [Fact]
    public async Task SaveAsync_SanitizesAndBoundsLongUnicodeFileNames()
    {
        var profile = CreateProfile(
            "../" + string.Concat(Enumerable.Repeat("\u00e9\U0001f34e", 100)),
            "id",
            string.Concat(Enumerable.Repeat("x", 200)));

        var path = await ProvisioningProfileExportService.SaveAsync(_directory, profile, [1]);

        Path.GetDirectoryName(path).Should().Be(_directory);
        var name = Path.GetFileName(path);
        name.Should().NotContain("/");
        Encoding.UTF8.GetByteCount(name.Normalize(NormalizationForm.FormD)).Should().BeLessThan(255);
        (await File.ReadAllBytesAsync(path)).Should().Equal(1);
    }

    [Fact]
    public async Task SaveAsync_UsesProfileIdWhenUuidIsMissing()
    {
        var profile = CreateProfile("...", "id-123", "");

        var path = await ProvisioningProfileExportService.SaveAsync(_directory, profile, [1]);

        Path.GetFileName(path).Should().Be("Profile - id-123.mobileprovision");
    }

    [Fact]
    public async Task SaveAsync_RejectsEmptyContentWithoutCreatingFiles()
    {
        var action = () => ProvisioningProfileExportService.SaveAsync(
            _directory, CreateProfile("Profile", "id", "uuid"), []);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no data*");
        Directory.Exists(_directory).Should().BeFalse();
    }

    private static AppleProfile CreateProfile(string name, string id, string uuid) =>
        new(id, name, "IOS_APP_DEVELOPMENT", "IOS", "ACTIVE", DateTime.UtcNow, null, uuid);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
