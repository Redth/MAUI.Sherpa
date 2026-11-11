using System.Diagnostics;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

internal interface IAppleDevicePairingRunner
{
    bool IsWirelessAvailable { get; }
    Task<JsonDocument> RunDeviceCtlAsync(string[] arguments, int timeoutSeconds, CancellationToken ct);
    Task RunWirelessAsync(IProgress<ApplePairingProgress> progress, int timeoutSeconds, CancellationToken ct);
}

internal sealed class AppleDevicePairingException(string message) : Exception(message);

internal sealed class AppleDevicePairingRunner : IAppleDevicePairingRunner
{
    private readonly string _helperPath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "MacOS", "sherpa-apple-pairing"));

    public bool IsWirelessAvailable => OperatingSystem.IsMacOS() &&
        File.Exists(_helperPath) &&
        Directory.Exists("/Library/Apple/System/Library/PrivateFrameworks/RemotePairing.framework");

    public async Task<JsonDocument> RunDeviceCtlAsync(string[] arguments, int timeoutSeconds, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds + 5));
        var directory = Directory.CreateTempSubdirectory("sherpa-apple-pairing-");
        try
        {
            var path = Path.Combine(directory.FullName, "result.json");
            var start = CreateStartInfo("/usr/bin/xcrun");
            start.ArgumentList.Add("devicectl");
            foreach (var argument in arguments)
                start.ArgumentList.Add(argument);
            foreach (var argument in new[] { "--timeout", timeoutSeconds.ToString(
                System.Globalization.CultureInfo.InvariantCulture), "--json-output", path, "--quiet" })
                start.ArgumentList.Add(argument);

            using var process = Process.Start(start) ??
                throw new AppleDevicePairingException("Could not start Apple's device tools. Check your Xcode installation.");
            // CLI output can contain authentication prompts; it is not a log or a UI channel.
            var output = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
            var error = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
            try
            {
                await process.WaitForExitAsync(deadline.Token);
                await Task.WhenAll(output, error);
                if (!File.Exists(path))
                    throw new AppleDevicePairingException(
                        "Apple's device tools returned no result. Check that Xcode is installed and selected with xcode-select.");
                return ParseDeviceCtlResponse(await File.ReadAllTextAsync(path, deadline.Token), process.ExitCode);
            }
            finally
            {
                await StopProcessAsync(process, graceful: false);
                await Task.WhenAll(output, error);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AppleDevicePairingException(
                "Apple's device command timed out. Pairing may have changed; refresh devices before retrying.");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    internal static JsonDocument ParseDeviceCtlResponse(string json, int exitCode)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(json); }
        catch (JsonException)
        {
            throw new AppleDevicePairingException("Apple's device tools returned unreadable JSON.");
        }

        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object &&
            info.TryGetProperty("outcome", out var outcome) && outcome.ValueKind == JsonValueKind.String &&
            outcome.GetString() == "success" && exitCode == 0 &&
            !root.TryGetProperty("error", out _) && root.TryGetProperty("result", out _))
            return document;

        var message = "Apple's device command failed. Unlock the device, complete Apple's prompts, then refresh before retrying.";
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error))
        {
            var description = ErrorDescription(error, 0);
            if (!string.IsNullOrWhiteSpace(description))
                message = description + " Refresh devices before retrying.";
        }
        document.Dispose();
        throw new AppleDevicePairingException(message);
    }

    private static string? ErrorDescription(JsonElement error, int depth)
    {
        if (depth > 8 || error.ValueKind != JsonValueKind.Object)
            return null;
        if (error.TryGetProperty("error", out var nested))
            return ErrorDescription(nested, depth + 1);
        if (!error.TryGetProperty("userInfo", out var info) || info.ValueKind != JsonValueKind.Object)
            return null;
        string? text = null;
        if (info.TryGetProperty("NSLocalizedDescription", out var description))
        {
            if (description.ValueKind == JsonValueKind.Object && description.TryGetProperty("string", out var value))
                description = value;
            if (description.ValueKind == JsonValueKind.String)
                text = description.GetString();
        }
        if (info.TryGetProperty("NSUnderlyingError", out var underlying))
        {
            var cause = ErrorDescription(underlying, depth + 1);
            if (cause != null) text = text == null ? cause : $"{text} ({cause})";
        }
        return text;
    }

    public async Task RunWirelessAsync(IProgress<ApplePairingProgress> progress, int timeoutSeconds, CancellationToken ct)
    {
        if (!IsWirelessAvailable)
            throw new AppleDevicePairingException("The private Apple Wi-Fi pairing helper is unavailable. Use USB pairing instead.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds + 5));
        var start = CreateStartInfo(_helperPath);
        start.RedirectStandardInput = true;
        foreach (var argument in new[] { "--advertise", "--confirm", "--timeout",
            timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ??
            throw new AppleDevicePairingException("Could not start the Apple Wi-Fi pairing helper.");
        var error = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
        var success = false;
        var codeShown = false;
        try
        {
            while (await process.StandardOutput.ReadLineAsync(deadline.Token) is { } line)
            {
                var update = ParseWirelessEvent(line);
                if (success)
                    throw new AppleDevicePairingException("The private Apple helper returned an event after completion. Refresh devices to check their state.");
                switch (update.Kind)
                {
                    case "advertising":
                        progress.Report(new() { Stage = ApplePairingStage.Advertising });
                        break;
                    case "code":
                        codeShown = true;
                        progress.Report(new()
                        {
                            Stage = ApplePairingStage.Code,
                            PairingCode = update.Code,
                            RequestingDevice = update.Device
                        });
                        break;
                    case "success":
                        if (!codeShown)
                            throw new AppleDevicePairingException("Apple ended pairing without a verified code event. Refresh devices to check their state.");
                        success = true;
                        progress.Report(new() { Stage = ApplePairingStage.Verifying });
                        break;
                    case "timedOut":
                        throw new AppleDevicePairingException("Wi-Fi pairing expired after three minutes. The Mac is no longer discoverable. Refresh before starting again.");
                    case "cancelled":
                        ct.ThrowIfCancellationRequested();
                        throw new AppleDevicePairingException("Wi-Fi pairing stopped. Refresh devices before retrying.");
                    case "error":
                        throw new AppleDevicePairingException("The private Apple pairing interface failed or is incompatible with this Mac. Use USB pairing, or refresh before trying again.");
                }
            }
            await process.WaitForExitAsync(deadline.Token);
            if (!success || process.ExitCode != 0)
                throw new AppleDevicePairingException("Apple did not confirm Wi-Fi pairing. Refresh devices before retrying.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AppleDevicePairingException("The Wi-Fi pairing helper timed out. Refresh devices before retrying.");
        }
        finally
        {
            await StopProcessAsync(process, graceful: true);
            await error;
        }
    }

    // Keep sensitive events out of ProcessExecutionService, terminal history, and exception text.
    internal static (string Kind, string? Code, string? Device) ParseWirelessEvent(string line)
    {
        try
        {
            if (line.Length > 4096)
                throw new JsonException();
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String)
                throw new JsonException();
            var type = kind.GetString();
            if (type is "advertising" or "success" or "timedOut" or "cancelled" or "error")
                return (type, null, null);
            if (type != "code" || !root.TryGetProperty("code", out var code) ||
                code.ValueKind != JsonValueKind.String || !IsSafeDisplayText(code.GetString(), 128))
                throw new JsonException();
            string? device = null;
            if (root.TryGetProperty("device", out var name) && name.ValueKind == JsonValueKind.String &&
                IsSafeDisplayText(name.GetString(), 256))
                device = name.GetString();
            return (type, code.GetString(), device);
        }
        catch (JsonException)
        {
            throw new AppleDevicePairingException("The private Apple pairing helper returned an unsupported event. No pairing success is assumed.");
        }
    }

    private static bool IsSafeDisplayText(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum &&
        !value.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format);

    private static ProcessStartInfo CreateStartInfo(string fileName) => new(fileName)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    private static async Task StopProcessAsync(Process process, bool graceful)
    {
        if (!process.HasExited && graceful)
        {
            process.StandardInput.Close();
            using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await process.WaitForExitAsync(grace.Token); }
            catch (OperationCanceledException) when (grace.IsCancellationRequested) { }
        }
        if (!process.HasExited)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (process.HasExited) { }
            await process.WaitForExitAsync();
        }
    }
}
