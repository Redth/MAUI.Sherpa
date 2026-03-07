using System.CommandLine;
using System.Reflection;
using MauiSherpa.Cli.Helpers;

namespace MauiSherpa.Cli.Commands;

public static class VersionCommand
{
    public static Command Create()
    {
        var cmd = new Command("version", "Show MAUI Sherpa CLI version information.");
        cmd.SetAction((parseResult) =>
        {
            var json = parseResult.GetValue(CliOptions.Json);
            var asm = Assembly.GetExecutingAssembly();
            var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                          ?? asm.GetName().Version?.ToString()
                          ?? "unknown";

            if (json)
                Output.WriteJson(new { tool = "maui-sherpa", version });
            else
                Console.WriteLine($"maui-sherpa {version}");
        });
        return cmd;
    }
}
