using FluentAssertions;
using MauiSherpa.Core.Services;

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
}
