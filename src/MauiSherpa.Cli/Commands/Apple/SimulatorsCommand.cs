using System.CommandLine;
using System.Text.Json;
using MauiSherpa.Cli.Helpers;

namespace MauiSherpa.Cli.Commands.Apple;

public static class SimulatorsCommand
{
    public static Command Create()
    {
        var cmd = new Command("simulators", "Manage iOS/tvOS/watchOS simulators via xcrun simctl.");
        cmd.Add(CreateListCommand());
        cmd.Add(CreateBootCommand());
        cmd.Add(CreateShutdownCommand());
        cmd.Add(CreateCreateCommand());
        return cmd;
    }

    private static Command CreateListCommand()
    {
        var cmd = new Command("list", "List all simulators. Filters available by default.");
        var allOpt = new Option<bool>("--all") { Description = "Include unavailable simulators" };
        var bootedOpt = new Option<bool>("--booted") { Description = "Show only booted simulators" };
        cmd.Add(allOpt);
        cmd.Add(bootedOpt);
        cmd.SetAction(async (parseResult, ct) =>
        {
            var all = parseResult.GetValue(allOpt);
            var booted = parseResult.GetValue(bootedOpt);
            var json = parseResult.GetValue(CliOptions.Json);
            if (!OperatingSystem.IsMacOS())
            {
                Output.WriteError("Simulators are only available on macOS.");
                return;
            }

            var availableArg = all ? "" : " available";
            var result = await ProcessRunner.RunAsync("xcrun", $"simctl list devices{availableArg} -j");
            if (result.ExitCode != 0)
            {
                Output.WriteError($"simctl failed: {result.Error}");
                return;
            }

            using var doc = JsonDocument.Parse(result.Output);
            var devices = new List<SimulatorInfo>();

            if (doc.RootElement.TryGetProperty("devices", out var devicesObj))
            {
                foreach (var runtime in devicesObj.EnumerateObject())
                {
                    var runtimeName = runtime.Name
                        .Replace("com.apple.CoreSimulator.SimRuntime.", "")
                        .Replace("-", " ")
                        .Replace(".", "");

                    foreach (var device in runtime.Value.EnumerateArray())
                    {
                        var name = device.GetProperty("name").GetString() ?? "";
                        var udid = device.GetProperty("udid").GetString() ?? "";
                        var state = device.GetProperty("state").GetString() ?? "";
                        var isAvailable = device.TryGetProperty("isAvailable", out var avail) && avail.GetBoolean();

                        if (booted && state != "Booted") continue;

                        devices.Add(new SimulatorInfo(name, udid, state, runtimeName, isAvailable));
                    }
                }
            }

            if (json)
            {
                Output.WriteJson(new { simulators = devices });
                return;
            }

            if (devices.Count == 0)
            {
                Console.WriteLine("No simulators found.");
                return;
            }

            Output.WriteTable(
                ["Name", "UDID", "State", "Runtime"],
                devices.Select(d => new[] { d.Name, d.Udid, d.State, d.Runtime }));
        });
        return cmd;
    }

    private static Command CreateBootCommand()
    {
        var cmd = new Command("boot", "Boot a simulator by UDID.\n\nExample:\n  maui-sherpa apple simulators boot XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX");
        var udidArg = new Argument<string>("udid") { Description = "Simulator UDID" };
        cmd.Add(udidArg);
        cmd.SetAction(async (parseResult, ct) =>
        {
            var udid = parseResult.GetValue(udidArg);
            if (!OperatingSystem.IsMacOS()) { Output.WriteError("macOS only."); return; }

            var result = await ProcessRunner.RunAsync("xcrun", $"simctl boot {udid}");
            if (result.ExitCode == 0)
                Output.WriteSuccess($"Simulator {udid} booted.");
            else
                Output.WriteError($"Failed to boot: {result.Error}");
        });
        return cmd;
    }

    private static Command CreateShutdownCommand()
    {
        var cmd = new Command("shutdown", "Shutdown a simulator by UDID.");
        var udidArg = new Argument<string>("udid") { Description = "Simulator UDID" };
        cmd.Add(udidArg);
        cmd.SetAction(async (parseResult, ct) =>
        {
            var udid = parseResult.GetValue(udidArg);
            if (!OperatingSystem.IsMacOS()) { Output.WriteError("macOS only."); return; }

            var result = await ProcessRunner.RunAsync("xcrun", $"simctl shutdown {udid}");
            if (result.ExitCode == 0)
                Output.WriteSuccess($"Simulator {udid} shutdown.");
            else
                Output.WriteError($"Failed to shutdown: {result.Error}");
        });
        return cmd;
    }

    private static Command CreateCreateCommand()
    {
        var cmd = new Command("create", "Create a new simulator.\n\nExample:\n  maui-sherpa apple simulators create --name 'iPhone 16' --device-type 'iPhone-16' --runtime 'iOS-18-2'");

        var nameOpt = new Option<string>("--name") { Description = "Simulator name", Required = true };
        var deviceTypeOpt = new Option<string>("--device-type") { Description = "Device type identifier (e.g., 'iPhone-16')", Required = true };
        var runtimeOpt = new Option<string>("--runtime") { Description = "Runtime identifier (e.g., 'iOS-18-2')", Required = true };

        cmd.Add(nameOpt);
        cmd.Add(deviceTypeOpt);
        cmd.Add(runtimeOpt);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var name = parseResult.GetValue(nameOpt);
            var deviceType = parseResult.GetValue(deviceTypeOpt);
            var runtime = parseResult.GetValue(runtimeOpt);
            var json = parseResult.GetValue(CliOptions.Json);
            if (!OperatingSystem.IsMacOS()) { Output.WriteError("macOS only."); return; }

            var dtId = $"com.apple.CoreSimulator.SimDeviceType.{deviceType}";
            var rtId = $"com.apple.CoreSimulator.SimRuntime.{runtime}";

            var result = await ProcessRunner.RunAsync("xcrun", $"simctl create \"{name}\" {dtId} {rtId}");
            if (result.ExitCode == 0)
            {
                var udid = result.Output.Trim();
                if (json)
                    Output.WriteJson(new { udid, name, deviceType, runtime });
                else
                    Output.WriteSuccess($"Created simulator '{name}': {udid}");
            }
            else
            {
                Output.WriteError($"Failed to create simulator: {result.Error}");
            }
        });

        return cmd;
    }

    private record SimulatorInfo(string Name, string Udid, string State, string Runtime, bool IsAvailable);
}
