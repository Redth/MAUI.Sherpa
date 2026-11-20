using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MauiSherpa.Services;

internal static class LibSecretInterop
{
    private const string LibSecret = "libsecret-1.so.0";
    private const string LibGLib = "libglib-2.0.so.0";
    private const string LibGObject = "libgobject-2.0.so.0";
    internal const int SearchAll = 1 << 1;
    internal const int SearchUnlock = 1 << 2;
    internal const int SearchLoadSecrets = 1 << 3;

    private static readonly Lazy<(IntPtr Hash, IntPtr Equal, IntPtr Free)> Functions = new(() =>
    {
        // Keep GLib loaded for the lifetime of these callback pointers.
        var library = NativeLibrary.Load(LibGLib);
        try
        {
            return (NativeLibrary.GetExport(library, "g_str_hash"),
                NativeLibrary.GetExport(library, "g_str_equal"),
                NativeLibrary.GetExport(library, "g_free"));
        }
        catch
        {
            NativeLibrary.Free(library);
            throw;
        }
    });

    [DllImport(LibSecret, EntryPoint = "secret_schema_newv", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SecretSchemaNew(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int flags, AttributeTableHandle attributes);

    [DllImport(LibSecret, EntryPoint = "secret_schema_unref", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SecretSchemaUnref(IntPtr schema);

    [DllImport(LibSecret, EntryPoint = "secret_service_store_sync", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SecretServiceStore(
        GObjectHandle service, SecretSchemaHandle schema, AttributeTableHandle attributes, IntPtr collection,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string label,
        SecretValueHandle value, IntPtr cancellable, out IntPtr error);

    [DllImport(LibSecret, EntryPoint = "secret_service_get_type", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint SecretServiceGetType();

    [DllImport(LibSecret, EntryPoint = "secret_service_open_sync", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SecretServiceOpen(
        nuint serviceType, IntPtr busName, int flags, IntPtr cancellable, out IntPtr error);

    [DllImport(LibSecret, EntryPoint = "secret_service_search_sync", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SecretServiceSearch(
        GObjectHandle service, SecretSchemaHandle schema, AttributeTableHandle attributes,
        int flags, IntPtr cancellable, out IntPtr error);

    [DllImport(LibSecret, EntryPoint = "secret_item_get_locked", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SecretItemGetLocked(IntPtr item);

    [DllImport(LibSecret, EntryPoint = "secret_item_delete_sync", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SecretItemDelete(IntPtr item, IntPtr cancellable, out IntPtr error);

    [DllImport(LibSecret, EntryPoint = "secret_item_get_secret", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SecretItemGetSecret(IntPtr item);

    [DllImport(LibSecret, EntryPoint = "secret_value_get_text", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SecretValueGetText(SecretValueHandle value);

    [DllImport(LibSecret, EntryPoint = "secret_value_new", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SecretValueNew(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string text, nint length,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string contentType);

    [DllImport(LibSecret, EntryPoint = "secret_value_unref", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SecretValueUnref(IntPtr value);

    [DllImport(LibGObject, EntryPoint = "g_object_unref", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GObjectUnref(IntPtr instance);

    [DllImport(LibGLib, EntryPoint = "g_hash_table_new_full", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr GHashTableNew(IntPtr hash, IntPtr equal, IntPtr destroyKey, IntPtr destroyValue);

    [DllImport(LibGLib, EntryPoint = "g_hash_table_insert", CallingConvention = CallingConvention.Cdecl)]
    private static extern int GHashTableInsert(AttributeTableHandle table, IntPtr key, IntPtr value);

    [DllImport(LibGLib, EntryPoint = "g_hash_table_destroy", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GHashTableDestroy(IntPtr table);

    [DllImport(LibGLib, EntryPoint = "g_strdup", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr GStrDup([MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(LibGLib, EntryPoint = "g_free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GFree(IntPtr memory);

    [DllImport(LibGLib, EntryPoint = "g_list_free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GListFree(IntPtr list);

    [DllImport(LibGLib, EntryPoint = "g_error_free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void GErrorFree(IntPtr error);

    [StructLayout(LayoutKind.Sequential)]
    internal struct GError
    {
        internal uint Domain;
        internal int Code;
        internal IntPtr Message;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GList
    {
        internal IntPtr Data;
        internal IntPtr Next;
        internal IntPtr Previous;
    }

    internal sealed class SecretSchemaHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal SecretSchemaHandle(IntPtr schema) : base(true) => SetHandle(schema);

        protected override bool ReleaseHandle()
        {
            SecretSchemaUnref(handle);
            return true;
        }
    }

    internal sealed class GObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal GObjectHandle(IntPtr instance) : base(true) => SetHandle(instance);

        protected override bool ReleaseHandle()
        {
            GObjectUnref(handle);
            return true;
        }
    }

    internal sealed class SecretValueHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal SecretValueHandle(IntPtr value) : base(true) => SetHandle(value);

        protected override bool ReleaseHandle()
        {
            SecretValueUnref(handle);
            return true;
        }
    }

    internal sealed class AttributeTableHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal AttributeTableHandle() : base(true)
        {
            var functions = Functions.Value;
            SetHandle(GHashTableNew(functions.Hash, functions.Equal, functions.Free, functions.Free));
            if (IsInvalid)
                throw new OutOfMemoryException("GLib could not allocate an attribute table.");
        }

        internal void Add(string name, string? value)
        {
            var namePointer = GStrDup(name);
            var valuePointer = IntPtr.Zero;
            try
            {
                if (namePointer == IntPtr.Zero)
                    throw new OutOfMemoryException();
                if (value is not null)
                {
                    valuePointer = GStrDup(value);
                    if (valuePointer == IntPtr.Zero)
                        throw new OutOfMemoryException();
                }

                _ = GHashTableInsert(this, namePointer, valuePointer);
                namePointer = IntPtr.Zero;
                valuePointer = IntPtr.Zero;
            }
            finally
            {
                GFree(namePointer);
                GFree(valuePointer);
            }
        }

        protected override bool ReleaseHandle()
        {
            GHashTableDestroy(handle);
            return true;
        }
    }

    internal sealed class ItemListHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal ItemListHandle(IntPtr list) : base(true) => SetHandle(list);

        internal IEnumerable<IntPtr> Items
        {
            get
            {
                for (var node = handle; node != IntPtr.Zero;)
                {
                    var item = Marshal.PtrToStructure<GList>(node);
                    yield return item.Data;
                    node = item.Next;
                }
            }
        }

        protected override bool ReleaseHandle()
        {
            foreach (var item in Items)
                GObjectUnref(item);
            GListFree(handle);
            return true;
        }
    }
}
