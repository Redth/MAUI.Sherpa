using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class CertificateBundleUtilitiesTests
{
    [Fact]
    public void ExportSelectedIdentities_IncludesOnlySelectedPrivateKeys()
    {
        const string sourcePassword = "source-password";
        const string exportPassword = "export-password";
        var certificates = new[]
        {
            CreateCertificate("First"),
            CreateCertificate("Second"),
            CreateCertificate("Third")
        };

        try
        {
            var source = new X509Certificate2Collection(certificates);
            var sourceP12 = source.Export(X509ContentType.Pfx, sourcePassword)!;
            var selected = new[]
            {
                CreateIdentity(certificates[0]),
                CreateIdentity(certificates[2])
            };

            var result = CertificateBundleUtilities.ExportSelectedIdentities(
                sourceP12,
                sourcePassword,
                selected,
                exportPassword);

            var exported = X509CertificateLoader.LoadPkcs12Collection(
                result,
                exportPassword,
                X509KeyStorageFlags.DefaultKeySet);
            try
            {
                exported.Cast<X509Certificate2>().Should().HaveCount(2);
                exported.Cast<X509Certificate2>().Should().OnlyContain(certificate => certificate.HasPrivateKey);
                exported.Cast<X509Certificate2>()
                    .Select(certificate => certificate.Thumbprint)
                    .Should()
                    .BeEquivalentTo(certificates[0].Thumbprint, certificates[2].Thumbprint);
            }
            finally
            {
                foreach (var certificate in exported)
                    certificate.Dispose();
            }
        }
        finally
        {
            foreach (var certificate in certificates)
                certificate.Dispose();
        }
    }

    [Fact]
    public void ExportSelectedIdentities_WhenIdentityIsMissing_Throws()
    {
        const string password = "password";
        using var certificate = CreateCertificate("Available");
        var source = new X509Certificate2Collection(certificate);
        var sourceP12 = source.Export(X509ContentType.Pfx, password)!;
        var missingIdentity = new LocalSigningIdentity(
            "Missing",
            "Missing",
            null,
            "1234",
            null,
            true,
            "00112233445566778899AABBCCDDEEFF00112233");

        var action = () => CertificateBundleUtilities.ExportSelectedIdentities(
            sourceP12,
            password,
            [missingIdentity],
            password);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Only 0 of 1 selected signing identities*");
    }

    private static LocalSigningIdentity CreateIdentity(X509Certificate2 certificate) => new(
        certificate.GetNameInfo(X509NameType.SimpleName, false),
        certificate.GetNameInfo(X509NameType.SimpleName, false),
        null,
        certificate.SerialNumber,
        certificate.NotAfter,
        true,
        certificate.Thumbprint);

    private static X509Certificate2 CreateCertificate(string commonName)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={commonName}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));
    }
}
