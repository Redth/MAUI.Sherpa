using System.CommandLine;

namespace MauiSherpa.Cli;

public static class CliOptions
{
    public static readonly Option<bool> Json = new("--json", "-j")
    {
        Description = "Output results as JSON for machine consumption",
        Recursive = true,
    };
}
