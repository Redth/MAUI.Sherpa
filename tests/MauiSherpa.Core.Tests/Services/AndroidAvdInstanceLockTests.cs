using FluentAssertions;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class AndroidAvdInstanceLockTests
{
    [Fact]
    public void StaleLockFile_DoesNotPreventAcquiringExclusiveLock()
    {
        using var avd = new TemporaryAvd();
        File.WriteAllText(avd.LockPath, "stale");

        using var instanceLock = AndroidAvdInstanceLock.TryAcquire(avd.Path);

        instanceLock.Should().NotBeNull();
    }

    [Fact]
    public void ExclusiveLock_PreventsAnotherInstanceFromAcquiringIt()
    {
        using var avd = new TemporaryAvd();
        using var instanceLock = AndroidAvdInstanceLock.TryAcquire(avd.Path);
        instanceLock.Should().NotBeNull();

        using var competingLock = AndroidAvdInstanceLock.TryAcquire(avd.Path);

        competingLock.Should().BeNull();
    }

    [Fact]
    public void SharedLock_PreventsDeletionOfReadOnlyEmulator()
    {
        using var avd = new TemporaryAvd();
        File.WriteAllText(avd.LockPath, "");
        using var sharedLock = new FileStream(avd.LockPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        using var instanceLock = AndroidAvdInstanceLock.TryAcquire(avd.Path);

        instanceLock.Should().BeNull();
    }

    [Fact]
    public async Task AcquireAsync_WaitsForTheLockToBeReleased()
    {
        using var avd = new TemporaryAvd();
        var existingLock = AndroidAvdInstanceLock.TryAcquire(avd.Path);
        existingLock.Should().NotBeNull();

        var pending = AndroidAvdInstanceLock.AcquireAsync(
            avd.Path, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(10), CancellationToken.None);
        pending.IsCompleted.Should().BeFalse();
        existingLock!.Dispose();
        using var instanceLock = await pending;

        instanceLock.Should().NotBeNull();
    }

    [Fact]
    public async Task AcquireAsync_TimesOutWithoutRemovingTheLock()
    {
        using var avd = new TemporaryAvd();
        using var existingLock = AndroidAvdInstanceLock.TryAcquire(avd.Path);

        using var instanceLock = await AndroidAvdInstanceLock.AcquireAsync(
            avd.Path, TimeSpan.Zero, TimeSpan.Zero, CancellationToken.None);

        instanceLock.Should().BeNull();
        File.Exists(avd.LockPath).Should().BeTrue();
    }

    private sealed class TemporaryAvd : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            AppContext.BaseDirectory, $"avd-lock-{Guid.NewGuid():N}.avd");
        public string LockPath => System.IO.Path.Combine(Path, "multiinstance.lock");

        public TemporaryAvd() => Directory.CreateDirectory(Path);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
