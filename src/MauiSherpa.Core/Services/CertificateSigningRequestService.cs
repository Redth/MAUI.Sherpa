using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public static class CertificateSigningRequestService
{
    internal static (string RequestPem, string PrivateKeyPem) Generate(
        string commonName, string? email, string passphrase, bool useEc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);

        using var key = CreateKey(useEc);
        var request = CreateRequest(commonName, email, key);
        var encryption = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 200_000);
        return (request.CreateSigningRequestPem(), key.ExportEncryptedPkcs8PrivateKeyPem(passphrase, encryption));
    }

    private static AsymmetricAlgorithm CreateKey(bool useEc) =>
        useEc ? ECDsa.Create(ECCurve.NamedCurves.nistP256) : RSA.Create(2048);

    private static CertificateRequest CreateRequest(string commonName, string? email, AsymmetricAlgorithm key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commonName);
        var subject = new X500DistinguishedNameBuilder();
        subject.AddCommonName(commonName.Trim());
        if (!string.IsNullOrWhiteSpace(email))
            subject.AddEmailAddress(email.Trim());

        return key switch
        {
            ECDsa ec => new CertificateRequest(subject.Build(), ec, HashAlgorithmName.SHA256),
            RSA rsa => new CertificateRequest(subject.Build(), rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
            _ => throw new NotSupportedException("Unsupported certificate request key algorithm."),
        };
    }

    public static async Task SaveToKeychainAsync(
        string requestPath, string commonName, string? email, bool useEc, ILocalCertificateService keychain)
    {
        if (!keychain.IsSupported)
            throw new PlatformNotSupportedException("The local keychain is not available.");

        using var key = CreateKey(useEc);
        var requestPem = CreateRequest(commonName, email, key).CreateSigningRequestPem();
        // Keychain's OpenSSL importer accepts RSA PKCS#1 and EC SEC1, not raw PKCS#8.
        var privateKey = key is RSA rsa ? rsa.ExportRSAPrivateKey() : ((ECDsa)key).ExportECPrivateKey();
        try
        {
            await keychain.ImportPrivateKeyAsync(privateKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }

        try
        {
            await File.WriteAllTextAsync(requestPath, requestPem, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                "The request could not be saved. Its private key was retained in the login keychain. Choose another save location to try again.", ex);
        }
    }

    public static async Task<string> SaveAsync(
        string requestPath, string commonName, string? email, string passphrase, bool useEc)
    {
        var (requestPem, privateKeyPem) = Generate(commonName, email, passphrase, useEc);
        var privateKeyPath = Path.ChangeExtension(requestPath, ".key");
        if (string.Equals(Path.GetFullPath(requestPath), Path.GetFullPath(privateKeyPath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a .certSigningRequest file, not a .key file.", nameof(requestPath));

        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        // Never replace a key that may already belong to an issued certificate.
        await using (var stream = new FileStream(privateKeyPath, options))
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            await writer.WriteAsync(privateKeyPem);

        try
        {
            await File.WriteAllTextAsync(requestPath, requestPem, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                $"The request could not be saved. Its encrypted private key was retained at '{privateKeyPath}'. Choose a new filename to try again.", ex);
        }

        return privateKeyPath;
    }
}
