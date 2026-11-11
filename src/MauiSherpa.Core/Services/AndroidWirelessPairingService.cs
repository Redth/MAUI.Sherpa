using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MauiSherpa.Core.Interfaces;
using QRCoder;

namespace MauiSherpa.Core.Services;

public sealed class AndroidWirelessPairingService : IAndroidWirelessPairingService
{
    private readonly ILoggingService _logger;
    private readonly Func<string?> _resolveAdb;
    private readonly Func<ProcessStartInfo, string?, CancellationToken, Task<AdbCommandResult>> _run;
    private readonly int? _discoveryPort;
    private readonly TimeSpan _connectAfterPair;
    private readonly Func<string, CancellationToken, Task<bool>>? _canReach;
    private int _discoveryServerStarted;
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);
    private static readonly Regex PairedGuid = new(@"\[guid=([^\]\s]+)\]", RegexOptions.CultureInvariant);

    // The user's adb server often misses wireless devices: its default mDNS browser reads network interfaces once at
    // startup and can miss services announced later. Sherpa also asks a private, discovery-only adb server (no USB,
    // no emulators, no auto-connect) that starts fresh for each pairing session.
    internal const int DiscoveryServerPort = 15037;

    public AndroidWirelessPairingService(IAndroidSdkService sdk, ILoggingService logger)
        : this(logger, () =>
        {
            var sdkPath = sdk.SdkPath;
            if (string.IsNullOrWhiteSpace(sdkPath))
                return null;
            var path = Path.Combine(sdkPath, "platform-tools", OperatingSystem.IsWindows() ? "adb.exe" : "adb");
            return File.Exists(path) ? path : null;
        }, RunProcessAsync, DiscoveryServerPort, TimeSpan.FromSeconds(10), CanReachAsync)
    {
    }

    internal AndroidWirelessPairingService(
        ILoggingService logger,
        Func<string?> resolveAdb,
        Func<ProcessStartInfo, string?, CancellationToken, Task<AdbCommandResult>> run,
        int? discoveryPort = null,
        TimeSpan? connectAfterPair = null,
        Func<string, CancellationToken, Task<bool>>? canReach = null)
    {
        _logger = logger;
        _resolveAdb = resolveAdb;
        _run = run;
        _discoveryPort = discoveryPort;
        _connectAfterPair = connectAfterPair ?? TimeSpan.Zero;
        _canReach = canReach;
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
                var guid = PairedGuid.Match(result.Output);
                if (guid.Success && _connectAfterPair > TimeSpan.Zero &&
                    await ConnectPairedDeviceAsync(guid.Groups[1].Value, ct) is { } connected)
                    return connected;
                return new(AndroidWirelessOperationState.Paired,
                    "Paired successfully. Pairing does not guarantee a debugging connection. Use the connection address from the main Wireless debugging screen if the device is not connected automatically.");
            }

            // adb only reports "protocol fault" when its server can't open the pairing connection. If Sherpa itself can
            // reach the device, the server process is the one being blocked (macOS Local Network privacy).
            if (IsUnreachable(result) ||
                (Mentions(result, "protocol fault") && _canReach != null && await _canReach(address, ct)))
                return LocalNetworkBlocked() ?? Fail(DescribeFailure(result, failureMessage, code));
            return Fail(DescribeFailure(result, failureMessage, code));
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or TimeoutException)
        {
            return Fail(OperationError(ex));
        }
    }

    // adb normally auto-connects a newly paired device through mDNS, but not when its discovery has gone stale.
    // Returns null when the device's debugging service can't be found so the caller reports a plain pairing.
    private async Task<AndroidWirelessOperationResult?> ConnectPairedDeviceAsync(string guid, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + _connectAfterPair;
        try
        {
            while (true)
            {
                var devices = await ExecuteAsync(["devices"], null, ct);
                if (devices.ExitCode == 0 && devices.Output.Split('\n').Any(line =>
                        line.StartsWith(guid + ".", StringComparison.Ordinal) && line.TrimEnd().EndsWith("device", StringComparison.Ordinal)))
                    return new(AndroidWirelessOperationState.Connected, "Paired and connected for debugging.");

                var service = (await DiscoverAsync(ct)).Services.FirstOrDefault(s =>
                    !s.IsPairing && string.Equals(s.Name, guid, StringComparison.Ordinal));
                if (service != null)
                {
                    var connection = await ConnectAsync(service.Endpoint, ct);
                    return connection.State switch
                    {
                        AndroidWirelessOperationState.Connected => new(AndroidWirelessOperationState.Connected, "Paired and connected for debugging."),
                        AndroidWirelessOperationState.ConnectionPending => connection,
                        _ => null
                    };
                }

                if (DateTimeOffset.UtcNow >= deadline)
                    return null;
                await Task.Delay(1000, ct);
            }
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or TimeoutException)
        {
            _logger.LogWarning("Android wireless pairing succeeded, but connecting afterwards failed.");
            return null;
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
            {
                if (IsUnreachable(result) && LocalNetworkBlocked() is { } blocked)
                    return blocked;
                return Fail(DescribeFailure(result, "Connection failed. Check the debugging port, enable Wireless debugging, and make sure both devices are on the same network."));
            }

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
        var main = await DiscoverWithAsync(null, ct);
        if (_discoveryPort is not int port)
            return main;

        Interlocked.Exchange(ref _discoveryServerStarted, 1);
        var fresh = await DiscoverWithAsync(port, ct);
        if (!fresh.Success)
            return main;
        if (!main.Success)
            return fresh;
        return new(true, main.Services.Concat(fresh.Services).Distinct().ToArray());
    }

    public async Task StopDiscoveryAsync()
    {
        if (_discoveryPort is not int port || Interlocked.Exchange(ref _discoveryServerStarted, 0) == 0)
            return;
        try
        {
            await ExecuteAsync(["kill-server"], null, CancellationToken.None, port);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or TimeoutException)
        {
            _logger.LogDebug("The private Android discovery server was already stopped.");
        }
    }

    private async Task<AndroidWirelessDiscoveryResult> DiscoverWithAsync(int? serverPort, CancellationToken ct)
    {
        try
        {
            var result = await ExecuteAsync(["mdns", "services"], null, ct, serverPort);
            if (result.ExitCode != 0 || result.Output.Contains("unknown command", StringComparison.OrdinalIgnoreCase) ||
                result.Output.Contains("ERROR:", StringComparison.OrdinalIgnoreCase))
            {
                LogDiscoveryFailure(serverPort);
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
            LogDiscoveryFailure(serverPort);
            return new(false, [], OperationError(ex));
        }
    }

    private void LogDiscoveryFailure(int? serverPort)
    {
        if (serverPort == null)
            _logger.LogWarning("Android wireless discovery failed.");
        else
            _logger.LogDebug("Private Android discovery server did not answer.");
    }

    /// <summary>True for adb serials of Wi-Fi transports: mDNS service names or an explicit host:port.</summary>
    public static bool IsWirelessSerial(string serial) =>
        serial.Contains("._adb-tls-connect.", StringComparison.Ordinal) ||
        serial.Contains("._adb._tcp", StringComparison.Ordinal) ||
        (serial.Contains(':') && TryNormalizeEndpoint(serial, out _));

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

    private async Task<AdbCommandResult> ExecuteAsync(IReadOnlyList<string> arguments, string? input, CancellationToken ct,
        int? discoveryServerPort = null)
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
        if (discoveryServerPort is int port)
        {
            startInfo.ArgumentList.Add("-P");
            startInfo.ArgumentList.Add(port.ToString(CultureInfo.InvariantCulture));
            // Read by the server this command auto-starts: never claim USB devices, scan emulators, or connect anything.
            startInfo.Environment["ADB_USB"] = "0";
            startInfo.Environment["ADB_EMU"] = "0";
            startInfo.Environment["ADB_MDNS_AUTO_CONNECT"] = "0";
            // adb's built-in (Openscreen) browser misses services announced after it starts, such as the one a phone
            // advertises right after scanning a QR code. macOS always runs mDNSResponder, so use it for live results.
            if (OperatingSystem.IsMacOS())
                startInfo.Environment["ADB_MDNS_OPENSCREEN"] = "0";
        }
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

    public async Task<bool> RestartAdbServerAsync(CancellationToken ct = default)
    {
        try
        {
            await ExecuteAsync(["kill-server"], null, ct);
            var start = await ExecuteAsync(["start-server"], null, ct);
            if (start.ExitCode == 0)
            {
                _logger.LogInformation("Restarted the ADB server from Sherpa.");
                return true;
            }
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or TimeoutException)
        {
        }
        _logger.LogWarning("Restarting the ADB server failed.");
        return false;
    }

    private static bool Mentions(AdbCommandResult result, string text) =>
        result.Output.Contains(text, StringComparison.OrdinalIgnoreCase) || result.Error.Contains(text, StringComparison.OrdinalIgnoreCase);

    private static bool IsUnreachable(AdbCommandResult result) => Mentions(result, "No route to host");

    // On macOS, "No route to host" from a reachable LAN device means the process that launched the adb server
    // was denied Local Network access. Restarting adb from Sherpa makes Sherpa responsible for the new server.
    private AndroidWirelessOperationResult? LocalNetworkBlocked()
    {
        if (!OperatingSystem.IsMacOS())
            return null;
        _logger.LogWarning("macOS blocked the ADB server from reaching the local network.");
        return new(AndroidWirelessOperationState.Failed,
            "macOS blocked ADB from reaching this device on your local network. This happens when ADB was started by an app without Local Network access. Restart ADB from Sherpa, allow local network access if macOS asks, then try again.")
        {
            LocalNetworkBlocked = true
        };
    }

    internal static async Task<bool> CanReachAsync(string endpoint, CancellationToken ct)
    {
        var separator = endpoint.LastIndexOf(':');
        if (separator <= 0 || !int.TryParse(endpoint.AsSpan(separator + 1), out var port))
            return false;
        var host = endpoint[..separator].Trim('[', ']');
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(host, port, timeout.Token);
            return true;
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return false;
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
