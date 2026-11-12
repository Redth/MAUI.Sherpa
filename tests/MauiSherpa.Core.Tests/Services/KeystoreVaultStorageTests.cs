using System.Text;
using System.Text.Json;
using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;
using Xunit;

namespace MauiSherpa.Core.Tests.Services;

public class KeystoreVaultStorageTests : IDisposable
{
    private readonly Dictionary<string, byte[]> _vault = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _secureStorage = new(StringComparer.Ordinal);
    private readonly string _directory;
    private readonly Mock<ISecretsProviderRegistry> _registry = new();
    private readonly Mock<ISecureStorageService> _secureStorageMock = new();
    private readonly KeystoreService _service;

    public KeystoreVaultStorageTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "sherpa-keystore-vault-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        var provider = new Mock<ICloudSecretsProvider>();
        provider.SetupGet(x => x.ProviderType).Returns(CloudSecretsProviderType.None);
        provider.Setup(x => x.ListSecretsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? prefix, CancellationToken _) =>
                _vault.Keys.Where(k => prefix is null || k.StartsWith(prefix, StringComparison.Ordinal)).ToList());
        provider.Setup(x => x.GetSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) => _vault.GetValueOrDefault(key));
        provider.Setup(x => x.GetSecretMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Dictionary<string, string>?)null);
        provider.Setup(x => x.StoreSecretAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, byte[] value, Dictionary<string, string>? _, CancellationToken _) =>
            {
                _vault[key] = value;
                return true;
            });
        provider.Setup(x => x.DeleteSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) => _vault.Remove(key));

        _registry.Setup(x => x.GetProviderAsync(CloudSecretsService.DefaultLocalProviderId)).ReturnsAsync(provider.Object);

        var secureStorage = _secureStorageMock;
        secureStorage.Setup(x => x.GetAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => _secureStorage.GetValueOrDefault(key));
        secureStorage.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((key, value) => _secureStorage[key] = value)
            .Returns(Task.CompletedTask);
        secureStorage.Setup(x => x.RemoveAsync(It.IsAny<string>()))
            .Callback<string>(key => _secureStorage.Remove(key))
            .Returns(Task.CompletedTask);

        _service = CreateService(LocalVaultIntroductionDecision.Enabled);
    }

    private KeystoreService CreateService(LocalVaultIntroductionDecision decision)
    {
        var introduction = new Mock<ILocalVaultIntroductionService>();
        introduction.Setup(x => x.GetState())
            .Returns(new LocalVaultIntroductionState(LocalVaultIntroductionState.CurrentVersion, decision));
        return new KeystoreService(
            Mock.Of<IOpenJdkSettingsService>(),
            _secureStorageMock.Object,
            Mock.Of<ILoggingService>(),
            _registry.Object,
            Mock.Of<ICloudSecretsService>(),
            introduction.Object)
        {
            LegacyRegistryPath = Path.Combine(_directory, "android-keystores.json"),
            FileKeystoreDirectory = Path.Combine(_directory, "keystores")
        };
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }

    private static AndroidKeystore Keystore(string id = "ks-1", string alias = "release", string filePath = "/source/release.jks") =>
        new(id, alias, filePath, "JKS", new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc), "notes");

    private async Task WriteLegacyRegistryAsync(params AndroidKeystore[] keystores) =>
        await File.WriteAllTextAsync(Path.Combine(_directory, "android-keystores.json"), JsonSerializer.Serialize(keystores));

    [Fact]
    public async Task AddKeystoreAsync_StoresFilePasswordsAndMetadataInTheLocalVault()
    {
        var stored = await _service.AddKeystoreAsync(Keystore(), new byte[] { 1, 2, 3 }, "store-pw", "key-pw");

        stored.StoredInVault.Should().BeTrue();
        stored.FilePath.Should().Be("release.jks");
        _vault["KEYSTORE_release_JKS"].Should().Equal(1, 2, 3);
        Encoding.UTF8.GetString(_vault["KEYSTORE_release_PWD"]).Should().Be("store-pw");
        Encoding.UTF8.GetString(_vault["KEYSTORE_release_KEYPWD"]).Should().Be("key-pw");

        var listed = (await _service.ListKeystoresAsync()).Should().ContainSingle().Subject;
        listed.Should().Be(new AndroidKeystore("ks-1", "release", "release.jks", "JKS",
            new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc), "notes", StoredInVault: true));
        (await _service.GetKeystoreContentAsync("ks-1")).Should().Equal(1, 2, 3);
        (await _service.GetKeystorePasswordAsync("ks-1")).Should().Be("store-pw");
        (await _service.GetKeyPasswordAsync("ks-1")).Should().Be("key-pw");
        _secureStorage.Should().BeEmpty();
    }

    [Fact]
    public async Task AddKeystoreAsync_RejectsADifferentKeystoreWithTheSameAlias()
    {
        await _service.AddKeystoreAsync(Keystore(), new byte[] { 1 }, "pw", "pw");

        var act = () => _service.AddKeystoreAsync(Keystore(id: "ks-2"), new byte[] { 2 }, "pw", "pw");

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*alias 'release' is already stored*");
        _vault["KEYSTORE_release_JKS"].Should().Equal(1);
    }

    [Fact]
    public async Task SavePasswords_UpdateTheVaultCopy()
    {
        await _service.AddKeystoreAsync(Keystore(), new byte[] { 1 }, "old", null);

        await _service.SaveKeystorePasswordAsync("ks-1", "new-store");
        await _service.SaveKeyPasswordAsync("ks-1", "new-key");

        Encoding.UTF8.GetString(_vault["KEYSTORE_release_PWD"]).Should().Be("new-store");
        Encoding.UTF8.GetString(_vault["KEYSTORE_release_KEYPWD"]).Should().Be("new-key");
    }

    [Fact]
    public async Task ListKeystoresAsync_MovesOlderKeystoresWhoseFileExistsIntoTheVault()
    {
        var path = Path.Combine(_directory, "release.jks");
        await File.WriteAllBytesAsync(path, new byte[] { 7, 8, 9 });
        await WriteLegacyRegistryAsync(Keystore(filePath: path));
        _secureStorage["android_keystore_pwd_ks-1"] = "store-pw";
        _secureStorage["android_keystore_keypwd_ks-1"] = "key-pw";

        var listed = (await _service.ListKeystoresAsync()).Should().ContainSingle().Subject;

        listed.Id.Should().Be("ks-1");
        listed.StoredInVault.Should().BeTrue();
        listed.FilePath.Should().Be("release.jks");
        _vault["KEYSTORE_release_JKS"].Should().Equal(7, 8, 9);
        Encoding.UTF8.GetString(_vault["KEYSTORE_release_PWD"]).Should().Be("store-pw");
        Encoding.UTF8.GetString(_vault["KEYSTORE_release_KEYPWD"]).Should().Be("key-pw");
        _secureStorage.Should().BeEmpty();
        JsonSerializer.Deserialize<List<AndroidKeystore>>(
            await File.ReadAllTextAsync(Path.Combine(_directory, "android-keystores.json")))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task ListKeystoresAsync_KeepsOlderKeystoreWhoseFileIsMissing()
    {
        await WriteLegacyRegistryAsync(Keystore(filePath: Path.Combine(_directory, "gone.jks")));
        _secureStorage["android_keystore_pwd_ks-1"] = "store-pw";

        var listed = (await _service.ListKeystoresAsync()).Should().ContainSingle().Subject;

        listed.StoredInVault.Should().BeFalse();
        (await _service.GetKeystoreContentAsync("ks-1")).Should().BeNull();
        (await _service.GetKeystorePasswordAsync("ks-1")).Should().Be("store-pw");
        _vault.Should().BeEmpty();
    }

    [Fact]
    public async Task ListKeystoresAsync_AdoptsAVaultCopyForAnOlderKeystoreWhoseFileIsMissing()
    {
        // A copy synced into the vault by an earlier version: no ID in its metadata.
        _vault["KEYSTORE_release_JKS"] = new byte[] { 5, 5 };
        _vault["KEYSTORE_release_PWD"] = Encoding.UTF8.GetBytes("vault-pw");
        _vault["KEYSTORE_release_META"] = Encoding.UTF8.GetBytes(
            """{"Alias":"release","KeystoreType":"PKCS12","CreatedDate":"2025-01-01T00:00:00Z","Notes":null,"UploadedAt":"2025-01-01T00:00:00Z"}""");
        await WriteLegacyRegistryAsync(Keystore(filePath: Path.Combine(_directory, "gone.jks")));
        _secureStorage["android_keystore_keypwd_ks-1"] = "key-pw";

        var listed = (await _service.ListKeystoresAsync()).Should().ContainSingle().Subject;

        listed.Id.Should().Be("ks-1");
        listed.StoredInVault.Should().BeTrue();
        _vault["KEYSTORE_release_JKS"].Should().Equal(5, 5);
        Encoding.UTF8.GetString(_vault["KEYSTORE_release_PWD"]).Should().Be("vault-pw");
        Encoding.UTF8.GetString(_vault["KEYSTORE_release_KEYPWD"]).Should().Be("key-pw");
    }

    [Fact]
    public async Task ListKeystoresAsync_GivesAVaultCopyWithoutAnIdAStableId()
    {
        _vault["KEYSTORE_upload_JKS"] = new byte[] { 1 };

        var first = (await _service.ListKeystoresAsync()).Single();
        var second = (await _service.ListKeystoresAsync()).Single();

        first.Id.Should().Be("vault-upload").And.Be(second.Id);
        first.StoredInVault.Should().BeTrue();
    }

    [Fact]
    public async Task OpenKeystoreFileAsync_WritesATemporaryCopyThatIsDeletedOnDispose()
    {
        await _service.AddKeystoreAsync(Keystore(), new byte[] { 3, 1, 4 }, "pw", "pw");

        string path;
        await using (var file = await _service.OpenKeystoreFileAsync("ks-1"))
        {
            path = file.Path;
            Path.GetFileName(path).Should().Be("release.jks");
            (await File.ReadAllBytesAsync(path)).Should().Equal(3, 1, 4);
        }

        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task ExportKeystoreFileAsync_WritesTheKeystoreToTheChosenPath()
    {
        await _service.AddKeystoreAsync(Keystore(), new byte[] { 2, 7 }, "pw", "pw");
        var destination = Path.Combine(_directory, "exported.jks");

        await _service.ExportKeystoreFileAsync("ks-1", destination);

        (await File.ReadAllBytesAsync(destination)).Should().Equal(2, 7);
    }

    [Fact]
    public async Task RemoveKeystoreAsync_DeletesTheKeystoreFromTheVault()
    {
        await _service.AddKeystoreAsync(Keystore(), new byte[] { 1 }, "pw", "pw");

        await _service.RemoveKeystoreAsync("ks-1");

        _vault.Should().BeEmpty();
        (await _service.ListKeystoresAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData(LocalVaultIntroductionDecision.Declined)]
    [InlineData(LocalVaultIntroductionDecision.NotSet)]
    public async Task FileAttachedKeystores_StayOnDiskAndWorkWhenTheLocalVaultIsNotOn(LocalVaultIntroductionDecision decision)
    {
        var service = CreateService(decision);
        var path = Path.Combine(_directory, "release.jks");
        await File.WriteAllBytesAsync(path, new byte[] { 7, 8, 9 });
        await WriteLegacyRegistryAsync(Keystore(filePath: path));
        _secureStorage["android_keystore_pwd_ks-1"] = "store-pw";

        var listed = (await service.ListKeystoresAsync()).Should().ContainSingle().Subject;

        listed.StoredInVault.Should().BeFalse();
        listed.FilePath.Should().Be(path);
        _vault.Should().BeEmpty();
        (await service.GetKeystoreContentAsync("ks-1")).Should().Equal(7, 8, 9);
        (await service.GetKeystorePasswordAsync("ks-1")).Should().Be("store-pw");

        await service.SaveKeyPasswordAsync("ks-1", "key-pw");
        _secureStorage["android_keystore_keypwd_ks-1"].Should().Be("key-pw");

        await using (var file = await service.OpenKeystoreFileAsync("ks-1"))
            (await File.ReadAllBytesAsync(file.Path)).Should().Equal(7, 8, 9);

        await service.RemoveKeystoreAsync("ks-1");
        File.Exists(path).Should().BeTrue();
        (await service.ListKeystoresAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task AddKeystoreAsync_AttachesTheKeystoresOwnFileWhenTheLocalVaultIsDeclined()
    {
        var service = CreateService(LocalVaultIntroductionDecision.Declined);
        var path = Path.Combine(_directory, "release.jks");
        await File.WriteAllBytesAsync(path, new byte[] { 1, 2 });

        var stored = await service.AddKeystoreAsync(Keystore(filePath: path), new byte[] { 1, 2 }, "store-pw", "key-pw");

        stored.StoredInVault.Should().BeFalse();
        stored.FilePath.Should().Be(path);
        _vault.Should().BeEmpty();
        _secureStorage["android_keystore_pwd_ks-1"].Should().Be("store-pw");
        _secureStorage["android_keystore_keypwd_ks-1"].Should().Be("key-pw");
        Directory.Exists(Path.Combine(_directory, "keystores")).Should().BeFalse();
    }

    [Fact]
    public async Task AddKeystoreAsync_WritesAFileWhenTheLocalVaultIsDeclinedAndThereIsNone()
    {
        var service = CreateService(LocalVaultIntroductionDecision.Declined);
        var clash = Path.Combine(_directory, "keystores", "release.keystore");
        Directory.CreateDirectory(Path.GetDirectoryName(clash)!);
        await File.WriteAllBytesAsync(clash, new byte[] { 9 });

        var stored = await service.AddKeystoreAsync(
            Keystore(id: "abcdef123456", filePath: "release.keystore"), new byte[] { 4, 5 }, "pw", null);

        stored.FilePath.Should().Be(Path.Combine(_directory, "keystores", "release-abcdef12.keystore"));
        (await File.ReadAllBytesAsync(stored.FilePath)).Should().Equal(4, 5);
        (await File.ReadAllBytesAsync(clash)).Should().Equal(9);
    }

    [Fact]
    public async Task AddKeystoreAsync_RejectsADuplicateAliasAmongFileAttachedKeystores()
    {
        var service = CreateService(LocalVaultIntroductionDecision.Declined);
        await service.AddKeystoreAsync(Keystore(filePath: "release.keystore"), new byte[] { 1 }, "pw", "pw");

        var act = () => service.AddKeystoreAsync(Keystore(id: "ks-2", filePath: "other.keystore"), new byte[] { 2 }, "pw", "pw");

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*alias 'release' is already registered*");
    }

    [Fact]
    public async Task FileAttachedKeystores_MoveIntoTheVaultOnceItIsTurnedOn()
    {
        var path = Path.Combine(_directory, "release.jks");
        await File.WriteAllBytesAsync(path, new byte[] { 3 });
        await CreateService(LocalVaultIntroductionDecision.Declined)
            .AddKeystoreAsync(Keystore(filePath: path), new byte[] { 3 }, "pw", "key-pw");

        var listed = (await CreateService(LocalVaultIntroductionDecision.Enabled).ListKeystoresAsync()).Single();

        listed.StoredInVault.Should().BeTrue();
        listed.Id.Should().Be("ks-1");
        _vault["KEYSTORE_release_JKS"].Should().Equal(3);
        Encoding.UTF8.GetString(_vault["KEYSTORE_release_KEYPWD"]).Should().Be("key-pw");
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task Adapter_ReadsFileAttachedKeystoresAsTheLocalSyncSource()
    {
        var service = CreateService(LocalVaultIntroductionDecision.Declined);
        var path = Path.Combine(_directory, "release.jks");
        await File.WriteAllBytesAsync(path, new byte[] { 6, 6 });
        await service.AddKeystoreAsync(Keystore(filePath: path), new byte[] { 6, 6 }, "store-pw", "key-pw");
        var adapter = new AndroidKeystoreItemAdapter(_registry.Object, service);

        var item = (await adapter.ListLocalItemsAsync()).Should().ContainSingle().Subject;
        var payload = await adapter.ReadLocalAsync(item);

        payload!.Artifacts.Select(a => a.Key).Should().BeEquivalentTo(
            "KEYSTORE_release_JKS", "KEYSTORE_release_PWD", "KEYSTORE_release_KEYPWD", "KEYSTORE_release_META");
        payload.Artifacts.Single(a => a.Key == "KEYSTORE_release_JKS").Value.Should().Equal(6, 6);
    }

    [Fact]
    public async Task Adapter_HasNoLocalSourceForKeystoresInTheVault()
    {
        await _service.AddKeystoreAsync(Keystore(), new byte[] { 1 }, "pw", "pw");
        var adapter = new AndroidKeystoreItemAdapter(_registry.Object, _service);

        (await adapter.ListLocalItemsAsync()).Should().BeEmpty();
        (await adapter.ReadLocalAsync(new SecretItemRef(SecretItemKind.AndroidKeystore, "release", "release"))).Should().BeNull();
    }
}
