using System.CommandLine;
using System.CommandLine.Parsing;
using MauiSherpa.Cli.Commands;
using MauiSherpa.Cli.Commands.Android;
using MauiSherpa.Cli.Commands.Apple;
using MauiSherpa.Cli.Commands.Workloads;
using MauiSherpa.Cli.Helpers;

namespace MauiSherpa.Cli;

public static class Program
{
    public static RootCommand CreateRootCommand() =>
        new("MAUI Sherpa CLI — manage mobile toolchains and guide encrypted Expedition Packs through CI.\n\nDesigned for discoverability by AI code agents.\n\nAI AGENTS: Always pass --agent to get structured remediation prompts when issues are found.\nExample: maui-sherpa doctor --agent\n\nUse 'maui-sherpa features' to list all capabilities as JSON.")
        {
            FeaturesCommand.Create(),
            VersionCommand.Create(),
            DoctorCommand.Create(),
            AndroidCommand.Create(),
            AppleCommand.Create(),
            WorkloadsCommand.Create(),
            BundleCommand.Create(),
            CliOptions.Json,
            CliOptions.Agent,
        };

    public static async Task<int> InvokeAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var root = CreateRootCommand();
        var parseResult = root.Parse(args);
        if (parseResult.Errors.Count > 0)
        {
            WriteParseErrors(parseResult);
            return 2;
        }

        try
        {
            return await parseResult.InvokeAsync(
                parseResult.InvocationConfiguration ?? new InvocationConfiguration(),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (ShouldUseStructuredOutput(parseResult))
            {
                Output.WriteJson(new
                {
                    status = "error",
                    command = GetCommandPath(parseResult),
                    exitCode = 130,
                    error = new
                    {
                        message = "Command execution was cancelled."
                    }
                });
            }
            else
            {
                Output.WriteError("Command execution was cancelled.");
            }

            return 130;
        }
    }

    public static Task<int> Main(string[] args) => InvokeAsync(args);

    private static void WriteParseErrors(ParseResult parseResult)
    {
        var errors = parseResult.Errors.Select(error => error.Message).ToArray();
        if (ShouldUseStructuredOutput(parseResult))
        {
            Output.WriteJson(new
            {
                status = "error",
                command = GetCommandPath(parseResult),
                exitCode = 2,
                errors,
            });
            return;
        }

        foreach (var error in errors)
            Output.WriteError(error);
        Output.WriteInfo("Use --help for usage information.");
    }

    private static bool ShouldUseStructuredOutput(ParseResult parseResult) =>
        parseResult.GetValue(CliOptions.Json) || parseResult.GetValue(CliOptions.Agent);

    private static string GetCommandPath(ParseResult parseResult)
    {
        var names = new Stack<string>();
        for (var current = parseResult.CommandResult; current is not null; current = current.Parent as CommandResult)
        {
            if (current.Command is RootCommand)
                continue;
            if (!string.IsNullOrWhiteSpace(current.Command.Name))
                names.Push(current.Command.Name);
        }

        return names.Count > 0 ? string.Join('.', names) : "root";
    }
}
