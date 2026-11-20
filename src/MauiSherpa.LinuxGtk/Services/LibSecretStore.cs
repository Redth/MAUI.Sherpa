using System.Runtime.InteropServices;
using static MauiSherpa.Services.LibSecretInterop;

namespace MauiSherpa.Services;

internal sealed class LibSecretStore : ILinuxSecretStore
{
    // Keep existing app-scoped libsecret entries readable.
    internal const string SchemaName = "org.maui.gtk.securestorage.v2";

    public string? Get(string applicationId, string key)
    {
        using var schema = CreateSchema();
        using var attributes = CreateAttributes(applicationId, key);
        using var service = GetService();
        using var matches = Search(service, schema, attributes, SearchAll | SearchUnlock | SearchLoadSecrets);
        var items = matches.Items.ToArray();
        if (items.Length == 0)
            return null;
        if (items.Length != 1)
            throw new InvalidOperationException("Multiple secrets match this application and key.");
        if (SecretItemGetLocked(items[0]))
            throw new InvalidOperationException("Secret Service could not unlock the matching item.");

        using var secret = new SecretValueHandle(SecretItemGetSecret(items[0]));
        if (secret.IsInvalid)
            throw new InvalidOperationException("Secret Service did not load the matching secret.");
        var text = SecretValueGetText(secret);
        if (text == IntPtr.Zero)
            throw new InvalidOperationException("The stored secret is not a text value.");
        return Marshal.PtrToStringUTF8(text);
    }

    public void Set(string applicationId, string key, string value)
    {
        using var schema = CreateSchema();
        using var attributes = CreateAttributes(applicationId, key);
        using var service = GetService();
        using var secret = new SecretValueHandle(SecretValueNew(value, -1, "text/plain"));
        if (secret.IsInvalid)
            throw new InvalidOperationException("libsecret could not allocate the secret value.");
        var stored = SecretServiceStore(
            service, schema, attributes, IntPtr.Zero, applicationId, secret, IntPtr.Zero, out var error);
        ThrowIfError(error, "store");
        if (!stored)
            throw new InvalidOperationException("Secret Service did not store the secret.");
    }

    public bool Remove(string applicationId, string? key)
    {
        using var schema = CreateSchema();
        using var attributes = CreateAttributes(applicationId, key);
        using var service = GetService();
        using var matches = Search(service, schema, attributes, SearchAll | SearchUnlock);
        var items = matches.Items.ToArray();
        if (items.Length == 0)
            return false;

        // Password-clear APIs can report success while silently skipping locked items.
        foreach (var item in items)
        {
            if (SecretItemGetLocked(item))
                throw new InvalidOperationException(
                    "Secret Service could not unlock all matching items. No deletion was attempted.");
        }

        foreach (var item in items)
        {
            var deleted = SecretItemDelete(item, IntPtr.Zero, out var error);
            ThrowIfError(error, "delete");
            if (!deleted)
                throw new InvalidOperationException("Secret Service did not delete a matching item.");
        }

        using var remaining = Search(service, schema, attributes, SearchAll);
        if (!remaining.IsInvalid)
            throw new InvalidOperationException(
                "Matching secrets remain after deletion. Another process may have written concurrently.");
        return true;
    }

    private static GObjectHandle GetService()
    {
        // A fresh proxy avoids stale lock state without requiring a caller-owned GLib main loop.
        var service = new GObjectHandle(SecretServiceOpen(SecretServiceGetType(), IntPtr.Zero, 0, IntPtr.Zero, out var error));
        try
        {
            ThrowIfError(error, "connect");
            if (service.IsInvalid)
                throw new InvalidOperationException("Secret Service is unavailable.");
            return service;
        }
        catch
        {
            service.Dispose();
            throw;
        }
    }

    private static SecretSchemaHandle CreateSchema()
    {
        using var attributes = new AttributeTableHandle();
        attributes.Add("application", null);
        attributes.Add("key", null);
        var schema = new SecretSchemaHandle(SecretSchemaNew(SchemaName, 0, attributes));
        if (schema.IsInvalid)
        {
            schema.Dispose();
            throw new InvalidOperationException("libsecret could not create the secure storage schema.");
        }
        return schema;
    }

    private static AttributeTableHandle CreateAttributes(string applicationId, string? key)
    {
        var attributes = new AttributeTableHandle();
        try
        {
            attributes.Add("application", applicationId);
            if (key is not null)
                attributes.Add("key", key);
            return attributes;
        }
        catch
        {
            attributes.Dispose();
            throw;
        }
    }

    private static ItemListHandle Search(
        GObjectHandle service, SecretSchemaHandle schema, AttributeTableHandle attributes, int flags)
    {
        var matches = new ItemListHandle(
            SecretServiceSearch(service, schema, attributes, flags, IntPtr.Zero, out var error));
        try
        {
            ThrowIfError(error, "search");
            return matches;
        }
        catch
        {
            matches.Dispose();
            throw;
        }
    }

    private static void ThrowIfError(IntPtr error, string operation)
    {
        if (error == IntPtr.Zero)
            return;
        try
        {
            var details = Marshal.PtrToStructure<GError>(error);
            // Native messages can contain secret attributes; expose only error codes.
            throw new InvalidOperationException(
                $"Secret Service {operation} failed (domain {details.Domain}, code {details.Code}). " +
                "Check that a session bus and an unlocked Secret Service keyring are available.");
        }
        finally
        {
            GErrorFree(error);
        }
    }
}
