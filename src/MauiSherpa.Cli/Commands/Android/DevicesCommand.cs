using System.CommandLine;
using MauiSherpa.Cli.Helpers;

namespace MauiSherpa.Cli.Commands.Android;

public static class DevicesCommand
{
    public static Command Create()
    {
        var cmd = new Command("devices", "List connected Android devices and emulators via ADB.");
        cmd.SetAction(async (parseResult, ct) =>
        {
            var json = parseResult.GetValue(CliOptions.Json);
            await HandleAsync(json);
        });
        return cmd;
    }

    private static async Task HandleAsync(bool json)
    {
        var adbPath = await ProcessRunner.WhichAsync("adb");
        if (adbPath is null)
        {
            Output.WriteError("adb not found on PATH. Install platform-tools: 'maui-sherpa android sdk install platform-tools'.");
            return;
        }

        var result = await ProcessRunner.RunAsync(adbPath, "devices -l");
        if (result.ExitCode != 0)
        {
            Output.WriteError($"adb failed: {result.Error}");
            return;
        }

        var devices = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1) // skip "List of devices attached"
            .Select(ParseDevice)
            .Where(d => d is not null)
            .ToList();

        if (json)
        {
            Output.WriteJson(new { devices });
            return;
        }

        if (devices.Count == 0)
        {
            Console.WriteLine("No Android devices connected.");
            return;
        }

        Output.WriteTable(
            ["Serial", "State", "Model", "Transport"],
            devices.Select(d => new[] { d!.Serial, d.State, d.Model ?? "", d.TransportId ?? "" }));
    }

    private static DeviceInfo? ParseDevice(string line)
    {
        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;

        var serial = parts[0];
        var state = parts[1];
        string? model = null;
        string? transportId = null;

        foreach (var part in parts.Skip(2))
        {
            if (part.StartsWith("model:")) model = part["model:".Length..];
            if (part.StartsWith("transport_id:")) transportId = part["transport_id:".Length..];
        }

        return new DeviceInfo(serial, state, model, transportId);
    }

    private record DeviceInfo(string Serial, string State, string? Model, string? TransportId);
}
