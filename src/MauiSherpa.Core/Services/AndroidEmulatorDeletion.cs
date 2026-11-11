using System.Diagnostics;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public static class AndroidEmulatorShutdown
{
    public static Task<bool> RunAsync(
        IAndroidSdkService sdk, IAndroidAdbService adb, string serial, string? expectedAvdName,
        IOperationContext context) =>
        RunAsync(sdk, adb, serial, expectedAvdName, context, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1));

    internal static async Task<bool> RunAsync(
        IAndroidSdkService sdk, IAndroidAdbService adb, string serial, string? expectedAvdName,
        IOperationContext context, TimeSpan timeout, TimeSpan pollInterval)
    {
        try
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var snapshot = await adb.GetSnapshotAsync(context.CancellationToken);
            var instance = snapshot.Emulators.FirstOrDefault(e => e.Serial == serial);
            if (instance == null)
            {
                context.LogInfo($"Emulator {serial} is already stopped.");
                return true;
            }
            if (expectedAvdName != null && instance.AvdName != expectedAvdName)
            {
                context.LogError($"Could not verify which emulator is using {serial}. It was not stopped.");
                return false;
            }

            context.SetStatus($"Stopping {serial}...");
            if (!await sdk.StopEmulatorAsync(serial))
            {
                context.LogError($"Could not stop emulator {serial}.");
                return false;
            }

            var elapsed = Stopwatch.StartNew();
            while (true)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                snapshot = await adb.GetSnapshotAsync(context.CancellationToken);
                instance = snapshot.Emulators.FirstOrDefault(e => e.Serial == serial);
                if (instance == null)
                {
                    context.LogSuccess($"Emulator {serial} stopped.");
                    return true;
                }
                if (expectedAvdName != null && instance.AvdName != expectedAvdName)
                {
                    context.LogError($"The identity of emulator {serial} changed while stopping it.");
                    return false;
                }
                if (elapsed.Elapsed >= timeout)
                {
                    context.LogError($"Emulator {serial} did not finish stopping.");
                    return false;
                }
                await Task.Delay(pollInterval, context.CancellationToken);
            }
        }
        catch (AndroidAdbException ex)
        {
            context.LogError($"Could not verify emulator shutdown: {ex.Message}");
            return false;
        }
    }
}

public static class AndroidEmulatorDeletion
{
    public static Task<bool> RunAsync(
        IAndroidSdkService sdk, IAndroidAdbService adb, AvdInfo emulator, IOperationContext context) =>
        RunAsync(sdk, adb, emulator, context, AndroidAvdInstanceLock.AcquireAsync);

    internal static async Task<bool> RunAsync(
        IAndroidSdkService sdk, IAndroidAdbService adb, AvdInfo emulator, IOperationContext context,
        Func<string, CancellationToken, Task<IDisposable?>> acquireLock)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        context.LogInfo($"Emulator: {emulator.Name}");

        var matches = (await sdk.GetAvdsAsync())
            .Where(avd => avd.Name == emulator.Name).Take(2).ToList();
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (matches.Count != 1 || !string.Equals(matches[0].Path, emulator.Path, pathComparison))
        {
            context.LogError("The emulator could not be verified. Refresh the list before trying again.");
            return false;
        }

        try
        {
            var snapshot = await adb.GetSnapshotAsync(context.CancellationToken);
            if (!AllInstancesIdentified(snapshot, context))
                return false;

            foreach (var instance in snapshot.Emulators.Where(e => e.AvdName == emulator.Name))
            {
                if (!await AndroidEmulatorShutdown.RunAsync(sdk, adb, instance.Serial, emulator.Name, context))
                {
                    context.LogError("The emulator was not deleted because shutdown could not be verified.");
                    return false;
                }
            }

            context.SetStatus("Waiting for the emulator to release its files...");
            using var instanceLock = await acquireLock(emulator.Path, context.CancellationToken);
            if (instanceLock == null)
            {
                context.LogError("The emulator is still using its files. Wait for it to stop and try again. It was not deleted.");
                return false;
            }

            context.CancellationToken.ThrowIfCancellationRequested();
            snapshot = await adb.GetSnapshotAsync(context.CancellationToken);
            if (!AllInstancesIdentified(snapshot, context))
                return false;
            if (snapshot.Emulators.Any(e => e.AvdName == emulator.Name))
            {
                context.LogError("The emulator is still running. It was not deleted.");
                return false;
            }

            context.CancellationToken.ThrowIfCancellationRequested();
            matches = (await sdk.GetAvdsAsync()).Where(avd => avd.Name == emulator.Name).Take(2).ToList();
            if (matches.Count != 1 || !string.Equals(matches[0].Path, emulator.Path, pathComparison))
            {
                context.LogError("The emulator changed while waiting for it to stop. It was not deleted.");
                return false;
            }

            // Windows cannot remove the AVD directory while its lock file is open.
            if (OperatingSystem.IsWindows())
                instanceLock.Dispose();

            context.CancellationToken.ThrowIfCancellationRequested();
            context.SetStatus("Deleting emulator...");
            var success = await sdk.DeleteAvdAsync(emulator.Name);
            if (success)
                context.LogSuccess($"Emulator '{emulator.Name}' deleted.");
            else
                context.LogError($"Could not delete '{emulator.Name}'.");
            return success;
        }
        catch (AndroidAdbException ex)
        {
            context.LogError($"Could not verify running emulators: {ex.Message} The emulator was not deleted.");
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            context.LogError($"Could not verify the emulator's files: {ex.Message} The emulator was not deleted.");
            return false;
        }
    }

    private static bool AllInstancesIdentified(AndroidAdbSnapshot snapshot, IOperationContext context)
    {
        if (snapshot.Emulators.All(e => !string.IsNullOrEmpty(e.AvdName)))
            return true;

        context.LogError("A running emulator could not be identified. Wait for it to finish starting or stop it, then retry. The emulator was not deleted.");
        return false;
    }
}
