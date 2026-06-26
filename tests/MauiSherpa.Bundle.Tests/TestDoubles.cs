using MauiSherpa.Bundle.Pipeline;

namespace MauiSherpa.Bundle.Tests;

/// <summary>
/// An <see cref="IProcessRunner"/> that records every invocation and returns a
/// canned result instead of shelling out to a real toolchain — so the pipeline
/// can be driven end-to-end (load → setup → build) without running
/// <c>dotnet</c>/<c>security</c>/<c>xcodebuild</c>.
/// </summary>
public sealed class RecordingProcessRunner : IProcessRunner
{
    public sealed record Invocation(string FileName, IReadOnlyList<string> Arguments, string? WorkingDirectory);

    public List<Invocation> Invocations { get; } = new();

    /// <summary>Result returned for every <see cref="RunAsync"/> call (success by default).</summary>
    public ProcessResult Result { get; set; } = new(0, "", "");

    public Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        IDictionary<string, string>? environment = null,
        ISherpaLog? log = null,
        CancellationToken ct = default)
    {
        Invocations.Add(new Invocation(fileName, arguments.ToList(), workingDirectory));
        return Task.FromResult(Result);
    }

    public Task<string?> WhichAsync(string command, CancellationToken ct = default)
        => Task.FromResult<string?>("/usr/bin/" + command);

    /// <summary>The recorded invocation of <paramref name="fileName"/> (first match), or null.</summary>
    public Invocation? FirstOf(string fileName)
        => Invocations.FirstOrDefault(i => string.Equals(i.FileName, fileName, StringComparison.OrdinalIgnoreCase));

    /// <summary>All recorded invocations of <paramref name="fileName"/>.</summary>
    public IEnumerable<Invocation> AllOf(string fileName)
        => Invocations.Where(i => string.Equals(i.FileName, fileName, StringComparison.OrdinalIgnoreCase));
}
