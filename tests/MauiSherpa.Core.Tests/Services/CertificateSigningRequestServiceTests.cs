using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class CertificateSigningRequestServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        AppContext.BaseDirectory, $"csr-tests-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generate_ProducesVerifiedRequestMatchingEncryptedPrivateKey(bool useEc)
    {
        var (requestPem, keyPem) = CertificateSigningRequestService.Generate(
            " Signing Key, Team ", " signing@example.com ", "test-passphrase", useEc);

        var request = CertificateRequest.LoadSigningRequestPem(
            requestPem, HashAlgorithmName.SHA256, signerSignaturePadding: RSASignaturePadding.Pkcs1);
        request.SubjectName.Name.Should().Contain("Signing Key, Team").And.Contain("signing@example.com");
        keyPem.Should().StartWith("-----BEGIN ENCRYPTED PRIVATE KEY-----");

        using AsymmetricAlgorithm key = useEc ? ECDsa.Create() : RSA.Create();
        key.ImportFromEncryptedPem(keyPem, "test-passphrase");
        key.KeySize.Should().Be(useEc ? 256 : 2048);
        key.ExportSubjectPublicKeyInfo().Should().Equal(request.PublicKey.ExportSubjectPublicKeyInfo());

        var wrongPassphrase = () => key.ImportFromEncryptedPem(keyPem, "wrong-passphrase");
        wrongPassphrase.Should().Throw<CryptographicException>();
    }

    [Fact]
    public async Task SaveAsync_WritesRequestAndProtectedKeyBesideIt()
    {
        Directory.CreateDirectory(_directory);
        var requestPath = Path.Combine(_directory, "developer-id.certSigningRequest");

        var keyPath = await CertificateSigningRequestService.SaveAsync(
            requestPath, "Signing Key", null, "test-passphrase", useEc: false);

        keyPath.Should().Be(Path.Combine(_directory, "developer-id.key"));
        var request = CertificateRequest.LoadSigningRequestPem(
            await File.ReadAllTextAsync(requestPath), HashAlgorithmName.SHA256);
        using var key = RSA.Create();
        key.ImportFromEncryptedPem(await File.ReadAllTextAsync(keyPath), "test-passphrase");
        key.ExportSubjectPublicKeyInfo().Should().Equal(request.PublicKey.ExportSubjectPublicKeyInfo());
        if (!OperatingSystem.IsWindows())
            File.GetUnixFileMode(keyPath).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public async Task SaveAsync_ExistingKeyDoesNotOverwriteEitherFile()
    {
        Directory.CreateDirectory(_directory);
        var requestPath = Path.Combine(_directory, "existing.certSigningRequest");
        var keyPath = Path.ChangeExtension(requestPath, ".key");
        await File.WriteAllTextAsync(keyPath, "existing private key");
        await File.WriteAllTextAsync(requestPath, "existing request");

        var action = () => CertificateSigningRequestService.SaveAsync(
            requestPath, "Signing Key", null, "test-passphrase", useEc: false);

        await action.Should().ThrowAsync<IOException>();
        (await File.ReadAllTextAsync(keyPath)).Should().Be("existing private key");
        (await File.ReadAllTextAsync(requestPath)).Should().Be("existing request");
    }

    [Fact]
    public async Task SaveAsync_RequestFailureRetainsKeyAndReportsItsLocation()
    {
        var requestPath = Path.Combine(_directory, "unwritable.certSigningRequest");
        Directory.CreateDirectory(requestPath);
        var keyPath = Path.ChangeExtension(requestPath, ".key");

        var action = () => CertificateSigningRequestService.SaveAsync(
            requestPath, "Signing Key", null, "test-passphrase", useEc: false);

        await action.Should().ThrowAsync<IOException>().WithMessage($"*retained at '{keyPath}'*");
        using var key = RSA.Create();
        key.ImportFromEncryptedPem(await File.ReadAllTextAsync(keyPath), "test-passphrase");
        key.KeySize.Should().Be(2048);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveToKeychainAsync_StoresMatchingKeyAndOnlySavesTheRequest(bool useEc)
    {
        Directory.CreateDirectory(_directory);
        var requestPath = Path.Combine(_directory, "keychain.certSigningRequest");
        var keychain = new Mock<ILocalCertificateService>(MockBehavior.Strict);
        keychain.SetupGet(service => service.IsSupported).Returns(true);
        byte[]? importedPublicKey = null;
        byte[]? privateKeyBuffer = null;
        keychain.Setup(service => service.ImportPrivateKeyAsync(It.IsAny<byte[]>()))
            .Callback<byte[]>(data =>
            {
                privateKeyBuffer = data;
                using AsymmetricAlgorithm key = useEc ? ECDsa.Create() : RSA.Create();
                int bytesRead;
                if (key is RSA rsa)
                    rsa.ImportRSAPrivateKey(data, out bytesRead);
                else
                    ((ECDsa)key).ImportECPrivateKey(data, out bytesRead);
                bytesRead.Should().Be(data.Length);
                key.KeySize.Should().Be(useEc ? 256 : 2048);
                importedPublicKey = key.ExportSubjectPublicKeyInfo();
            })
            .Returns(Task.CompletedTask);

        await CertificateSigningRequestService.SaveToKeychainAsync(
            requestPath, "Keychain Signing Key", null, useEc, keychain.Object);

        var request = CertificateRequest.LoadSigningRequestPem(
            await File.ReadAllTextAsync(requestPath), HashAlgorithmName.SHA256);
        request.PublicKey.ExportSubjectPublicKeyInfo().Should().Equal(importedPublicKey!);
        Directory.GetFiles(_directory).Should().Equal(requestPath);
        privateKeyBuffer.Should().NotBeNull().And.OnlyContain(value => value == 0);
        keychain.Verify(service => service.ImportPrivateKeyAsync(It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public async Task SaveToKeychainAsync_ImportFailureDoesNotSaveAnUnusableRequest()
    {
        Directory.CreateDirectory(_directory);
        var requestPath = Path.Combine(_directory, "failed.certSigningRequest");
        var keychain = new Mock<ILocalCertificateService>();
        keychain.SetupGet(service => service.IsSupported).Returns(true);
        byte[]? privateKeyBuffer = null;
        keychain.Setup(service => service.ImportPrivateKeyAsync(It.IsAny<byte[]>()))
            .Callback<byte[]>(data => privateKeyBuffer = data)
            .ThrowsAsync(new CryptographicException("Keychain import failed"));

        var action = () => CertificateSigningRequestService.SaveToKeychainAsync(
            requestPath, "Signing Key", null, useEc: false, keychain.Object);

        await action.Should().ThrowAsync<CryptographicException>().WithMessage("Keychain import failed");
        File.Exists(requestPath).Should().BeFalse();
        privateKeyBuffer.Should().NotBeNull().And.OnlyContain(value => value == 0);
    }

    [Fact]
    public async Task SaveToKeychainAsync_RequestFailureExplainsThatKeyWasRetained()
    {
        var requestPath = Path.Combine(_directory, "unwritable.certSigningRequest");
        Directory.CreateDirectory(requestPath);
        var keychain = new Mock<ILocalCertificateService>();
        keychain.SetupGet(service => service.IsSupported).Returns(true);
        keychain.Setup(service => service.ImportPrivateKeyAsync(It.IsAny<byte[]>()))
            .Returns(Task.CompletedTask);

        var action = () => CertificateSigningRequestService.SaveToKeychainAsync(
            requestPath, "Signing Key", null, useEc: false, keychain.Object);

        await action.Should().ThrowAsync<IOException>().WithMessage("*retained in the login keychain*");
        keychain.Verify(service => service.ImportPrivateKeyAsync(It.IsAny<byte[]>()), Times.Once);
    }

    [Theory]
    [InlineData("", "passphrase")]
    [InlineData("Signing Key", " ")]
    public void Generate_RejectsMissingNameOrPassphrase(string name, string passphrase)
    {
        var action = () => CertificateSigningRequestService.Generate(name, null, passphrase, useEc: false);

        action.Should().Throw<ArgumentException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
