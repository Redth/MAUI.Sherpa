using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace MauiSherpa.Core.Tests.Services;

public class ManagedSecretsServiceTests
{
    readonly Mock<ICloudSecretsService> _cloudService = new();
    readonly Mock<ILoggingService> _logger = new();
    readonly ManagedSecretsService _sut;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ManagedSecretsServiceTests()
    {
        _sut = new ManagedSecretsService(_cloudService.Object, _logger.Object);
    }

    [Fact]
    public async Task ListAsync_NoActiveProvider_ReturnsEmpty()
    {
        _cloudService.Setup(x => x.ActiveProvider).Returns((CloudSecretsProviderConfig?)null);

        var result = await _sut.ListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_WithSecrets_ReturnsManagedSecrets()
    {
        SetupActiveProvider();
        var metaKeys = new List<string> { "sherpa-secrets-meta/api-key", "sherpa-secrets-meta/db-password" };
        _cloudService.Setup(x => x.ListSecretsAsync("sherpa-secrets-meta/", It.IsAny<CancellationToken>()))
            .ReturnsAsync(metaKeys);

        SetupMetadataByFullKey("sherpa-secrets-meta/api-key", new ManagedSecret("api-key", ManagedSecretType.String, "API Key", null, DateTime.UtcNow, DateTime.UtcNow));
        SetupMetadataByFullKey("sherpa-secrets-meta/db-password", new ManagedSecret("db-password", ManagedSecretType.String, "DB Password", null, DateTime.UtcNow, DateTime.UtcNow));

        var result = await _sut.ListAsync();

        result.Should().HaveCount(2);
        result[0].Key.Should().Be("api-key");
        result[1].Key.Should().Be("db-password");
    }

    [Fact]
    public async Task ListAsync_WithMissingMetadata_SkipsSecret()
    {
        SetupActiveProvider();
        _cloudService.Setup(x => x.ListSecretsAsync("sherpa-secrets-meta/", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "sherpa-secrets-meta/orphan" });
        _cloudService.Setup(x => x.GetSecretAsync("sherpa-secrets-meta/orphan", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var result = await _sut.ListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListFoldersAsync_WithFolders_ReturnsManagedSecretFolders()
    {
        SetupActiveProvider();
        var folderKeys = new List<string> { "sherpa-secret-folders/team", "sherpa-secret-folders/team/mobile" };
        _cloudService.Setup(x => x.ListSecretsAsync("sherpa-secret-folders/", It.IsAny<CancellationToken>()))
            .ReturnsAsync(folderKeys);

        SetupFolderByFullKey("sherpa-secret-folders/team", new ManagedSecretFolder("/team", "team", DateTime.UtcNow));
        SetupFolderByFullKey("sherpa-secret-folders/team/mobile", new ManagedSecretFolder("/team/mobile", "mobile", DateTime.UtcNow));

        var result = await _sut.ListFoldersAsync();

        result.Should().HaveCount(2);
        result.Select(f => f.Path).Should().Equal("/team", "/team/mobile");
    }

    [Fact]
    public async Task CreateFolderAsync_StoresFolderMetadata()
    {
        SetupActiveProvider();
        _cloudService.Setup(x => x.StoreSecretAsync(
                "sherpa-secrets/team/mobile/sherpa-folder-marker",
                It.IsAny<byte[]>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _cloudService.Setup(x => x.StoreSecretAsync(
                "sherpa-secret-folders/team/mobile",
                It.IsAny<byte[]>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.CreateFolderAsync("/team/mobile");

        result.Should().BeTrue();
        _cloudService.Verify(x => x.StoreSecretAsync(
            "sherpa-secrets/team/mobile/sherpa-folder-marker",
            It.IsAny<byte[]>(),
            It.Is<Dictionary<string, string>>(m => m["SherpaKind"] == "FolderPlaceholder" && m["FolderPath"] == "/team/mobile"),
            It.IsAny<CancellationToken>()), Times.Once);
        _cloudService.Verify(x => x.StoreSecretAsync(
            "sherpa-secret-folders/team/mobile",
            It.Is<byte[]>(b => Encoding.UTF8.GetString(b).Contains("\"path\":\"/team/mobile\"")),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateFolderAsync_Root_Throws()
    {
        SetupActiveProvider();

        var act = () => _sut.CreateFolderAsync("/");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RenameFolderAsync_MovesSecretsAndChildFolders()
    {
        SetupActiveProvider();
        var created = DateTime.UtcNow.AddDays(-1);
        var api = new ManagedSecret("team/api-key", ManagedSecretType.String, "API Key", null, created, created);
        var token = new ManagedSecret("team/mobile/token", ManagedSecretType.String, "Token", null, created, created);

        SetupSecretMetadataList(api, token);
        SetupSecretValue("team/api-key", "api-value");
        SetupSecretValue("team/mobile/token", "token-value");
        SetupFolderList(
            new ManagedSecretFolder("/team", "team", created),
            new ManagedSecretFolder("/team/mobile", "mobile", created));
        _cloudService.Setup(x => x.StoreSecretAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _cloudService.Setup(x => x.DeleteSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.RenameFolderAsync("/team", "/project");

        result.Should().BeTrue();
        _cloudService.Verify(x => x.StoreSecretAsync("sherpa-secrets/project/api-key", It.IsAny<byte[]>(), null, It.IsAny<CancellationToken>()), Times.Once);
        _cloudService.Verify(x => x.StoreSecretAsync("sherpa-secrets/project/mobile/token", It.IsAny<byte[]>(), null, It.IsAny<CancellationToken>()), Times.Once);
        _cloudService.Verify(x => x.StoreSecretAsync("sherpa-secrets-meta/project/api-key", It.IsAny<byte[]>(), null, It.IsAny<CancellationToken>()), Times.Once);
        _cloudService.Verify(x => x.StoreSecretAsync("sherpa-secrets-meta/project/mobile/token", It.IsAny<byte[]>(), null, It.IsAny<CancellationToken>()), Times.Once);
        _cloudService.Verify(x => x.StoreSecretAsync("sherpa-secret-folders/project", It.IsAny<byte[]>(), null, It.IsAny<CancellationToken>()), Times.Once);
        _cloudService.Verify(x => x.StoreSecretAsync("sherpa-secret-folders/project/mobile", It.IsAny<byte[]>(), null, It.IsAny<CancellationToken>()), Times.Once);
        _cloudService.Verify(x => x.StoreSecretAsync("sherpa-secrets/project/sherpa-folder-marker", It.IsAny<byte[]>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
        _cloudService.Verify(x => x.StoreSecretAsync("sherpa-secrets/project/mobile/sherpa-folder-marker", It.IsAny<byte[]>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
        _cloudService.Verify(x => x.DeleteSecretAsync("sherpa-secrets/team/api-key", It.IsAny<CancellationToken>()), Times.Once);
        _cloudService.Verify(x => x.DeleteSecretAsync("sherpa-secrets-meta/team/api-key", It.IsAny<CancellationToken>()), Times.Once);
        _cloudService.Verify(x => x.DeleteSecretAsync("sherpa-secret-folders/team", It.IsAny<CancellationToken>()), Times.Once);
        _cloudService.Verify(x => x.DeleteSecretAsync("sherpa-secret-folders/team/mobile", It.IsAny<CancellationToken>()), Times.Once);
        _cloudService.Verify(x => x.DeleteSecretAsync("sherpa-secrets/team/sherpa-folder-marker", It.IsAny<CancellationToken>()), Times.Once);
        _cloudService.Verify(x => x.DeleteSecretAsync("sherpa-secrets/team/mobile/sherpa-folder-marker", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RenameFolderAsync_ExistingTargetSecret_ReturnsFalse()
    {
        SetupActiveProvider();
        var created = DateTime.UtcNow;
        SetupSecretMetadataList(
            new ManagedSecret("team/api-key", ManagedSecretType.String, "API Key", null, created, created),
            new ManagedSecret("project/api-key", ManagedSecretType.String, "Existing", null, created, created));

        var result = await _sut.RenameFolderAsync("/team", "/project");

        result.Should().BeFalse();
        _cloudService.Verify(x => x.StoreSecretAsync(It.IsAny<string>(), It.IsAny<byte[]>(), null, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteFolderAsync_EmptyFolder_DeletesFolderMetadata()
    {
        SetupActiveProvider();
        SetupSecretMetadataList();
        SetupFolderList(new ManagedSecretFolder("/team", "team", DateTime.UtcNow));
        _cloudService.Setup(x => x.DeleteSecretAsync("sherpa-secret-folders/team", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _cloudService.Setup(x => x.DeleteSecretAsync("sherpa-secrets/team/sherpa-folder-marker", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.DeleteFolderAsync("/team");

        result.Should().BeTrue();
        _cloudService.Verify(x => x.DeleteSecretAsync("sherpa-secret-folders/team", It.IsAny<CancellationToken>()), Times.Once);
        _cloudService.Verify(x => x.DeleteSecretAsync("sherpa-secrets/team/sherpa-folder-marker", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteFolderAsync_WithSecrets_ReturnsFalse()
    {
        SetupActiveProvider();
        var created = DateTime.UtcNow;
        SetupSecretMetadataList(new ManagedSecret("team/api-key", ManagedSecretType.String, "API Key", null, created, created));

        var result = await _sut.DeleteFolderAsync("/team");

        result.Should().BeFalse();
        _cloudService.Verify(x => x.DeleteSecretAsync("sherpa-secret-folders/team", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAsync_ReturnsMetadata()
    {
        SetupActiveProvider();
        var expected = new ManagedSecret(
            "my-key",
            ManagedSecretType.File,
            "A file",
            "data.bin",
            DateTime.UtcNow,
            DateTime.UtcNow,
            new Dictionary<string, string> { ["environment/name"] = "prod" });
        SetupMetadata("my-key", expected);

        var result = await _sut.GetAsync("my-key");

        result.Should().NotBeNull();
        result!.Key.Should().Be("my-key");
        result.Type.Should().Be(ManagedSecretType.File);
        result.OriginalFileName.Should().Be("data.bin");
        result.Metadata.Should().ContainKey("environment/name").WhoseValue.Should().Be("prod");
    }

    [Fact]
    public async Task GetValueAsync_ReturnsBytes()
    {
        SetupActiveProvider();
        var value = Encoding.UTF8.GetBytes("secret-value");
        _cloudService.Setup(x => x.GetSecretAsync("sherpa-secrets/my-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(value);

        var result = await _sut.GetValueAsync("my-key");

        result.Should().BeEquivalentTo(value);
    }

    [Fact]
    public async Task CreateAsync_StoresValueAndMetadata()
    {
        SetupActiveProvider();
        var value = Encoding.UTF8.GetBytes("test-value");
        var metadata = new Dictionary<string, string>
        {
            ["environment/name"] = "prod",
            ["owner=email"] = "team@example.com"
        };

        _cloudService.Setup(x => x.StoreSecretAsync("sherpa-secrets/new-key", value, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _cloudService.Setup(x => x.StoreSecretAsync(
                It.Is<string>(k => k.StartsWith("sherpa-secrets-meta/")),
                It.IsAny<byte[]>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.CreateAsync("new-key", value, ManagedSecretType.String, "A test secret", metadata: metadata);

        result.Should().BeTrue();
        _cloudService.Verify(x => x.StoreSecretAsync("sherpa-secrets/new-key", value, null, It.IsAny<CancellationToken>()), Times.Once);
        _cloudService.Verify(x => x.StoreSecretAsync(
            "sherpa-secrets-meta/new-key",
            It.Is<byte[]>(b =>
                Encoding.UTF8.GetString(b).Contains("\"environment/name\":\"prod\"") &&
                Encoding.UTF8.GetString(b).Contains("\"owner=email\":\"team@example.com\"")),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_NoProvider_Throws()
    {
        _cloudService.Setup(x => x.ActiveProvider).Returns((CloudSecretsProviderConfig?)null);

        var act = () => _sut.CreateAsync("key", new byte[] { 1 }, ManagedSecretType.String);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CreateAsync_EmptyKey_Throws()
    {
        SetupActiveProvider();

        var act = () => _sut.CreateAsync("", new byte[] { 1 }, ManagedSecretType.String);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UpdateAsync_UpdatesValueAndMetadata()
    {
        SetupActiveProvider();
        var existing = new ManagedSecret("my-key", ManagedSecretType.String, "Old desc", null, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(-1));
        SetupMetadata("my-key", existing);

        var newValue = Encoding.UTF8.GetBytes("new-value");
        _cloudService.Setup(x => x.StoreSecretAsync("sherpa-secrets/my-key", newValue, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _cloudService.Setup(x => x.StoreSecretAsync(
                It.Is<string>(k => k.StartsWith("sherpa-secrets-meta/")),
                It.IsAny<byte[]>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.UpdateAsync("my-key", newValue, "New desc");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_NonExistent_ReturnsFalse()
    {
        SetupActiveProvider();
        _cloudService.Setup(x => x.GetSecretAsync("sherpa-secrets-meta/missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var result = await _sut.UpdateAsync("missing", description: "test");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task MoveAsync_CopiesSecretAndMetadataThenDeletesOriginal()
    {
        SetupActiveProvider();
        var created = DateTime.UtcNow.AddDays(-1);
        var existing = new ManagedSecret("team/api-key", ManagedSecretType.String, "Old desc", null, created, created);
        SetupMetadata("team/api-key", existing);
        SetupMetadata("project/api-key", null);
        SetupSecretValue("team/api-key", "secret-value");
        _cloudService.Setup(x => x.SecretExistsAsync("sherpa-secrets/project/api-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _cloudService.Setup(x => x.StoreSecretAsync("sherpa-secrets/project/api-key", It.IsAny<byte[]>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _cloudService.Setup(x => x.StoreSecretAsync("sherpa-secrets-meta/project/api-key", It.IsAny<byte[]>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _cloudService.Setup(x => x.DeleteSecretAsync("sherpa-secrets/team/api-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _cloudService.Setup(x => x.DeleteSecretAsync("sherpa-secrets-meta/team/api-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.MoveAsync("team/api-key", "project/api-key", description: "New desc");

        result.Should().BeTrue();
        _cloudService.Verify(x => x.StoreSecretAsync(
            "sherpa-secrets/project/api-key",
            It.Is<byte[]>(b => b.SequenceEqual(Encoding.UTF8.GetBytes("secret-value"))),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
        _cloudService.Verify(x => x.StoreSecretAsync(
            "sherpa-secrets-meta/project/api-key",
            It.Is<byte[]>(b => Encoding.UTF8.GetString(b).Contains("\"key\":\"project/api-key\"") && Encoding.UTF8.GetString(b).Contains("\"description\":\"New desc\"")),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
        _cloudService.Verify(x => x.DeleteSecretAsync("sherpa-secrets/team/api-key", It.IsAny<CancellationToken>()), Times.Once);
        _cloudService.Verify(x => x.DeleteSecretAsync("sherpa-secrets-meta/team/api-key", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MoveAsync_TargetExists_ReturnsFalse()
    {
        SetupActiveProvider();
        var created = DateTime.UtcNow;
        SetupMetadata("team/api-key", new ManagedSecret("team/api-key", ManagedSecretType.String, "API Key", null, created, created));
        SetupMetadata("project/api-key", new ManagedSecret("project/api-key", ManagedSecretType.String, "Existing", null, created, created));

        var result = await _sut.MoveAsync("team/api-key", "project/api-key");

        result.Should().BeFalse();
        _cloudService.Verify(x => x.StoreSecretAsync("sherpa-secrets/project/api-key", It.IsAny<byte[]>(), null, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MoveAsync_SameKey_UpdatesSecret()
    {
        SetupActiveProvider();
        var created = DateTime.UtcNow;
        SetupMetadata("team/api-key", new ManagedSecret("team/api-key", ManagedSecretType.String, "Old desc", null, created, created));
        var newValue = Encoding.UTF8.GetBytes("new-value");
        _cloudService.Setup(x => x.StoreSecretAsync("sherpa-secrets/team/api-key", newValue, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _cloudService.Setup(x => x.StoreSecretAsync("sherpa-secrets-meta/team/api-key", It.IsAny<byte[]>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.MoveAsync("team/api-key", "team/api-key", newValue, "New desc");

        result.Should().BeTrue();
        _cloudService.Verify(x => x.DeleteSecretAsync("sherpa-secrets/team/api-key", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_DeletesValueAndMetadata()
    {
        SetupActiveProvider();
        _cloudService.Setup(x => x.DeleteSecretAsync("sherpa-secrets/my-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _cloudService.Setup(x => x.DeleteSecretAsync("sherpa-secrets-meta/my-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.DeleteAsync("my-key");

        result.Should().BeTrue();
        _cloudService.Verify(x => x.DeleteSecretAsync("sherpa-secrets/my-key", It.IsAny<CancellationToken>()), Times.Once);
        _cloudService.Verify(x => x.DeleteSecretAsync("sherpa-secrets-meta/my-key", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_FileType_StoresOriginalFileName()
    {
        SetupActiveProvider();
        var value = new byte[] { 1, 2, 3 };

        _cloudService.Setup(x => x.StoreSecretAsync(It.IsAny<string>(), It.IsAny<byte[]>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.CreateAsync("cert-file", value, ManagedSecretType.File, "My cert", "certificate.p12");

        result.Should().BeTrue();

        // Verify metadata contains original filename
        _cloudService.Verify(x => x.StoreSecretAsync(
            "sherpa-secrets-meta/cert-file",
            It.Is<byte[]>(b => Encoding.UTF8.GetString(b).Contains("certificate.p12")),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    void SetupActiveProvider()
    {
        var provider = new CloudSecretsProviderConfig("test-id", "Test Provider", CloudSecretsProviderType.AzureKeyVault, new());
        _cloudService.Setup(x => x.ActiveProvider).Returns(provider);
    }

    void SetupMetadata(string key, ManagedSecret? meta)
    {
        if (meta is null)
        {
            _cloudService.Setup(x => x.GetSecretAsync($"sherpa-secrets-meta/{key}", It.IsAny<CancellationToken>()))
                .ReturnsAsync((byte[]?)null);
        }
        else
        {
            var json = JsonSerializer.Serialize(meta, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            _cloudService.Setup(x => x.GetSecretAsync($"sherpa-secrets-meta/{key}", It.IsAny<CancellationToken>()))
                .ReturnsAsync(bytes);
        }
    }

    void SetupMetadataByFullKey(string fullKey, ManagedSecret meta)
    {
        var json = JsonSerializer.Serialize(meta, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        _cloudService.Setup(x => x.GetSecretAsync(fullKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);
    }

    void SetupSecretMetadataList(params ManagedSecret[] secrets)
    {
        var keys = secrets.Select(secret => $"sherpa-secrets-meta/{secret.Key}").ToList();
        _cloudService.Setup(x => x.ListSecretsAsync("sherpa-secrets-meta/", It.IsAny<CancellationToken>()))
            .ReturnsAsync(keys);
        foreach (var secret in secrets)
            SetupMetadataByFullKey($"sherpa-secrets-meta/{secret.Key}", secret);
    }

    void SetupSecretValue(string key, string value)
    {
        _cloudService.Setup(x => x.GetSecretAsync($"sherpa-secrets/{key}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(value));
    }

    void SetupFolderList(params ManagedSecretFolder[] folders)
    {
        var keys = folders.Select(folder => $"sherpa-secret-folders/{folder.Path.TrimStart('/')}").ToList();
        _cloudService.Setup(x => x.ListSecretsAsync("sherpa-secret-folders/", It.IsAny<CancellationToken>()))
            .ReturnsAsync(keys);
        foreach (var folder in folders)
            SetupFolderByFullKey($"sherpa-secret-folders/{folder.Path.TrimStart('/')}", folder);
    }

    void SetupFolderByFullKey(string fullKey, ManagedSecretFolder folder)
    {
        var json = JsonSerializer.Serialize(folder, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        _cloudService.Setup(x => x.GetSecretAsync(fullKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);
    }
}
