using System.CommandLine;
using MauiSherpa.Cli.Helpers;

namespace MauiSherpa.Cli.Commands.Apple;

public static class XcodeCommand
{
    public static Command Create()
    {
        var cmd = new Command("xcode", "Show Xcode installation path, version, and build number.");
        cmd.SetAction(async (parseResult, ct) =>
        {
            var json = parseResult.GetValue(CliOptions.Json);
            await HandleAsync(json);
        });
        return cmd;
    }

    private static async Task HandleAsync(bool json)
    {
        if (!OperatingSystem.IsMacOS())
        {
            Output.WriteError("Xcode is only available on macOS.");
            return;
        }

        var pathResult = await ProcessRunner.RunAsync("xcode-select", "-p");
        if (pathResult.ExitCode != 0)
        {
            if (json)
                Output.WriteJson(new { installed = false, error = "Xcode not installed or xcode-select not configured." });
            else
                Output.WriteError("Xcode not installed or xcode-select not configured.");
            return;
        }

        var xcodePath = pathResult.Output.Trim();
        var versionResult = await ProcessRunner.RunAsync("xcodebuild", "-version");

        string? version = null;
        string? build = null;

        if (versionResult.ExitCode == 0)
        {
            var lines = versionResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            version = lines.FirstOrDefault()?.Replace("Xcode ", "").Trim();
            build = lines.Skip(1).FirstOrDefault()?.Replace("Build version ", "").Trim();
        }

        // Check for command-line tools version
        var cltResult = await ProcessRunner.RunAsync("pkgutil", "--pkg-info=com.apple.pkg.CLTools_Executables");
        string? cltVersion = null;
        if (cltResult.ExitCode == 0)
        {
            var vLine = cltResult.Output.Split('\n').FirstOrDefault(l => l.StartsWith("version:"));
            cltVersion = vLine?.Replace("version:", "").Trim();
        }

        if (json)
        {
            Output.WriteJson(new
            {
                installed = true,
                path = xcodePath,
                version,
                build,
                commandLineToolsVersion = cltVersion,
            });
        }
        else
        {
            Output.WriteSuccess($"Xcode {version ?? "unknown"}");
            Output.WriteInfo($"Path: {xcodePath}");
            if (build is not null)
                Output.WriteInfo($"Build: {build}");
            if (cltVersion is not null)
                Output.WriteInfo($"Command Line Tools: {cltVersion}");
        }
    }
}
