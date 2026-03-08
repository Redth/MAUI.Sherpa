using System.CommandLine;
using MauiSherpa.Cli;
using MauiSherpa.Cli.Commands;
using MauiSherpa.Cli.Commands.Android;
using MauiSherpa.Cli.Commands.Apple;
using MauiSherpa.Cli.Commands.Workloads;

var root = new RootCommand("MAUI Sherpa CLI — manage Android SDK, iOS simulators, keystores, .NET workloads, and more.\n\nDesigned for discoverability by AI code agents. Use 'maui-sherpa features' to list all capabilities as JSON.")
{
    FeaturesCommand.Create(),
    VersionCommand.Create(),
    DoctorCommand.Create(),
    AndroidCommand.Create(),
    AppleCommand.Create(),
    WorkloadsCommand.Create(),
    CliOptions.Json,
    CliOptions.Agent,
};

var config = new CommandLineConfiguration(root);
return await config.InvokeAsync(args);
