using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MauiSherpa.Core.Services;

internal static class AndroidAvdInstanceLock
{
    private const int LockExclusive = 2;
    private const int LockNonBlocking = 4;

    public static Task<IDisposable?> AcquireAsync(string avdPath, CancellationToken cancellationToken) =>
        AcquireAsync(avdPath, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1), cancellationToken);

    internal static async Task<IDisposable?> AcquireAsync(
        string avdPath, TimeSpan timeout, TimeSpan pollInterval, CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handle = TryAcquire(avdPath);
            if (handle != null || elapsed.Elapsed >= timeout)
                return handle;

            await Task.Delay(pollInterval, cancellationToken);
        }
    }

    internal static IDisposable? TryAcquire(string avdPath)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() &&
            !OperatingSystem.IsMacOS() && !OperatingSystem.IsMacCatalyst())
            throw new PlatformNotSupportedException("Android emulator instance locks are not supported on this platform.");

        if (string.IsNullOrWhiteSpace(avdPath) || !Directory.Exists(avdPath))
            throw new DirectoryNotFoundException("The emulator directory could not be found.");

        var lockPath = Path.Combine(avdPath, "multiinstance.lock");
        FileStream stream;
        try
        {
            stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex) when (IsSharingViolation(ex))
        {
            return null;
        }

        if (OperatingSystem.IsWindows())
            return stream;

        // The emulator uses flock, including a shared lock for read-only instances.
        // FileStream's Unix locking is best-effort, so verify it explicitly.
        int result;
        int error;
        do
        {
            result = Flock(stream.SafeFileHandle.DangerousGetHandle().ToInt32(), LockExclusive | LockNonBlocking);
            error = result == 0 ? 0 : Marshal.GetLastPInvokeError();
        } while (result != 0 && error == 4); // EINTR

        if (result == 0)
            return stream;

        stream.Dispose();
        if (error is 11 or 35) // EWOULDBLOCK on Linux or macOS
            return null;

        throw new IOException($"Could not check the emulator instance lock (error {error}).");
    }

    private static bool IsSharingViolation(IOException ex) =>
        OperatingSystem.IsWindows()
            ? (ex.HResult & 0xFFFF) is 32 or 33
            : ex.HResult is 11 or 35;

    [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static extern int Flock(int descriptor, int operation);
}
