using FluentAssertions;
using MauiSherpa.Bundle.Pipeline;
using MauiSherpa.Bundle.Steps;

namespace MauiSherpa.Bundle.Tests;

public class TokenReplacerTests : IDisposable
{
    private readonly string _dir;

    public TokenReplacerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "sherpa-tok-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Replaces_known_tokens_and_leaves_unknown()
    {
        var file = Write("appsettings.json", """{ "Greeting": "${Hello}", "Other": "${Unknown}" }""");

        var modified = TokenReplacer.Apply(_dir, new Dictionary<string, string> { ["Hello"] = "World" }, NullSherpaLog.Instance);

        modified.Should().Be(1);
        File.ReadAllText(file).Should().Be("""{ "Greeting": "World", "Other": "${Unknown}" }""");
    }

    [Fact]
    public void Skips_build_output_directories()
    {
        var src = Write("Config.cs", "var x = \"${Token}\";");
        var inBin = Write(Path.Combine("bin", "Config.cs"), "var x = \"${Token}\";");

        TokenReplacer.Apply(_dir, new Dictionary<string, string> { ["Token"] = "VALUE" }, NullSherpaLog.Instance);

        File.ReadAllText(src).Should().Contain("VALUE");
        File.ReadAllText(inBin).Should().Contain("${Token}"); // untouched
    }

    [Fact]
    public void No_tokens_is_a_noop()
    {
        var file = Write("a.txt", "${Hello}");
        TokenReplacer.Apply(_dir, new Dictionary<string, string>(), NullSherpaLog.Instance).Should().Be(0);
        File.ReadAllText(file).Should().Be("${Hello}");
    }
}
