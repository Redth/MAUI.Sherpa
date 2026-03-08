using System.CommandLine;
using System.Runtime.InteropServices;
using MauiSherpa.Cli.Helpers;

namespace MauiSherpa.Cli.Commands;

public static class DoctorCommand
{
    public static Command Create()
    {
        var cmd = new Command("doctor", "Run a full environment health check for MAUI development.\n\nChecks .NET SDK, Android SDK, JDK, Xcode (macOS), and iOS simulators.\n\nWith --agent, outputs remediation prompts and fix guidance when issues are found, so the calling AI agent can act on them instead of starting an inner Copilot session.");
        cmd.SetAction(async (parseResult, ct) =>
        {
            var json = parseResult.GetValue(CliOptions.Json);
            var agent = parseResult.GetValue(CliOptions.Agent);
            await RunAsync(json, agent);
        });
        return cmd;
    }

    private static async Task RunAsync(bool json, bool agent)
    {
        var checks = new List<HealthCheck>();

        checks.Add(await CheckDotNetAsync());
        checks.Add(await CheckAndroidSdkAsync());
        checks.Add(await CheckJdkAsync());

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            checks.Add(await CheckXcodeAsync());
            checks.Add(await CheckSimulatorsAsync());
        }

        var issues = checks
            .Where(c => c.Status is "error" or "warning")
            .Select(c => new Remediation.Issue(c.Name, c.Message, c.Status == "error"))
            .ToList();

        // Agent mode: always output JSON with remediation prompts when issues exist
        if (agent)
        {
            if (issues.Count == 0)
            {
                Output.WriteJson(new
                {
                    status = "ok",
                    message = "All checks passed. No remediation needed.",
                    checks = checks.Select(c => new { c.Name, c.Status, c.Message, c.Details }),
                });
            }
            else
            {
                Output.WriteJson(Remediation.BuildEnvironmentFix(
                    issues,
                    checks.Select(c => new { c.Name, c.Status, c.Message, c.Details } as object).ToList()));
            }
            return;
        }

        if (json)
        {
            Output.WriteJson(new { checks });
            return;
        }

        Console.WriteLine("MAUI Sherpa — Environment Health Check");
        Console.WriteLine(new string('═', 42));
        Console.WriteLine();

        foreach (var check in checks)
        {
            var icon = check.Status switch
            {
                "ok" => "✓",
                "warning" => "⚠",
                _ => "✗"
            };
            Console.WriteLine($"  {icon} {check.Name}: {check.Message}");
            if (check.Details is not null)
                foreach (var detail in check.Details)
                    Console.WriteLine($"    {detail}");
        }

        Console.WriteLine();
        var okCount = checks.Count(c => c.Status == "ok");
        Console.WriteLine($"  {okCount}/{checks.Count} checks passed.");
    }

    private static async Task<HealthCheck> CheckDotNetAsync()
    {
        try
        {
            var result = await ProcessRunner.RunAsync("dotnet", "--version");
            if (result.ExitCode == 0)
            {
                var version = result.Output.Trim();
                var sdkResult = await ProcessRunner.RunAsync("dotnet", "--list-sdks");
                var sdks = sdkResult.Output
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrEmpty(l))
                    .ToArray();

                return new HealthCheck(".NET SDK", "ok", $"v{version}",
                    sdks.Length > 0 ? sdks : null);
            }
            return new HealthCheck(".NET SDK", "error", "dotnet command failed");
        }
        catch
        {
            return new HealthCheck(".NET SDK", "error", "dotnet not found on PATH");
        }
    }

    private static async Task<HealthCheck> CheckAndroidSdkAsync()
    {
        var sdkPath = Environment.GetEnvironmentVariable("ANDROID_HOME")
                      ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");

        if (string.IsNullOrEmpty(sdkPath))
        {
            var defaultPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Android", "sdk"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Android", "Sdk"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk"),
            };
            sdkPath = defaultPaths.FirstOrDefault(Directory.Exists);
        }

        if (string.IsNullOrEmpty(sdkPath) || !Directory.Exists(sdkPath))
            return new HealthCheck("Android SDK", "error", "Not found. Set ANDROID_HOME or install via Android Studio.");

        var details = new List<string> { $"Path: {sdkPath}" };

        var platformTools = Path.Combine(sdkPath, "platform-tools");
        if (Directory.Exists(platformTools))
            details.Add("platform-tools: installed");
        else
            details.Add("platform-tools: missing");

        var emulatorDir = Path.Combine(sdkPath, "emulator");
        if (Directory.Exists(emulatorDir))
            details.Add("emulator: installed");
        else
            details.Add("emulator: missing");

        return new HealthCheck("Android SDK", "ok", sdkPath, details.ToArray());
    }

    private static async Task<HealthCheck> CheckJdkAsync()
    {
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        string javaBin;

        if (!string.IsNullOrEmpty(javaHome))
            javaBin = Path.Combine(javaHome, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");
        else
            javaBin = "java";

        try
        {
            var result = await ProcessRunner.RunAsync(javaBin, "-version");
            // java -version writes to stderr
            var output = !string.IsNullOrEmpty(result.Error) ? result.Error : result.Output;
            var firstLine = output.Split('\n')[0].Trim();
            var details = javaHome is not null ? new[] { $"JAVA_HOME: {javaHome}" } : null;
            return new HealthCheck("JDK", "ok", firstLine, details);
        }
        catch
        {
            return new HealthCheck("JDK", "error", "java not found. Set JAVA_HOME or install OpenJDK.");
        }
    }

    private static async Task<HealthCheck> CheckXcodeAsync()
    {
        try
        {
            var pathResult = await ProcessRunner.RunAsync("xcode-select", "-p");
            if (pathResult.ExitCode != 0)
                return new HealthCheck("Xcode", "error", "Xcode not installed or xcode-select not configured.");

            var xcodePath = pathResult.Output.Trim();
            var versionResult = await ProcessRunner.RunAsync("xcodebuild", "-version");
            var lines = versionResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            return new HealthCheck("Xcode", "ok", lines.FirstOrDefault()?.Trim() ?? "installed",
                new[] { $"Path: {xcodePath}" }.Concat(lines.Skip(1).Select(l => l.Trim())).ToArray());
        }
        catch
        {
            return new HealthCheck("Xcode", "warning", "Could not determine Xcode status.");
        }
    }

    private static async Task<HealthCheck> CheckSimulatorsAsync()
    {
        try
        {
            var result = await ProcessRunner.RunAsync("xcrun", "simctl list devices available -j");
            if (result.ExitCode != 0)
                return new HealthCheck("iOS Simulators", "warning", "simctl not available");

            // Count devices from the JSON output
            var count = result.Output.Split("\"udid\"", StringSplitOptions.None).Length - 1;
            return new HealthCheck("iOS Simulators", "ok", $"{count} simulator(s) available");
        }
        catch
        {
            return new HealthCheck("iOS Simulators", "warning", "Could not query simulators.");
        }
    }

    private record HealthCheck(string Name, string Status, string Message, string[]? Details = null);
}
