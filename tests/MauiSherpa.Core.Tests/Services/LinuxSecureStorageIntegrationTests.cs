using System.Runtime.InteropServices;
using MauiSherpa.Services;
using static MauiSherpa.Services.LibSecretInterop;

namespace MauiSherpa.Core.Tests.Services;

[CollectionDefinition("Linux Secret Service", DisableParallelization = true)]
public sealed class LinuxSecretServiceCollection;

[Collection("Linux Secret Service")]
public class LinuxSecureStorageIntegrationTests
{
    [LinuxSecretServiceFact]
    [Trait("Category", "Keyring")]
    public async Task NativeRoundTripUpdatesUnicodeAndEmptyValues()
    {
        using var storage = new LinuxSecureStorage(NewApplicationId());
        try
        {
            Assert.Null(await storage.GetAsync("missing"));
            Assert.False(storage.Remove("missing"));
            foreach (var value in new[] { "first", "", "  ", "secret-\u00e9-\U0001f512" })
            {
                await storage.SetAsync("key-\u00e9-\U0001f512", value);
                Assert.Equal(value, await storage.GetAsync("key-\u00e9-\U0001f512"));
            }
            Assert.True(storage.Remove("key-\u00e9-\U0001f512"));
            Assert.Null(await storage.GetAsync("key-\u00e9-\U0001f512"));
        }
        finally
        {
            storage.RemoveAll();
        }
    }

    [LinuxSecretServiceFact]
    [Trait("Category", "Keyring")]
    public async Task NativeValuesPersistAcrossInstancesAndDeletionIsAppScoped()
    {
        var applicationId = NewApplicationId();
        using var first = new LinuxSecureStorage(applicationId);
        using var reopened = new LinuxSecureStorage(applicationId);
        using var otherApp = new LinuxSecureStorage(NewApplicationId());
        try
        {
            await first.SetAsync("shared", "first");
            await first.SetAsync("another", "another");
            await otherApp.SetAsync("shared", "second");
            Assert.Equal("first", await reopened.GetAsync("shared"));
            Assert.True(reopened.Remove("shared"));
            Assert.Equal("second", await otherApp.GetAsync("shared"));
            first.RemoveAll();
            Assert.Null(await reopened.GetAsync("another"));
            Assert.Equal("second", await otherApp.GetAsync("shared"));
            first.RemoveAll();
        }
        finally
        {
            first.RemoveAll();
            otherApp.RemoveAll();
        }
    }

    [LinuxSecretServiceFact]
    [Trait("Category", "Keyring")]
    public async Task NativeOperationsWorkAcrossConcurrentInstances()
    {
        var applicationId = NewApplicationId();
        using var first = new LinuxSecureStorage(applicationId);
        using var second = new LinuxSecureStorage(applicationId);
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 16).Select(async i =>
            {
                var storage = i % 2 == 0 ? first : second;
                await storage.SetAsync($"key-{i}", $"value-{i}");
                Assert.Equal($"value-{i}", await storage.GetAsync($"key-{i}"));
            }));
        }
        finally
        {
            first.RemoveAll();
        }
    }

    [LinuxSecretServiceFact("SHERPA_SECURE_STORAGE_LOCK_TEST")]
    [Trait("Category", "LockedKeyring")]
    public async Task LockedKeyringFailsWithoutDeletingAnyMatchingItems()
    {
        var applicationId = NewApplicationId();
        using var storage = new LinuxSecureStorage(applicationId);
        await storage.SetAsync("locked", "must-survive");
        StoreInSessionCollection(applicationId, "unlocked", "must-not-be-partially-deleted");
        LockDefaultCollection();

        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.GetAsync("locked"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SetAsync("locked", "replacement"));
        Assert.Throws<InvalidOperationException>(() => storage.Remove("locked"));
        Assert.Throws<InvalidOperationException>(storage.RemoveAll);
        Assert.Equal("must-not-be-partially-deleted", await storage.GetAsync("unlocked"));
        Assert.True(storage.Remove("unlocked"));
        using var service = OpenService();
        using var schema = CreateSchema();
        using var attributes = new AttributeTableHandle();
        attributes.Add("application", applicationId);
        attributes.Add("key", "locked");
        using var remaining = new ItemListHandle(
            SecretServiceSearch(service, schema, attributes, SearchAll, IntPtr.Zero, out var error));
        CheckError(error);
        Assert.True(SecretItemGetLocked(Assert.Single(remaining.Items)));
    }

    [LinuxSecretServiceFact("SHERPA_SECURE_STORAGE_NO_SERVICE_TEST")]
    [Trait("Category", "NoSecretService")]
    public async Task UnavailableServiceFailsEveryOperation()
    {
        using var storage = new LinuxSecureStorage(NewApplicationId());
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.GetAsync("key"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SetAsync("key", "value"));
        Assert.Throws<InvalidOperationException>(() => storage.Remove("key"));
        Assert.Throws<InvalidOperationException>(storage.RemoveAll);
    }

    private static string NewApplicationId() => $"codes.redth.mauisherpa.tests.{Guid.NewGuid():N}";

    private static GObjectHandle OpenService()
    {
        var service = new GObjectHandle(SecretServiceOpen(SecretServiceGetType(), IntPtr.Zero, 0, IntPtr.Zero, out var error));
        CheckError(error);
        return service;
    }

    private static SecretSchemaHandle CreateSchema()
    {
        using var attributes = new AttributeTableHandle();
        attributes.Add("application", null);
        attributes.Add("key", null);
        return new SecretSchemaHandle(SecretSchemaNew(LibSecretStore.SchemaName, 0, attributes));
    }

    private static void StoreInSessionCollection(string applicationId, string key, string value)
    {
        using var service = OpenService();
        using var schema = CreateSchema();
        using var attributes = new AttributeTableHandle();
        attributes.Add("application", applicationId);
        attributes.Add("key", key);
        using var secret = new SecretValueHandle(SecretValueNew(value, -1, "text/plain"));
        var collection = Marshal.StringToCoTaskMemUTF8("session");
        try
        {
            var stored = SecretServiceStore(service, schema, attributes, collection, applicationId, secret, IntPtr.Zero, out var error);
            CheckError(error);
            Assert.True(stored);
        }
        finally
        {
            Marshal.FreeCoTaskMem(collection);
        }
    }

    private static void LockDefaultCollection()
    {
        using var service = OpenService();
        using var collection = new GObjectHandle(CollectionForAlias(service, "default", 0, IntPtr.Zero, out var error));
        CheckError(error);
        Assert.False(collection.IsInvalid);
        var input = ListAppend(IntPtr.Zero, collection.DangerousGetHandle());
        try
        {
            var count = ServiceLock(service, input, IntPtr.Zero, out var locked, out error);
            using var output = new ItemListHandle(locked);
            CheckError(error);
            Assert.True(count > 0);
        }
        finally
        {
            ListFree(input);
        }
    }

    private static void CheckError(IntPtr error)
    {
        if (error == IntPtr.Zero)
            return;
        GErrorFree(error);
        throw new InvalidOperationException("The disposable Secret Service test setup failed.");
    }

    [DllImport("libsecret-1.so.0", EntryPoint = "secret_collection_for_alias_sync", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr CollectionForAlias(
        GObjectHandle service, [MarshalAs(UnmanagedType.LPUTF8Str)] string alias, int flags, IntPtr cancellable, out IntPtr error);

    [DllImport("libsecret-1.so.0", EntryPoint = "secret_service_lock_sync", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ServiceLock(
        GObjectHandle service, IntPtr objects, IntPtr cancellable, out IntPtr locked, out IntPtr error);

    [DllImport("libglib-2.0.so.0", EntryPoint = "g_list_append", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ListAppend(IntPtr list, IntPtr data);

    [DllImport("libglib-2.0.so.0", EntryPoint = "g_list_free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void ListFree(IntPtr list);
}

public sealed class LinuxSecretServiceFactAttribute : FactAttribute
{
    public LinuxSecretServiceFactAttribute(string enabledVariable = "SHERPA_SECURE_STORAGE_KEYRING_TEST")
    {
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable(enabledVariable) != "1")
            Skip = "Requires an explicitly enabled, disposable Linux Secret Service session.";
    }
}
