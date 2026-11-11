using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public sealed class AndroidAdbException(string message) : Exception(message);

public sealed class AndroidAdbService : IAndroidAdbService
{
    private readonly IAndroidSdkService _sdkService;
    private readonly IAndroidAdbRunner _runner;

    public AndroidAdbService(IAndroidSdkService sdkService)
        : this(sdkService, new AndroidAdbRunner())
    {
    }

    internal AndroidAdbService(IAndroidSdkService sdkService, IAndroidAdbRunner runner)
    {
        _sdkService = sdkService;
        _runner = runner;
    }

    public async Task<AndroidAdbSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var result = await RunAsync(["devices", "-l"], null, TimeSpan.FromSeconds(15), ct);
        if (result.ExitCode != 0)
            throw new AndroidAdbException("Android Debug Bridge could not list devices. Check that platform-tools is working.");

        var devices = ParseDevices(result.Output);
        var emulators = new List<RunningAndroidEmulator>();
        foreach (var device in devices.Where(d => d.IsEmulator))
        {
            ct.ThrowIfCancellationRequested();
            var nameResult = await RunAsync(
                ["-s", device.Serial, "emu", "avd", "name"], null, TimeSpan.FromSeconds(10), ct);
            emulators.Add(new RunningAndroidEmulator(device.Serial, ParseAvdName(nameResult)));
        }

        return new AndroidAdbSnapshot(devices, emulators);
    }

    public async Task PairAsync(string endpoint, string pairingCode, CancellationToken ct = default)
    {
        if (!IsValidEndpoint(endpoint))
            throw new AndroidAdbException("Enter the pairing IP address and port shown on the device.");
        if (!IsValidPairingCode(pairingCode))
            throw new AndroidAdbException("Enter the six-digit pairing code shown on the device.");

        // The code is supplied on stdin rather than in the process arguments or logs.
        var result = await RunAsync(["pair", endpoint.Trim()], pairingCode, TimeSpan.FromSeconds(60), ct);
        if (result.ExitCode != 0 ||
            !result.Output.Contains("Successfully paired", StringComparison.OrdinalIgnoreCase))
        {
            throw new AndroidAdbException(
                "Pairing failed. Check the pairing address and code, keep the pairing screen open, and try again.");
        }
    }

    public async Task ConnectAsync(string endpoint, CancellationToken ct = default)
    {
        if (!IsValidEndpoint(endpoint))
            throw new AndroidAdbException("Enter the wireless debugging IP address and port shown on the device.");

        var result = await RunAsync(["connect", endpoint.Trim()], null, TimeSpan.FromSeconds(20), ct);
        if (result.ExitCode != 0 ||
            !result.Output.Contains("connected to ", StringComparison.OrdinalIgnoreCase))
        {
            throw new AndroidAdbException(
                "Could not connect. Check the IP address and port on the main Wireless debugging screen and try again.");
        }
    }

    public static bool IsValidPairingCode(string? code) =>
        code is { Length: 6 } && code.All(c => c is >= '0' and <= '9');

    public static bool IsValidEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return false;

        endpoint = endpoint.Trim();
        if (endpoint.Any(char.IsWhiteSpace))
            return false;

        string host;
        string port;
        if (endpoint.StartsWith('['))
        {
            var end = endpoint.IndexOf("]:", StringComparison.Ordinal);
            if (end < 2)
                return false;
            host = endpoint[1..end];
            port = endpoint[(end + 2)..];
            if (!IPAddress.TryParse(host, out var address) || address.AddressFamily != AddressFamily.InterNetworkV6)
                return false;
        }
        else
        {
            var separator = endpoint.LastIndexOf(':');
            if (separator <= 0 || endpoint.IndexOf(':') != separator)
                return false;
            host = endpoint[..separator];
            port = endpoint[(separator + 1)..];
            if (host.StartsWith('-') || Uri.CheckHostName(host) is UriHostNameType.Unknown)
                return false;
        }

        return int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out var value) &&
            value is > 0 and <= 65535;
    }

    private Task<AndroidAdbCommandResult> RunAsync(
        IReadOnlyList<string> arguments, string? standardInput, TimeSpan timeout, CancellationToken ct)
    {
        var sdkPath = _sdkService.SdkPath;
        if (string.IsNullOrWhiteSpace(sdkPath))
            throw new AndroidAdbException("Android SDK not found. Configure it in Settings.");

        return _runner.RunAsync(AppDataPath.GetAdbPath(sdkPath), arguments, standardInput, timeout, ct);
    }

    internal static IReadOnlyList<DeviceInfo> ParseDevices(string output)
    {
        var lines = output.Split('\n');
        var header = Array.FindIndex(lines, line => line.Trim() == "List of devices attached");
        if (header < 0)
            throw new AndroidAdbException("Android Debug Bridge returned an unreadable device list.");

        var devices = new List<DeviceInfo>();
        foreach (var line in lines.Skip(header + 1))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 0)
                continue;
            if (fields.Length < 2)
                throw new AndroidAdbException("Android Debug Bridge returned an unreadable device list.");

            var serial = fields[0];
            var state = fields[1] == "no" && fields.Length > 2 && fields[2] == "permissions"
                ? "no permissions"
                : fields[1];
            var model = fields.FirstOrDefault(field => field.StartsWith("model:", StringComparison.Ordinal));
            devices.Add(new DeviceInfo(
                serial,
                state,
                model?["model:".Length..].Replace('_', ' '),
                serial.StartsWith("emulator-", StringComparison.Ordinal)));
        }

        return devices;
    }

    private static string? ParseAvdName(AndroidAdbCommandResult result)
    {
        if (result.ExitCode != 0)
            return null;

        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 2 && lines[1] == "OK" && lines[0].Length > 0 ? lines[0] : null;
    }
}

internal sealed record AndroidAdbCommandResult(int ExitCode, string Output, string Error);

internal interface IAndroidAdbRunner
{
    Task<AndroidAdbCommandResult> RunAsync(
        string adbPath, IReadOnlyList<string> arguments, string? standardInput, TimeSpan timeout, CancellationToken ct);
}

internal sealed class AndroidAdbRunner : IAndroidAdbRunner
{
    public async Task<AndroidAdbCommandResult> RunAsync(
        string adbPath, IReadOnlyList<string> arguments, string? standardInput, TimeSpan timeout, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(adbPath))
            throw new AndroidAdbException("Android platform-tools was not found. Install it from SDK Packages.");

        var startInfo = new ProcessStartInfo(adbPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput != null
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        Process? started;
        try
        {
            started = Process.Start(startInfo);
        }
        catch (Win32Exception)
        {
            throw new AndroidAdbException("Android Debug Bridge could not start. Check your platform-tools installation.");
        }

        using var process = started ??
            throw new AndroidAdbException("Android Debug Bridge could not start. Check your platform-tools installation.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);

        try
        {
            var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var error = process.StandardError.ReadToEndAsync(deadline.Token);
            if (standardInput != null)
            {
                await process.StandardInput.WriteLineAsync(standardInput.AsMemory(), deadline.Token);
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(deadline.Token);
            return new AndroidAdbCommandResult(process.ExitCode, await output, await error);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AndroidAdbException("Android Debug Bridge did not respond in time. Check the device and try again.");
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
        }
    }
}
