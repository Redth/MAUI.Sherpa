using System.CommandLine;

namespace MauiSherpa.Cli;

public static class CliOptions
{
    public static readonly Option<bool> Json = new("--json", "-j")
    {
        Description = "Output results as JSON for machine consumption",
        Recursive = true,
    };

    public static readonly Option<bool> Agent = new("--agent")
    {
        Description = "Agent mode: when issues are found, output remediation prompts for the calling AI agent instead of starting an inner Copilot session",
        Recursive = true,
    };
}
