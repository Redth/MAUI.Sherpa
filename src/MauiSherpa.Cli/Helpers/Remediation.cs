namespace MauiSherpa.Cli.Helpers;

/// <summary>
/// Builds remediation prompts for AI agents, mirroring the Copilot context
/// that MAUI Sherpa's GUI would send to an inner Copilot session.
/// In --agent mode, these are emitted as structured output so the calling
/// agent can act on them directly.
/// </summary>
public static class Remediation
{
    /// <summary>
    /// Build a remediation payload for environment issues found by the doctor command.
    /// </summary>
    public static object BuildEnvironmentFix(
        IReadOnlyList<Issue> issues,
        IReadOnlyList<object> checks)
    {
        var errors = issues.Where(i => i.IsError).ToList();
        var warnings = issues.Where(i => !i.IsError).ToList();

        return new
        {
            type = "environment_fix",
            diagnostics = checks,
            issues = issues.Select(i => new
            {
                category = i.Category,
                message = i.Message,
                severity = i.IsError ? "error" : "warning",
            }),
            prompt = BuildEnvironmentFixPrompt(errors, warnings),
            guidance = GetDiagnosticGuidance(issues),
            suggestedCommands = GetSuggestedCommands(issues),
        };
    }

    /// <summary>
    /// Build a remediation payload for a failed command or operation.
    /// </summary>
    public static object BuildProcessFailure(
        string command,
        int exitCode,
        string? output)
    {
        var truncated = output is not null && output.Length > 2000
            ? output[..2000] + "\n... (truncated)"
            : output;

        return new
        {
            type = "process_failure",
            command,
            exitCode,
            output = truncated,
            prompt = $"""
                A command failed and needs troubleshooting.

                Command: `{command}`
                Exit Code: {exitCode}
                {(truncated is not null ? $"\nOutput:\n```\n{truncated}\n```" : "")}

                Please analyze this failure and suggest a fix.
                Use the `maui-sherpa doctor` command to check the overall environment health.
                Use `maui-sherpa android sdk info` or `maui-sherpa apple xcode` to inspect specific tools.
                """,
        };
    }

    /// <summary>
    /// Build a remediation payload for a failed operation.
    /// </summary>
    public static object BuildOperationFailure(
        string operationName,
        string errorMessage,
        string? details = null)
    {
        return new
        {
            type = "operation_failure",
            operation = operationName,
            error = errorMessage,
            details,
            prompt = $"""
                An operation failed and needs troubleshooting.

                Operation: {operationName}
                Error: {errorMessage}
                {(details is not null ? $"\nDetails:\n{details}" : "")}

                Please diagnose this issue and suggest a fix.
                Use `maui-sherpa doctor` to check the overall environment health.
                """,
        };
    }

    private static string BuildEnvironmentFixPrompt(
        List<Issue> errors,
        List<Issue> warnings)
    {
        var lines = new List<string>
        {
            "Please help fix my .NET MAUI development environment.",
            "",
            "Current issues:",
        };

        foreach (var e in errors)
            lines.Add($"  ERROR - {e.Category}: {e.Message}");
        foreach (var w in warnings)
            lines.Add($"  WARNING - {w.Category}: {w.Message}");

        lines.Add("");
        lines.Add("Remediation guidance:");
        lines.Add("");
        lines.Add("1. Start by understanding the working directory, SDK location, and any global.json settings.");
        lines.Add("2. Check the .NET SDK version: run `dotnet --version` and `dotnet --list-sdks`.");
        lines.Add("3. Check MAUI workloads: run `maui-sherpa workloads list` and `maui-sherpa workloads sets`.");
        lines.Add("4. For JDK issues: use Microsoft Build of OpenJDK (not other distributions).");
        lines.Add("5. For Android SDK issues: run `maui-sherpa android sdk info` and `maui-sherpa android sdk packages --installed`.");
        lines.Add("6. For Xcode issues (macOS): run `maui-sherpa apple xcode` and verify the active version.");
        lines.Add("");
        lines.Add("Use `maui-sherpa` CLI commands to inspect and fix individual components.");
        lines.Add("Ask before making changes to the system.");

        return string.Join("\n", lines);
    }

    private static List<object> GetDiagnosticGuidance(IReadOnlyList<Issue> issues)
    {
        var guidance = new List<object>();

        foreach (var issue in issues)
        {
            var steps = issue.Category switch
            {
                "dotnet" or ".NET SDK" => new
                {
                    category = issue.Category,
                    steps = new[]
                    {
                        "Run `dotnet --list-sdks` to see installed SDKs",
                        "Run `maui-sherpa workloads list` to check installed workloads",
                        "Run `maui-sherpa workloads sets` to find available workload set versions",
                        "Check if a global.json pins a specific SDK version: `cat global.json`",
                        "Install missing workloads: `dotnet workload install maui`",
                        "Update workload set: `dotnet workload update`",
                    },
                    references = new[]
                    {
                        "https://dotnet.microsoft.com/download/dotnet",
                        "https://learn.microsoft.com/dotnet/maui/get-started/installation",
                    }
                } as object,
                "android" or "Android SDK" => new
                {
                    category = issue.Category,
                    steps = new[]
                    {
                        "Run `maui-sherpa android sdk info` to check SDK installation",
                        "Run `maui-sherpa android sdk packages --installed` to see installed packages",
                        "Install missing packages: `maui-sherpa android sdk install <package>`",
                        "Ensure platform-tools, build-tools, and a platform SDK are installed",
                        "Check ANDROID_HOME environment variable points to the SDK directory",
                    },
                    references = new[]
                    {
                        "https://developer.android.com/studio/command-line/sdkmanager",
                    }
                } as object,
                "jdk" or "JDK" => new
                {
                    category = issue.Category,
                    steps = new[]
                    {
                        "Use Microsoft Build of OpenJDK (not other JDK distributions)",
                        "On macOS: `brew install --cask microsoft-openjdk`",
                        "On Windows: download from https://learn.microsoft.com/java/openjdk/download",
                        "Set JAVA_HOME environment variable to the JDK installation path",
                        "Verify: `java -version` should show microsoft build",
                    },
                    references = new[]
                    {
                        "https://learn.microsoft.com/java/openjdk/download",
                    }
                } as object,
                "xcode" or "Xcode" => new
                {
                    category = issue.Category,
                    steps = new[]
                    {
                        "Run `maui-sherpa apple xcode` to check the active Xcode version",
                        "Run `xcode-select -p` to check the selected Xcode path",
                        "Switch Xcode version: `sudo xcode-select -s /Applications/Xcode.app`",
                        "Accept license: `sudo xcodebuild -license accept`",
                        "Install command line tools: `xcode-select --install`",
                    },
                    references = new[]
                    {
                        "https://developer.apple.com/xcode/",
                    }
                } as object,
                "simulators" or "iOS Simulators" => new
                {
                    category = issue.Category,
                    steps = new[]
                    {
                        "Run `maui-sherpa apple simulators list` to see available simulators",
                        "Create a simulator: `maui-sherpa apple simulators create --name 'iPhone 16' --device-type iPhone-16 --runtime iOS-18-2`",
                        "Download runtimes from Xcode → Settings → Platforms",
                        "Or via CLI: `xcodebuild -downloadPlatform iOS`",
                    },
                    references = new[]
                    {
                        "https://developer.apple.com/documentation/xcode/installing-additional-simulator-runtimes",
                    }
                } as object,
                _ => new
                {
                    category = issue.Category,
                    steps = new[] { $"Run `maui-sherpa doctor --json` for detailed diagnostics" },
                    references = Array.Empty<string>(),
                } as object,
            };

            guidance.Add(steps);
        }

        return guidance;
    }

    private static List<string> GetSuggestedCommands(IReadOnlyList<Issue> issues)
    {
        var commands = new List<string>();

        foreach (var issue in issues)
        {
            switch (issue.Category)
            {
                case "dotnet" or ".NET SDK":
                    commands.Add("dotnet --list-sdks");
                    commands.Add("maui-sherpa workloads list");
                    commands.Add("dotnet workload install maui");
                    break;
                case "android" or "Android SDK":
                    commands.Add("maui-sherpa android sdk info");
                    commands.Add("maui-sherpa android sdk packages --installed");
                    break;
                case "jdk" or "JDK":
                    commands.Add("java -version");
                    if (OperatingSystem.IsMacOS())
                        commands.Add("brew install --cask microsoft-openjdk");
                    break;
                case "xcode" or "Xcode":
                    commands.Add("maui-sherpa apple xcode");
                    commands.Add("xcode-select -p");
                    break;
                case "simulators" or "iOS Simulators":
                    commands.Add("maui-sherpa apple simulators list");
                    break;
            }
        }

        return commands.Distinct().ToList();
    }

    public record Issue(string Category, string Message, bool IsError);
}
