using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public sealed class ProvisioningProfileCleanupAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsync_IdentifiesExpiredOlderAndMissingCertificateProfiles()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            using var installedCertificate = CreateCertificate("Installed");
            using var missingCertificate = CreateCertificate("Missing");
            var now = new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.Zero);

            await WriteProfileAsync(
                directory,
                "old.mobileprovision",
                CreateProfile(
                    installedCertificate,
                    "Example Distribution",
                    "OLD-UUID",
                    "com.example.app",
                    new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero)));
            await WriteProfileAsync(
                directory,
                "new.mobileprovision",
                CreateProfile(
                    installedCertificate,
                    "Example Distribution",
                    "NEW-UUID",
                    "com.example.app",
                    new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2027, 6, 1, 0, 0, 0, TimeSpan.Zero)));
            await WriteProfileAsync(
                directory,
                "expired.mobileprovision",
                CreateProfile(
                    installedCertificate,
                    "Expired Distribution",
                    "EXPIRED-UUID",
                    "com.example.expired",
                    new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)));
            await WriteProfileAsync(
                directory,
                "missing.mobileprovision",
                CreateProfile(
                    missingCertificate,
                    "Missing Certificate",
                    "MISSING-UUID",
                    "com.example.missing",
                    new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero)));

            var profiles = await ProvisioningProfileCleanupAnalyzer.AnalyzeAsync(
                [directory.FullName],
                [installedCertificate.SerialNumber],
                now);

            profiles.Single(profile => profile.Uuid == "OLD-UUID")
                .Should().Match<InstalledProvisioningProfileAssessment>(profile =>
                    profile.IsOlderVersion &&
                    profile.RecommendedForDeletion);
            profiles.Single(profile => profile.Uuid == "NEW-UUID")
                .RecommendedForDeletion.Should().BeFalse();
            profiles.Single(profile => profile.Uuid == "EXPIRED-UUID")
                .Should().Match<InstalledProvisioningProfileAssessment>(profile =>
                    profile.IsExpired &&
                    profile.RecommendedForDeletion);
            profiles.Single(profile => profile.Uuid == "MISSING-UUID")
                .Should().Match<InstalledProvisioningProfileAssessment>(profile =>
                    !profile.HasMatchingCertificate &&
                    profile.RecommendedForDeletion);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AnalyzeAsync_DoesNotTreatSameUuidInBothFoldersAsOlder()
    {
        var firstDirectory = Directory.CreateTempSubdirectory();
        var secondDirectory = Directory.CreateTempSubdirectory();
        try
        {
            using var certificate = CreateCertificate("Installed");
            var content = CreateProfile(
                certificate,
                "Example Distribution",
                "SAME-UUID",
                "com.example.app",
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
            await WriteProfileAsync(firstDirectory, "SAME-UUID.mobileprovision", content);
            await WriteProfileAsync(secondDirectory, "SAME-UUID.mobileprovision", content);

            var profiles = await ProvisioningProfileCleanupAnalyzer.AnalyzeAsync(
                [firstDirectory.FullName, secondDirectory.FullName],
                [certificate.SerialNumber],
                new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.Zero));

            profiles.Should().HaveCount(2);
            profiles.Should().OnlyContain(profile =>
                !profile.IsOlderVersion &&
                !profile.RecommendedForDeletion);
        }
        finally
        {
            firstDirectory.Delete(recursive: true);
            secondDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AnalyzeAsync_DoesNotTreatDifferentNamedProfilesAsVersions()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            using var certificate = CreateCertificate("Installed");
            await WriteProfileAsync(
                directory,
                "first.mobileprovision",
                CreateProfile(
                    certificate,
                    "Full Device Set",
                    "FIRST-UUID",
                    "com.example.app",
                    new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero)));
            await WriteProfileAsync(
                directory,
                "second.mobileprovision",
                CreateProfile(
                    certificate,
                    "QA Device Set",
                    "SECOND-UUID",
                    "com.example.app",
                    new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero)));

            var profiles = await ProvisioningProfileCleanupAnalyzer.AnalyzeAsync(
                [directory.FullName],
                [certificate.SerialNumber],
                new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.Zero));

            profiles.Should().OnlyContain(profile =>
                !profile.IsOlderVersion &&
                !profile.RecommendedForDeletion);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static async Task WriteProfileAsync(
        DirectoryInfo directory,
        string fileName,
        byte[] content)
    {
        await File.WriteAllBytesAsync(Path.Combine(directory.FullName, fileName), content);
    }

    private static X509Certificate2 CreateCertificate(string commonName)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={commonName}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));
    }

    private static byte[] CreateProfile(
        X509Certificate2 certificate,
        string name,
        string uuid,
        string bundleId,
        DateTimeOffset creationDate,
        DateTimeOffset expirationDate,
        bool provisionsAllDevices = false)
    {
        var certificateData = Convert.ToBase64String(certificate.RawData);
        var xml = $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>AppIDName</key><string>{{name}}</string>
                <key>ApplicationIdentifierPrefix</key><array><string>TEAMID</string></array>
                <key>Name</key><string>{{name}}</string>
                <key>CreationDate</key><date>{{creationDate.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}}</date>
                <key>Platform</key><array><string>iOS</string></array>
                <key>ExpirationDate</key><date>{{expirationDate.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}}</date>
                <key>TeamIdentifier</key><array><string>TEAMID</string></array>
                <key>UUID</key><string>{{uuid}}</string>
                <key>Version</key><integer>1</integer>
                {{(provisionsAllDevices ? "<key>ProvisionsAllDevices</key><true/>" : "")}}
                <key>DeveloperCertificates</key><array><data>{{certificateData}}</data></array>
                <key>Entitlements</key>
                <dict>
                    <key>application-identifier</key><string>TEAMID.{{bundleId}}</string>
                </dict>
            </dict>
            </plist>
            """;

        var signedCms = new SignedCms(new ContentInfo(Encoding.UTF8.GetBytes(xml)));
        signedCms.ComputeSignature(new CmsSigner(certificate));
        return signedCms.Encode();
    }
}
