using FluentAssertions;

namespace MauiSherpa.Bundles.Tests;

public class SherpaPackTextTests
{
    [Fact]
    public void EncodeDecode_RoundTripsPackBytes()
    {
        var bytes = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();

        var encoded = SherpaPackText.Encode(bytes);
        var decoded = SherpaPackText.Decode(encoded);

        encoded.Should().StartWith("spk2.");
        encoded.Should().NotContain("+").And.NotContain("/").And.NotEndWith("=");
        decoded.Should().Equal(bytes);
    }

    [Fact]
    public void Split_SinglePart_UsesBaseName()
    {
        var parts = SherpaPackText.Split([1, 2, 3], maximumValueLength: 200);

        parts.Should().ContainSingle();
        parts[0].Name.Should().Be("SHERPA_PACK");
        parts[0].Value.Should().StartWith("spk2.");
    }

    [Fact]
    public void SplitAndAssemble_MultipleParts_RoundTrips()
    {
        var bytes = Enumerable.Range(0, 4096).Select(value => (byte)(value % 251)).ToArray();
        var parts = SherpaPackText.Split(bytes, maximumValueLength: 300);
        var environment = parts.ToDictionary(part => part.Name, part => part.Value);

        var assembled = SherpaPackText.AssembleFromEnvironment(
            name => environment.GetValueOrDefault(name));

        parts.Should().HaveCountGreaterThan(1);
        parts.Should().OnlyContain(part => part.Value.Length <= 300);
        assembled.Should().Equal(bytes);
    }

    [Fact]
    public void Assemble_MissingMiddlePart_FailsClearly()
    {
        var bytes = Enumerable.Range(0, 4096).Select(value => (byte)(value % 251)).ToArray();
        var parts = SherpaPackText.Split(bytes, maximumValueLength: 300);
        var environment = parts.ToDictionary(part => part.Name, part => part.Value);
        environment.Remove("SHERPA_PACK_2");

        var act = () => SherpaPackText.AssembleFromEnvironment(
            name => environment.GetValueOrDefault(name));

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*SHERPA_PACK_2*");
    }

    [Fact]
    public void Assemble_TamperedPart_FailsDigest()
    {
        var bytes = Enumerable.Range(0, 4096).Select(value => (byte)(value % 251)).ToArray();
        var parts = SherpaPackText.Split(bytes, maximumValueLength: 300);
        var environment = parts.ToDictionary(part => part.Name, part => part.Value);
        environment["SHERPA_PACK_2"] = environment["SHERPA_PACK_2"][..^1] + "A";

        var act = () => SherpaPackText.AssembleFromEnvironment(
            name => environment.GetValueOrDefault(name));

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*digest*");
    }
}
