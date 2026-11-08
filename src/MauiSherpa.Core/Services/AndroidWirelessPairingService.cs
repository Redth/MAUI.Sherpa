using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using MauiSherpa.Core.Interfaces;
using QRCoder;

namespace MauiSherpa.Core.Services;

public sealed class AndroidWirelessPairingService : IAndroidWirelessPairingService
{
    private readonly ILoggingService _logger;
    private readonly Func<string?> _resolveAdb;
    private readonly Func<ProcessStartInfo, string?, CancellationToken, Task<AdbCommandResult>> _run;
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    public AndroidWirelessPairingService(IAndroidSdkService sdk, ILoggingService logger)
        : this(logger, () =>
        {
            var sdkPath = sdk.SdkPath;
            if (string.IsNullOrWhiteSpace(sdkPath))
                return null;
            var path = Path.Combine(sdkPath, "platform-tools", OperatingSystem.IsWindows() ? "adb.exe" : "adb");
            return File.Exists(path) ? path : null;
        }, RunProcessAsync)
    {
    }

    internal AndroidWirelessPairingService(
        ILoggingService logger,
        Func<string?> resolveAdb,
        Func<ProcessStartInfo, string?, CancellationToken, Task<AdbCommandResult>> run)
    {
        _logger = logger;
        _resolveAdb = resolveAdb;
        _run = run;
    }

    public AndroidWirelessQrSession CreateQrPairingSession()
    {
        var serviceName = "studio-sherpa-" + RandomNumberGenerator.GetHexString(20, lowercase: true);
        var password = RandomNumberGenerator.GetHexString(32, lowercase: true);
        // Android's Wireless debugging scanner expects ADB's Wi-Fi payload, not a Wi-Fi network login.
        var payload = $"WIFI:T:ADB;S:{serviceName};P:{password};;";
        var png = PngByteQRCodeHelper.GetQRCode(payload, QRCodeGenerator.ECCLevel.Q, 8);
        return new(serviceName, password, "data:image/png;base64," + Convert.ToBase64String(png),
            DateTimeOffset.UtcNow.AddMinutes(3));
    }

    public async Task<AndroidWirelessOperationResult> PairQrAsync(AndroidWirelessQrSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ct.ThrowIfCancellationRequested();
        var remaining = session.ExpiresAt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero || !session.TryStart())
            return Fail("This QR code has expired or was already used. Show a new QR code and scan it again.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(remaining);
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var discovery = await DiscoverAsync(deadline.Token);
                if (!discovery.Success)
                    return Fail("QR pairing needs network discovery. Use pairing by code if discovery is unavailable.\n" +
                        discovery.Message?.Replace(session.Password, "[redacted]", StringComparison.Ordinal));

                var service = discovery.Services.FirstOrDefault(s =>
                    s.IsPairing && string.Equals(s.Name, session.ServiceName, StringComparison.Ordinal));
                if (service != null)
                    return await PairEndpointAsync(service.Endpoint, session.Password, deadline.Token,
                        "QR pairing failed. Show a new QR code and keep the device's Wireless debugging QR scanner open until pairing finishes.");

                await Task.Delay(1000, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Fail("The QR code expired before pairing finished. Show a new code, keep the device's QR scanner open, and check that both devices are on the same Wi-Fi network. You can also pair by code.");
        }
    }

    public async Task<AndroidWirelessOperationResult> PairAsync(string endpoint, string pairingCode, CancellationToken ct = default)
    {
        if (!TryNormalizeEndpoint(endpoint, out var address))
            return Fail("Enter the pairing IP address and port shown on your device, for example 192.168.1.20:37123.");

        var code = pairingCode.Trim();
        if (code.Length != 6 || code.Any(c => c is < '0' or > '9'))
            return Fail("Enter the six-digit pairing code shown on your device.");

        return await PairEndpointAsync(address, code, ct);
    }

    private async Task<AndroidWirelessOperationResult> PairEndpointAsync(string address, string code, CancellationToken ct,
        string failureMessage = "Pairing failed. Keep the pairing-code screen open and use its current address and code.")
    {
        try
        {
            // Send the code through stdin, never through command arguments or the shared command/output logger.
            var result = await ExecuteAsync(["pair", address], code, ct);
            if (result.ExitCode == 0 && result.Output.TrimStart().StartsWith("Enter pairing code:", StringComparison.OrdinalIgnoreCase))
                result = result with { Output = result.Output[(result.Output.IndexOf(':') + 1)..] };

            if (result.ExitCode == 0 && result.Output.Trim().StartsWith("Successfully paired to ", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Android wireless pairing succeeded.");
                return new(AndroidWirelessOperationState.Paired,
                    "Paired successfully. Pairing does not guarantee a debugging connection. Use the connection address from the main Wireless debugging screen if the device is not connected automatically.");
            }

            return Fail(DescribeFailure(result, failureMessage, code));
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or TimeoutException)
        {
            return Fail(OperationError(ex));
        }
    }

    public async Task<AndroidWirelessOperationResult> ConnectAsync(string endpoint, CancellationToken ct = default)
    {
        if (!TryNormalizeEndpoint(endpoint, out var address))
            return Fail("Enter the debugging IP address and port, for example 192.168.1.20:40555. This is not the pairing port.");

        try
        {
            var result = await ExecuteAsync(["connect", address], null, ct);
            var output = result.Output.Trim();
            if (result.ExitCode != 0 ||
                !(output.StartsWith("connected to ", StringComparison.OrdinalIgnoreCase) ||
                  output.StartsWith("already connected to ", StringComparison.OrdinalIgnoreCase)))
                return Fail(DescribeFailure(result, "Connection failed. Check the debugging port, enable Wireless debugging, and make sure both devices are on the same network."));

            for (var attempt = 0; attempt < 3; attempt++)
            {
                var state = await ExecuteAsync(["-s", address, "get-state"], null, ct);
                if (state.ExitCode == 0 && state.Output.Trim() == "device")
                {
                    _logger.LogInformation("Android wireless debugging device is online.");
                    return new(AndroidWirelessOperationState.Connected, "Connected and ready for debugging.");
                }
                if (attempt < 2)
                    await Task.Delay(500, ct);
            }

            _logger.LogWarning("ADB accepted a wireless connection, but the device is not online.");
            return new(AndroidWirelessOperationState.ConnectionPending,
                "ADB accepted the connection, but the device is not online yet. Check for an authorization prompt on the device, then retry the connection.");
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or TimeoutException)
        {
            return Fail(OperationError(ex));
        }
    }

    public async Task<AndroidWirelessDiscoveryResult> DiscoverAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await ExecuteAsync(["mdns", "services"], null, ct);
            if (result.ExitCode != 0 || result.Output.Contains("unknown command", StringComparison.OrdinalIgnoreCase) ||
                result.Output.Contains("ERROR:", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Android wireless discovery failed.");
                return new(false, [], DescribeFailure(result,
                    "Network discovery is unavailable. Update Android SDK platform-tools or enter the device address manually."));
            }

            var services = new List<AndroidWirelessService>();
            foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 2)
                    continue;
                var type = fields[1].TrimEnd('.');
                if (type is not ("_adb-tls-pairing._tcp" or "_adb-tls-connect._tcp"))
                    continue;
                if (fields.Length != 3 || !TryNormalizeEndpoint(fields[2], out var endpoint))
                    throw new FormatException("ADB returned an invalid wireless discovery address. Enter the device address manually.");
                services.Add(new(fields[0], endpoint, type == "_adb-tls-pairing._tcp"));
            }

            if (services.Count == 0 && !result.Output.Contains("List of discovered mdns services", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("ADB did not return a wireless discovery list. Update Android SDK platform-tools or enter the device address manually.");

            return new(true, services.Distinct().ToArray());
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or TimeoutException or FormatException)
        {
            _logger.LogWarning("Android wireless discovery failed.");
            return new(false, [], OperationError(ex));
        }
    }

    public static bool TryNormalizeEndpoint(string? input, out string endpoint)
    {
        endpoint = "";
        var value = input?.Trim();
        if (string.IsNullOrEmpty(value))
            return false;
        var separator = value.LastIndexOf(':');
        if (separator <= 0 || !int.TryParse(value.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var port) ||
            port is < 1 or > 65535)
            return false;

        var host = value[..separator];
        if (host.StartsWith('[') && host.EndsWith(']'))
        {
            if (!IPAddress.TryParse(host[1..^1], out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
                return false;
        }
        else if (host.Contains(':') || host.Length > 253 || Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            return false;
        }

        endpoint = $"{host}:{port}";
        return true;
    }

    private async Task<AdbCommandResult> ExecuteAsync(IReadOnlyList<string> arguments, string? input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = _resolveAdb();
        if (path == null)
            throw new InvalidOperationException("Android SDK platform-tools (adb) were not found. Set your SDK location in Settings and install or update platform-tools in Android SDK.");

        var startInfo = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CommandTimeout);
        try
        {
            return await _run(startInfo, input, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("ADB did not respond within 30 seconds. Check the device address, keep Wireless debugging open, and try again.");
        }
    }

    internal static async Task<AdbCommandResult> RunProcessAsync(ProcessStartInfo startInfo, string? input, CancellationToken ct)
    {
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            if (input != null)
                await process.StandardInput.WriteLineAsync(input.AsMemory(), ct);
            process.StandardInput.Close();
            await process.WaitForExitAsync(ct);
            return new(process.ExitCode, await output, await error);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output, error);
        }
    }

    private AndroidWirelessOperationResult Fail(string message)
    {
        _logger.LogWarning("Android wireless pairing or connection failed.");
        return new(AndroidWirelessOperationState.Failed, message);
    }

    private static string DescribeFailure(AdbCommandResult result, string guidance, string? code = null)
    {
        var detail = string.Join("\n", new[] { result.Output.Trim(), result.Error.Trim() }.Where(s => s.Length > 0));
        if (!string.IsNullOrEmpty(code))
            detail = detail.Replace(code, "[redacted]", StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(detail) ? guidance : $"{guidance}\n{detail}";
    }

    private static string OperationError(Exception ex) => ex is Win32Exception or IOException
        ? "Could not run ADB. Check that Android SDK platform-tools are installed and executable, then try again."
        : ex.Message;

    internal sealed record AdbCommandResult(int ExitCode, string Output, string Error = "");
}
