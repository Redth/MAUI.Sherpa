using System.Text.Json;
using System.Text.Json.Serialization;
using MauiSherpa.Bundle.Models;
using Microsoft.Data.Sqlite;
using Shiny.DocumentDb;
using Shiny.DocumentDb.Sqlite.SqlCipher;

namespace MauiSherpa.Bundle.Loading;

/// <summary>
/// Reads and writes a bundle as an encrypted SQLCipher database — the binary,
/// password-protected counterpart to the plain-JSON
/// <see cref="SherpaBundleWriter"/>/<see cref="JsonBundleLoader"/> path.
/// <para>
/// Each environment is persisted as its own document keyed by environment name;
/// the optional top-level <c>Build</c> defaults are a single document. The whole
/// database is encrypted at rest with the caller-supplied password (SQLCipher),
/// so the signing material inlined into the bundle never sits on disk in clear.
/// </para>
/// </summary>
public static class SqlCipherBundleStore
{
    /// <summary>Singleton id for the top-level <c>Build</c> defaults document.</summary>
    internal const string BuildDocumentId = "$build";

    // A context-backed options instance (not the frozen Default) so the document
    // store can use — and adapt — it while staying reflection-free.
    private static readonly BundleJsonContext JsonContext = new(new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    });

    private static SqlCipherDocumentStore CreateStore(string path, string password)
        => new(new DocumentStoreOptions
        {
            DatabaseProvider = new SqlCipherDatabaseProvider(path, password),
            JsonSerializerOptions = JsonContext.Options,
        });

    /// <summary>
    /// Writes <paramref name="bundle"/> to an encrypted SQLCipher database at
    /// <paramref name="path"/>. Any existing file is replaced so a rewrite to
    /// fewer environments never leaves orphaned blocks behind.
    /// </summary>
    public static async Task SaveAsync(SherpaBundle bundle, string path, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(password))
            throw new SherpaBundleException("A password is required to write an encrypted bundle.");

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Start from a clean file: the on-disk environments must exactly mirror
        // the model, with no leftovers from a previous (larger) bundle — and a
        // rewrite may use a brand-new password, so we never reopen the old file.
        // Connection pooling can keep a disposed store's handle (and its rows)
        // alive, so drain the pools before deleting the database + WAL/SHM sidecars.
        SqliteConnection.ClearAllPools();
        DeleteDatabaseFiles(path);

        using var store = CreateStore(path, password);

        if (bundle.Build is not null)
            await store.Insert(new BuildDocument { Build = bundle.Build }, cancellationToken: ct);

        foreach (var (name, env) in bundle.Environments)
            await store.Insert(new EnvironmentDocument { Id = name, Block = env }, cancellationToken: ct);
    }

    /// <summary>
    /// Reads an encrypted SQLCipher bundle written by <see cref="SaveAsync"/>.
    /// Throws <see cref="SherpaBundleException"/> when the password is wrong or
    /// the file is not a readable Sherpa bundle.
    /// </summary>
    public static async Task<SherpaBundle> LoadAsync(string path, string password, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            throw new SherpaBundleException($"Bundle file not found: {path}");
        if (string.IsNullOrEmpty(password))
            throw new SherpaBundleException("This bundle is encrypted; a password is required to read it.");

        try
        {
            using var store = CreateStore(path, password);

            var build = await store.Get<BuildDocument>(BuildDocumentId, cancellationToken: ct);
            var envDocs = await store.Query<EnvironmentDocument>().ToList();

            var environments = new Dictionary<string, EnvironmentBlock>(StringComparer.OrdinalIgnoreCase);
            foreach (var doc in envDocs)
                if (doc.Block is not null)
                    environments[doc.Id] = doc.Block;

            return new SherpaBundle { Build = build?.Build, Environments = environments };
        }
        catch (SherpaBundleException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // SQLCipher surfaces a wrong key (or a non-database file) the first time
            // a page is read, as a generic "file is not a database" SqliteException.
            throw new SherpaBundleException(
                $"Could not open encrypted bundle '{Path.GetFileName(path)}'. " +
                "The password may be incorrect, or the file is not a Sherpa bundle.", ex);
        }
    }

    private static void DeleteDatabaseFiles(string path)
    {
        foreach (var file in new[] { path, path + "-wal", path + "-shm" })
            if (File.Exists(file))
                File.Delete(file);
    }

    /// <summary>Per-environment storage document (<see cref="Id"/> = environment name).</summary>
    internal sealed class EnvironmentDocument
    {
        public string Id { get; set; } = "";
        public EnvironmentBlock? Block { get; set; }
    }

    /// <summary>Singleton document holding the top-level <c>Build</c> defaults.</summary>
    internal sealed class BuildDocument
    {
        public string Id { get; set; } = BuildDocumentId;
        public CommonConfig? Build { get; set; }
    }
}
