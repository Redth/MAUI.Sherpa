using System.Diagnostics;
using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class KeystoreServiceTests
{
    [Theory]
    [InlineData("JKS", "\n")]
    [InlineData("PKCS12", "\r\n")]
    public void ParseKeystoreInfo_ReadsActualTypeAndKeyAlias(string type, string newline)
    {
        var output = string.Join(newline,
            $"Keystore type: {type}",
            "Keystore provider: SUN",
            "",
            "Your keystore contains 1 entry",
            "",
            "Alias name: androiddebugkey",
            "Creation date: Oct 23, 2026",
            "Entry type: PrivateKeyEntry",
            "Certificate chain length: 1");

        var result = KeystoreService.ParseKeystoreInfo(output);

        result.KeystoreType.Should().Be(type);
        result.Aliases.Should().Equal("androiddebugkey");
    }

    [Fact]
    public void ParseKeystoreInfo_ReturnsAllPrivateKeyAliasesWithoutTrustedCertificates()
    {
        var output = """
            Keystore type: JKS
            Keystore provider: SUN

            Your keystore contains 3 entries

            Alias name: first-key
            Creation date: Oct 23, 2026
            Entry type: PrivateKeyEntry
            Certificate chain length: 1

            Alias name: trusted-ca
            Creation date: Oct 23, 2026
            Entry type: trustedCertEntry

            Alias name: second-key
            Creation date: Oct 23, 2026
            Entry type: PrivateKeyEntry
            Certificate chain length: 1
            """;

        var result = KeystoreService.ParseKeystoreInfo(output);

        result.Aliases.Should().Equal("first-key", "second-key");
    }

    [Theory]
    [InlineData("release key")]
    [InlineData("key\"with\"quotes")]
    [InlineData("Cancel")]
    [InlineData(" leading and trailing spaces ")]
    [InlineData("cl\u00e9")]
    public void ParseKeystoreInfo_PreservesAlias(string alias)
    {
        var output = $"Keystore type: PKCS12\nAlias name: {alias}\nEntry type: PrivateKeyEntry\n";

        KeystoreService.ParseKeystoreInfo(output).Aliases.Should().Equal(alias);
    }

    [Theory]
    [InlineData("Keystore type: JKS\nYour keystore contains 0 entries")]
    [InlineData("Keystore type: PKCS12\nAlias name: certificate\nEntry type: trustedCertEntry")]
    [InlineData("Keystore type: JKS\nAlias name: secret\nEntry type: SecretKeyEntry")]
    [InlineData("Keystore type: JKS\nAlias name: \nEntry type: PrivateKeyEntry")]
    public void ParseKeystoreInfo_RejectsKeystoreWithoutSigningKeys(string output)
    {
        var act = () => KeystoreService.ParseKeystoreInfo(output);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not contain any private key entries*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Alias name: my-key\nEntry type: PrivateKeyEntry")]
    [InlineData("Keystore type: \nAlias name: my-key\nEntry type: PrivateKeyEntry")]
    public void ParseKeystoreInfo_RejectsMissingKeystoreType(string output)
    {
        var act = () => KeystoreService.ParseKeystoreInfo(output);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Could not read the keystore type*");
    }

    [Fact]
    public async Task VerifyKeyPasswordAsync_ChecksJksKeyPasswordAndAcceptsPkcs12()
    {
        // Runs against a real keytool; there is nothing to check on a machine without a JDK.
        var jdk = FindJdk();
        if (jdk is null)
            return;

        var jdkSettings = new Mock<IOpenJdkSettingsService>();
        jdkSettings.Setup(x => x.GetEffectiveJdkPathAsync()).ReturnsAsync(jdk);
        var service = new KeystoreService(jdkSettings.Object, Mock.Of<ISecureStorageService>(), Mock.Of<ILoggingService>());
        var keytool = (await service.GetKeytoolPathAsync())!;

        var dir = Path.Combine(Path.GetTempPath(), "sherpa-keystore-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var jks = Path.Combine(dir, "release.jks");
            var p12 = Path.Combine(dir, "release.p12");
            GenerateKey(keytool, jks, "JKS", "store-pw", "key-pw1");
            GenerateKey(keytool, p12, "PKCS12", "store-pw", "store-pw");

            (await service.VerifyKeyPasswordAsync(jks, "release", "store-pw", "key-pw1")).Should().BeTrue();
            (await service.VerifyKeyPasswordAsync(jks, "release", "store-pw", "store-pw")).Should().BeFalse();
            // keytool ignores -keypass for PKCS12, whose key password is the keystore password.
            (await service.VerifyKeyPasswordAsync(p12, "release", "store-pw", "anything")).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string? FindJdk()
    {
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome) && File.Exists(Path.Combine(javaHome, "bin", OperatingSystem.IsWindows() ? "keytool.exe" : "keytool")))
            return javaHome;
        if (!OperatingSystem.IsMacOS() || !File.Exists("/usr/libexec/java_home"))
            return null;

        using var process = Process.Start(new ProcessStartInfo("/usr/libexec/java_home")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        var path = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return process.ExitCode == 0 && File.Exists(Path.Combine(path, "bin", "keytool")) ? path : null;
    }

    private static void GenerateKey(string keytool, string path, string type, string storePassword, string keyPassword)
    {
        var psi = new ProcessStartInfo(keytool) { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        foreach (var argument in new[]
        {
            "-genkeypair", "-keystore", path, "-storetype", type, "-alias", "release",
            "-keyalg", "RSA", "-keysize", "2048", "-validity", "1",
            "-storepass", storePassword, "-keypass", keyPassword, "-dname", "CN=Test"
        })
            psi.ArgumentList.Add(argument);
        using var process = Process.Start(psi)!;
        process.StandardInput.Close();
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0);
    }
}
