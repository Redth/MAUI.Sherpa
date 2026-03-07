using System.CommandLine;
using System.Text.Json;
using MauiSherpa.Cli.Helpers;

namespace MauiSherpa.Cli.Commands.Android;

public static class EmulatorsCommand
{
    public static Command Create()
    {
        var cmd = new Command("emulators", "Manage Android Virtual Devices (AVDs) for emulator testing.");
        cmd.Add(CreateListCommand());
        cmd.Add(CreateStartCommand());
        cmd.Add(CreateStopCommand());
        return cmd;
    }

    private static string? FindEmulator()
    {
        var sdkPath = Environment.GetEnvironmentVariable("ANDROID_HOME")
                      ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");

        if (!string.IsNullOrEmpty(sdkPath))
        {
            var emulatorBin = Path.Combine(sdkPath, "emulator", OperatingSystem.IsWindows() ? "emulator.exe" : "emulator");
            if (File.Exists(emulatorBin)) return emulatorBin;
        }

        return null;
    }

    private static string? FindAvdManager()
    {
        var sdkPath = Environment.GetEnvironmentVariable("ANDROID_HOME")
                      ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");

        if (!string.IsNullOrEmpty(sdkPath))
        {
            var candidates = new[]
            {
                Path.Combine(sdkPath, "cmdline-tools", "latest", "bin", "avdmanager"),
                Path.Combine(sdkPath, "tools", "bin", "avdmanager"),
            };
            if (OperatingSystem.IsWindows())
                candidates = candidates.Select(c => c + ".bat").ToArray();

            return candidates.FirstOrDefault(File.Exists);
        }

        return null;
    }

    private static Command CreateListCommand()
    {
        var cmd = new Command("list", "List all Android Virtual Devices (AVDs).");
        cmd.SetAction(async (parseResult, ct) =>
        {
            var json = parseResult.GetValue(CliOptions.Json);
            var emulator = FindEmulator();
            if (emulator is null)
            {
                Output.WriteError("Android emulator not found. Install via 'maui-sherpa android sdk install emulator'.");
                return;
            }

            var result = await ProcessRunner.RunAsync(emulator, "-list-avds");
            if (result.ExitCode != 0)
            {
                Output.WriteError($"Failed to list AVDs: {result.Error}");
                return;
            }

            var avds = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l))
                .ToArray();

            if (json)
            {
                Output.WriteJson(new { avds = avds.Select(name => new { name }) });
                return;
            }

            if (avds.Length == 0)
            {
                Console.WriteLine("No AVDs found.");
                return;
            }

            foreach (var avd in avds)
                Console.WriteLine($"  • {avd}");
        });
        return cmd;
    }

    private static Command CreateStartCommand()
    {
        var cmd = new Command("start", "Start an Android emulator by AVD name.");
        var nameArg = new Argument<string>("name") { Description = "AVD name to start" };
        cmd.Add(nameArg);
        cmd.SetAction(async (parseResult, ct) =>
        {
            var name = parseResult.GetValue(nameArg);
            var emulator = FindEmulator();
            if (emulator is null) { Output.WriteError("Android emulator not found."); return; }

            Console.WriteLine($"Starting emulator '{name}'...");
            // Fire and forget — emulator runs as its own process
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = emulator,
                Arguments = $"-avd {name}",
                UseShellExecute = false,
            };
            System.Diagnostics.Process.Start(psi);
            Output.WriteSuccess($"Emulator '{name}' starting.");
        });
        return cmd;
    }

    private static Command CreateStopCommand()
    {
        var cmd = new Command("stop", "Stop a running Android emulator by AVD name.\n\nUses 'adb emu kill' on the matching emulator.");
        var nameArg = new Argument<string>("name") { Description = "AVD name to stop" };
        cmd.Add(nameArg);
        cmd.SetAction(async (parseResult, ct) =>
        {
            var name = parseResult.GetValue(nameArg);
            var adbPath = await ProcessRunner.WhichAsync("adb");
            if (adbPath is null)
            {
                Output.WriteError("adb not found on PATH.");
                return;
            }

            // Find the emulator serial for this AVD
            var result = await ProcessRunner.RunAsync(adbPath, "devices -l");
            if (result.ExitCode != 0)
            {
                Output.WriteError("Failed to list devices.");
                return;
            }

            var emulatorSerial = result.Output.Split('\n')
                .Where(l => l.Contains("emulator-"))
                .Select(l => l.Split('\t', ' ')[0].Trim())
                .FirstOrDefault();

            if (emulatorSerial is null)
            {
                Output.WriteError($"No running emulator found for '{name}'.");
                return;
            }

            var killResult = await ProcessRunner.RunAsync(adbPath, $"-s {emulatorSerial} emu kill");
            if (killResult.ExitCode == 0)
                Output.WriteSuccess($"Emulator stopped.");
            else
                Output.WriteError($"Failed to stop emulator: {killResult.Error}");
        });
        return cmd;
    }
}
