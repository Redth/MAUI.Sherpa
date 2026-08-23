using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public sealed class ProvisioningProfileDownloadManagerTests
{
    [Fact]
    public async Task PlanAsync_ReplacesOlderCopiesOfSameProfile()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var older = CreateProfile(
                "Profile",
                "OLD-UUID",
                "com.example.app",
                new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var incoming = CreateProfile(
                "Profile",
                "NEW-UUID",
                "com.example.app",
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var canonicalPath = Path.Combine(directory.FullName, "Profile.mobileprovision");
            var numberedPath = Path.Combine(directory.FullName, "Profile (1).mobileprovision");
            await File.WriteAllBytesAsync(canonicalPath, older);
            await File.WriteAllBytesAsync(numberedPath, older);

            var plan = await ProvisioningProfileDownloadManager.PlanAsync(
                directory.FullName,
                "Profile.mobileprovision",
                CreateAppleProfile(),
                incoming,
                new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

            plan.RequiresConfirmation.Should().BeFalse();
            plan.AutomaticReplacementPaths.Should().BeEquivalentTo(canonicalPath, numberedPath);

            await ProvisioningProfileDownloadManager.SaveAsync(plan, incoming, replaceConfirmedConflicts: false);

            Directory.GetFiles(directory.FullName, "*.mobileprovision")
                .Should().ContainSingle()
                .Which.Should().Be(canonicalPath);
            (await File.ReadAllBytesAsync(canonicalPath)).Should().Equal(incoming);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PlanAsync_RequiresConfirmationForUnrelatedExactName()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var unrelated = CreateProfile(
                "Profile",
                "OTHER-UUID",
                "com.example.other",
                new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var incoming = CreateProfile(
                "Profile",
                "NEW-UUID",
                "com.example.app",
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var existingPath = Path.Combine(directory.FullName, "Profile.mobileprovision");
            await File.WriteAllBytesAsync(existingPath, unrelated);

            var plan = await ProvisioningProfileDownloadManager.PlanAsync(
                directory.FullName,
                "Profile.mobileprovision",
                CreateAppleProfile(),
                incoming);

            plan.Conflict.Should().Be(ProvisioningProfileDownloadConflict.ExistingFile);
            plan.ConfirmedReplacementPaths.Should().ContainSingle().Which.Should().Be(existingPath);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PlanAsync_RequiresConfirmationForCaseOnlyExactNameCollision()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows())
            return;

        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var unrelated = CreateProfile(
                "Profile",
                "OTHER-UUID",
                "com.example.other",
                new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var incoming = CreateProfile(
                "Profile",
                "NEW-UUID",
                "com.example.app",
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
            await File.WriteAllBytesAsync(
                Path.Combine(directory.FullName, "profile.mobileprovision"),
                unrelated);

            var plan = await ProvisioningProfileDownloadManager.PlanAsync(
                directory.FullName,
                "Profile.mobileprovision",
                CreateAppleProfile(),
                incoming);

            plan.Conflict.Should().Be(ProvisioningProfileDownloadConflict.ExistingFile);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PlanAsync_RequiresConfirmationBeforeReplacingNewerCopy()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var newer = CreateProfile(
                "Profile",
                "NEWER-UUID",
                "com.example.app",
                new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2027, 6, 1, 0, 0, 0, TimeSpan.Zero));
            var incoming = CreateProfile(
                "Profile",
                "INCOMING-UUID",
                "com.example.app",
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
            await File.WriteAllBytesAsync(
                Path.Combine(directory.FullName, "Profile.mobileprovision"),
                newer);

            var plan = await ProvisioningProfileDownloadManager.PlanAsync(
                directory.FullName,
                "Profile.mobileprovision",
                CreateAppleProfile(),
                incoming);

            plan.Conflict.Should().Be(ProvisioningProfileDownloadConflict.NewerProfile);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PlanAsync_RequiresConfirmationForDifferentProfileKind()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var directProfile = CreateProfile(
                "Profile",
                "DIRECT-UUID",
                "com.example.app",
                new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
                provisionsAllDevices: true);
            var appStoreProfile = CreateProfile(
                "Profile",
                "STORE-UUID",
                "com.example.app",
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
            await File.WriteAllBytesAsync(
                Path.Combine(directory.FullName, "Profile.mobileprovision"),
                directProfile);

            var plan = await ProvisioningProfileDownloadManager.PlanAsync(
                directory.FullName,
                "Profile.mobileprovision",
                CreateAppleProfile(),
                appStoreProfile);

            plan.Conflict.Should().Be(ProvisioningProfileDownloadConflict.ExistingFile);
            plan.AutomaticReplacementPaths.Should().BeEmpty();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_RejectsFileChangedAfterPlan()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var older = CreateProfile(
                "Profile",
                "OLD-UUID",
                "com.example.app",
                new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var incoming = CreateProfile(
                "Profile",
                "NEW-UUID",
                "com.example.app",
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var targetPath = Path.Combine(directory.FullName, "Profile.mobileprovision");
            await File.WriteAllBytesAsync(targetPath, older);

            var plan = await ProvisioningProfileDownloadManager.PlanAsync(
                directory.FullName,
                "Profile.mobileprovision",
                CreateAppleProfile(),
                incoming);
            await File.WriteAllTextAsync(targetPath, "changed after review");

            var action = () => ProvisioningProfileDownloadManager.SaveAsync(
                plan,
                incoming,
                replaceConfirmedConflicts: false);

            await action.Should().ThrowAsync<IOException>()
                .WithMessage("*changed after the replacement review*");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static AppleProfile CreateAppleProfile() => new(
        "profile-id",
        "Profile",
        "IOS_APP_STORE",
        "IOS",
        "ACTIVE",
        new DateTime(2027, 1, 1),
        "com.example.app",
        "NEW-UUID");

    private static byte[] CreateProfile(
        string name,
        string uuid,
        string bundleId,
        DateTimeOffset creationDate,
        DateTimeOffset expirationDate,
        bool provisionsAllDevices = false)
    {
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
                <key>Entitlements</key>
                <dict>
                    <key>application-identifier</key><string>TEAMID.{{bundleId}}</string>
                </dict>
            </dict>
            </plist>
            """;

        var content = new ContentInfo(Encoding.UTF8.GetBytes(xml));
        var signedCms = new SignedCms(content);
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Provisioning Profile Test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        signedCms.ComputeSignature(new CmsSigner(certificate));
        return signedCms.Encode();
    }
}
