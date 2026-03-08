using System.CommandLine;
using MauiSherpa.Cli.Helpers;

namespace MauiSherpa.Cli.Commands;

public static class FeaturesCommand
{
    public static Command Create()
    {
        var cmd = new Command("features", "List all MAUI Sherpa capabilities as a structured manifest. Designed for AI agent discovery.");
        cmd.SetAction((_) => Handle());
        return cmd;
    }

    private static void Handle()
    {
        Output.WriteJson(new
        {
            tool = "maui-sherpa",
            description = "CLI for managing mobile development tools — Android SDK, iOS simulators, keystores, .NET workloads, environment diagnostics, and more.",
            version = typeof(FeaturesCommand).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            agentMode = new
            {
                flag = "--agent",
                description = "When issues are found, outputs structured remediation prompts (fix guidance, suggested commands, references) for the calling AI agent instead of starting an inner Copilot session. Combine with any command that can detect problems.",
                example = "maui-sherpa doctor --agent",
            },
            features = new object[]
            {
                new
                {
                    id = "doctor",
                    name = "Environment Diagnostics",
                    description = "Check your MAUI development environment health — .NET SDK, Android SDK, JDK, Xcode, and simulators. With --agent, outputs remediation prompts and fix guidance when issues are found.",
                    commands = new[]
                    {
                        new { command = "maui-sherpa doctor", description = "Run a full environment health check" },
                        new { command = "maui-sherpa doctor --agent", description = "Run health check and output remediation prompts for the calling AI agent when issues are found" },
                    }
                },
                new
                {
                    id = "android.sdk",
                    name = "Android SDK Management",
                    description = "Detect, inspect, and manage Android SDK installations and packages (platform-tools, build-tools, system images, etc.).",
                    commands = new[]
                    {
                        new { command = "maui-sherpa android sdk info", description = "Show Android SDK location and installation status" },
                        new { command = "maui-sherpa android sdk packages", description = "List installed and available SDK packages" },
                        new { command = "maui-sherpa android sdk install <package>", description = "Install an Android SDK package (e.g., 'platform-tools', 'platforms;android-35')" },
                        new { command = "maui-sherpa android sdk uninstall <package>", description = "Uninstall an Android SDK package" },
                    }
                },
                new
                {
                    id = "android.emulators",
                    name = "Android Emulator Management",
                    description = "List, create, start, and stop Android Virtual Devices (AVDs) for emulator testing.",
                    commands = new[]
                    {
                        new { command = "maui-sherpa android emulators list", description = "List all Android Virtual Devices" },
                        new { command = "maui-sherpa android emulators create", description = "Create a new AVD" },
                        new { command = "maui-sherpa android emulators start <name>", description = "Start an emulator by AVD name" },
                        new { command = "maui-sherpa android emulators stop <name>", description = "Stop a running emulator by AVD name" },
                    }
                },
                new
                {
                    id = "android.devices",
                    name = "Android Device Detection",
                    description = "List connected Android devices and emulators via ADB.",
                    commands = new[]
                    {
                        new { command = "maui-sherpa android devices", description = "List connected Android devices and emulators" },
                    }
                },
                new
                {
                    id = "android.keystores",
                    name = "Android Keystore Management",
                    description = "Create Android keystores for app signing, view certificate signature hashes (SHA-1, SHA-256, MD5).",
                    commands = new[]
                    {
                        new { command = "maui-sherpa android keystores list", description = "List known keystore files" },
                        new { command = "maui-sherpa android keystores create", description = "Create a new Android keystore" },
                        new { command = "maui-sherpa android keystores signatures <path>", description = "Display certificate fingerprints (SHA-1, SHA-256, MD5) for a keystore" },
                    }
                },
                new
                {
                    id = "apple.simulators",
                    name = "iOS Simulator Management",
                    description = "List, create, boot, and shutdown iOS/tvOS/watchOS simulators via xcrun simctl.",
                    commands = new[]
                    {
                        new { command = "maui-sherpa apple simulators list", description = "List all iOS/tvOS/watchOS simulators" },
                        new { command = "maui-sherpa apple simulators create", description = "Create a new simulator" },
                        new { command = "maui-sherpa apple simulators boot <udid>", description = "Boot a simulator by UDID" },
                        new { command = "maui-sherpa apple simulators shutdown <udid>", description = "Shutdown a simulator by UDID" },
                    }
                },
                new
                {
                    id = "apple.devices",
                    name = "iOS Device Detection",
                    description = "List connected physical iOS devices via xcrun devicectl or xcdevice.",
                    commands = new[]
                    {
                        new { command = "maui-sherpa apple devices", description = "List connected physical iOS devices" },
                    }
                },
                new
                {
                    id = "apple.xcode",
                    name = "Xcode Information",
                    description = "Show the active Xcode installation path, version, and build number.",
                    commands = new[]
                    {
                        new { command = "maui-sherpa apple xcode", description = "Show Xcode installation details" },
                    }
                },
                new
                {
                    id = "apple.profiles",
                    name = "Provisioning Profile Management",
                    description = "List, inspect, install, and remove locally installed provisioning profiles (.mobileprovision files). Decodes profile contents including entitlements, team, expiration, and device count.",
                    commands = new[]
                    {
                        new { command = "maui-sherpa apple profiles list", description = "List all installed provisioning profiles with name, UUID, type, and expiration" },
                        new { command = "maui-sherpa apple profiles show <profile>", description = "Show full details of a profile by UUID, path, or name substring" },
                        new { command = "maui-sherpa apple profiles install <file>", description = "Install a .mobileprovision file to ~/Library/MobileDevice/Provisioning Profiles/" },
                        new { command = "maui-sherpa apple profiles remove <profile>", description = "Remove a provisioning profile by UUID or name" },
                    }
                },
                new
                {
                    id = "workloads",
                    name = ".NET Workload Management",
                    description = "Query installed .NET SDK workloads, workload sets, and manifests. Useful for diagnosing MAUI build issues.",
                    commands = new[]
                    {
                        new { command = "maui-sherpa workloads list", description = "List installed .NET workloads for the current SDK" },
                        new { command = "maui-sherpa workloads sets", description = "List available workload set versions" },
                    }
                },
            }
        });
    }
}
