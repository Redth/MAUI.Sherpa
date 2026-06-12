using MauiSherpa.Bundle.Pipeline;

namespace MauiSherpa.Bundle.Cli;

/// <summary>Writes pipeline progress to the console. Diagnostics go to stderr so stdout stays clean for the JSON result.</summary>
public sealed class ConsoleLog : ISherpaLog
{
    private readonly bool _quiet;

    public ConsoleLog(bool quiet) => _quiet = quiet;

    public void Info(string message) { if (!_quiet) Console.Error.WriteLine($"  {message}"); }
    public void Step(string message) { if (!_quiet) Console.Error.WriteLine($"▸ {message}"); }
    public void Success(string message) { if (!_quiet) Console.Error.WriteLine($"✓ {message}"); }
    public void Warn(string message) { if (!_quiet) Console.Error.WriteLine($"⚠ {message}"); }
    public void Error(string message) => Console.Error.WriteLine($"✗ {message}");
}
