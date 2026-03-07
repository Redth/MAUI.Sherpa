using System.CommandLine;
using System.Text.Json;
using MauiSherpa.Cli.Helpers;

namespace MauiSherpa.Cli.Commands.Apple;

public static class DevicesCommand
{
    public static Command Create()
    {
        var cmd = new Command("devices", "List connected physical iOS devices.");
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
            Output.WriteError("iOS device detection is only available on macOS.");
            return;
        }

        // Try devicectl first (Xcode 15+), fall back to xcdevice
        var result = await ProcessRunner.RunAsync("xcrun", "devicectl list devices --json-output /dev/stdout 2>/dev/null");

        List<DeviceInfo> devices;

        if (result.ExitCode == 0 && result.Output.Contains("{"))
        {
            devices = ParseDeviceCtlOutput(result.Output);
        }
        else
        {
            // Fallback to system_profiler
            var spResult = await ProcessRunner.RunAsync("system_profiler", "SPUSBDataType -json");
            devices = ParseSystemProfilerOutput(spResult.Output);
        }

        if (json)
        {
            Output.WriteJson(new { devices });
            return;
        }

        if (devices.Count == 0)
        {
            Console.WriteLine("No iOS devices connected.");
            return;
        }

        Output.WriteTable(
            ["Name", "UDID", "OS Version", "Model"],
            devices.Select(d => new[] { d.Name, d.Udid, d.OsVersion ?? "", d.Model ?? "" }));
    }

    private static List<DeviceInfo> ParseDeviceCtlOutput(string output)
    {
        var devices = new List<DeviceInfo>();
        try
        {
            using var doc = JsonDocument.Parse(output);
            if (doc.RootElement.TryGetProperty("result", out var resultObj) &&
                resultObj.TryGetProperty("devices", out var devicesArr))
            {
                foreach (var device in devicesArr.EnumerateArray())
                {
                    var name = device.TryGetProperty("deviceProperties", out var props) &&
                               props.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var udid = device.TryGetProperty("hardwareProperties", out var hw) &&
                               hw.TryGetProperty("udid", out var u) ? u.GetString() : null;
                    var osVersion = props.TryGetProperty("osVersionNumber", out var ov) ? ov.GetString() : null;
                    var model = hw.TryGetProperty("marketingName", out var m) ? m.GetString() : null;

                    if (name is not null || udid is not null)
                        devices.Add(new DeviceInfo(name ?? "Unknown", udid ?? "", osVersion, model));
                }
            }
        }
        catch { }
        return devices;
    }

    private static List<DeviceInfo> ParseSystemProfilerOutput(string output)
    {
        // Basic fallback — system_profiler doesn't give clean iOS device data
        return new List<DeviceInfo>();
    }

    private record DeviceInfo(string Name, string Udid, string? OsVersion, string? Model);
}
