namespace MauiSherpa.Bundle.Pipeline;

/// <summary>Progress sink for the pipeline, so the engine stays decoupled from the console.</summary>
public interface ISherpaLog
{
    void Info(string message);
    void Step(string message);
    void Success(string message);
    void Warn(string message);
    void Error(string message);
}

/// <summary>Discards all messages (used in tests and <c>--json</c> mode).</summary>
public sealed class NullSherpaLog : ISherpaLog
{
    public static readonly NullSherpaLog Instance = new();
    public void Info(string message) { }
    public void Step(string message) { }
    public void Success(string message) { }
    public void Warn(string message) { }
    public void Error(string message) { }
}
