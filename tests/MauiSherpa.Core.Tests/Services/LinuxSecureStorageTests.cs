using MauiSherpa.Services;
using Microsoft.Maui.Storage;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class LinuxSecureStorageTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" \t")]
    [InlineData("key\0suffix")]
    public void RejectsInvalidIdentifiersBeforeCallingNativeStorage(string text)
    {
        var store = new RecordingStore();
        Assert.Throws<ArgumentException>(() => new LinuxSecureStorage(text, store));
        using var storage = new LinuxSecureStorage("app", store);
        Assert.Throws<ArgumentException>(() => { _ = storage.GetAsync(text); });
        Assert.Throws<ArgumentException>(() => { _ = storage.SetAsync(text, "value"); });
        Assert.Throws<ArgumentException>(() => storage.Remove(text));
        Assert.Empty(store.Calls);
    }

    [Theory]
    [InlineData(0xd800)]
    [InlineData(0xdc00)]
    public void RejectsMalformedUnicodeBeforeNativeMarshalling(int codeUnit)
    {
        var text = new string((char)codeUnit, 1);
        var store = new RecordingStore();
        Assert.Throws<ArgumentException>(() => new LinuxSecureStorage(text, store));
        using var storage = new LinuxSecureStorage("app", store);
        Assert.Throws<ArgumentException>(() => { _ = storage.GetAsync(text); });
        Assert.Throws<ArgumentException>(() => { _ = storage.SetAsync(text, "value"); });
        Assert.Throws<ArgumentException>(() => { _ = storage.SetAsync("key", text); });
        Assert.Throws<ArgumentException>(() => storage.Remove(text));
        Assert.Empty(store.Calls);
    }

    [Fact]
    public void RejectsNullsAndNulTerminatedValues()
    {
        var store = new RecordingStore();
        Assert.Throws<ArgumentNullException>(() => new LinuxSecureStorage(null!, store));
        Assert.Throws<ArgumentNullException>(() => new LinuxSecureStorage("app", null!));
        using var storage = new LinuxSecureStorage("app", store);
        Assert.Throws<ArgumentNullException>(() => { _ = storage.GetAsync(null!); });
        Assert.Throws<ArgumentNullException>(() => { _ = storage.SetAsync(null!, "value"); });
        Assert.Throws<ArgumentNullException>(() => { _ = storage.SetAsync("key", null!); });
        Assert.Throws<ArgumentNullException>(() => storage.Remove(null!));
        Assert.Throws<ArgumentException>(() => { _ = storage.SetAsync("key", "value\0suffix"); });
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task ScopesEveryOperationAndPreservesEmptyAndUnicodeValues()
    {
        var store = new RecordingStore();
        using var storage = new LinuxSecureStorage("codes.redth.mauisherpa", store);
        await storage.SetAsync("key", "");
        await storage.SetAsync("unicode-\u00e9-\U0001f512", "value-\u00e9-\U0001f512");
        Assert.Null(await storage.GetAsync("missing"));
        Assert.False(storage.Remove("missing"));
        storage.RemoveAll();
        Assert.Equal(new[]
        {
            ("codes.redth.mauisherpa", (string?)"key", (string?)""),
            ("codes.redth.mauisherpa", "unicode-\u00e9-\U0001f512", "value-\u00e9-\U0001f512"),
            ("codes.redth.mauisherpa", "missing", null),
            ("codes.redth.mauisherpa", "missing", null),
            ("codes.redth.mauisherpa", null, null)
        }, store.Calls);
    }

    [Fact]
    public async Task NativeFailuresAreNotReportedAsMissingValuesOrSuccessfulDeletions()
    {
        var error = new InvalidOperationException("Service unavailable.");
        using var storage = new LinuxSecureStorage("app", new RecordingStore { Error = error });
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => storage.GetAsync("key")));
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SetAsync("key", "value")));
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => storage.Remove("key")));
        Assert.Same(error, Assert.Throws<InvalidOperationException>(storage.RemoveAll));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingNativeDependenciesProduceAnActionableError(bool missingLibrary)
    {
        Exception error = missingLibrary ? new DllNotFoundException() : new EntryPointNotFoundException();
        using var storage = new LinuxSecureStorage("app", new RecordingStore { Error = error });
        var failure = await Assert.ThrowsAsync<PlatformNotSupportedException>(() => storage.GetAsync("key"));
        Assert.Same(error, failure.InnerException);
    }

    [Fact]
    public async Task DisposalRejectsFurtherNativeOperations()
    {
        var store = new RecordingStore();
        var storage = new LinuxSecureStorage("app", store);
        storage.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => storage.GetAsync("key"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => storage.SetAsync("key", "value"));
        Assert.Throws<ObjectDisposedException>(() => storage.Remove("key"));
        Assert.Throws<ObjectDisposedException>(storage.RemoveAll);
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task LinuxLegacyFacadeDoesNotSwallowFailuresOrFallBackToFilesEvenInDebug()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var error = new InvalidOperationException("Locked keyring.");
        var platform = new Mock<ISecureStorage>(MockBehavior.Strict);
        platform.Setup(s => s.GetAsync("missing")).ReturnsAsync((string?)null);
        platform.Setup(s => s.GetAsync("locked")).ThrowsAsync(error);
        platform.Setup(s => s.SetAsync("locked", "value")).ThrowsAsync(error);
        platform.Setup(s => s.Remove("locked")).Throws(error);
        var legacy = new SecureStorageService(platform.Object);
        Assert.Null(await legacy.GetAsync("missing"));
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => legacy.GetAsync("locked")));
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => legacy.SetAsync("locked", "value")));
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => legacy.RemoveAsync("locked")));
    }

    private sealed class RecordingStore : ILinuxSecretStore
    {
        internal List<(string Application, string? Key, string? Value)> Calls { get; } = [];
        internal Exception? Error { get; init; }

        public string? Get(string applicationId, string key)
        {
            Record(applicationId, key, null);
            return null;
        }

        public void Set(string applicationId, string key, string value) => Record(applicationId, key, value);

        public bool Remove(string applicationId, string? key)
        {
            Record(applicationId, key, null);
            return false;
        }

        private void Record(string applicationId, string? key, string? value)
        {
            if (Error is not null)
                throw Error;
            Calls.Add((applicationId, key, value));
        }
    }
}
