using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Services;
using Microsoft.Data.Sqlite;
using Shiny.DocumentDb;
using Shiny.DocumentDb.Sqlite.SqlCipher;

if (args.Length != 1)
    throw new ArgumentException("Supply a new output directory for the synthetic 5.2.2 fixtures.");

var output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(output);
const string fixtureKey = "sherpa-5.2.2-synthetic-fixture-key";
var createdAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
var updatedAt = createdAt.AddDays(1);

var vaultItems = new List<LocalVaultItem>();
foreach (var scope in new[]
{
    "local-provider-secret", "local-provider-metadata", "settings", "secure",
    "cloud-provider", "migration", "background-task", "secret-sync"
})
{
    vaultItems.Add(new LocalVaultItem
    {
        Id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"v1\n{scope}\n/upgrade\nshared"))).ToLowerInvariant(),
        Scope = scope,
        Path = "/upgrade",
        Key = "shared",
        ContentType = "application/octet-stream",
        Value = Enumerable.Range(0, 256).Select(x => (byte)x).ToArray(),
        Metadata = new() { ["environment/name"] = "prod", ["owner=email"] = "test@example.invalid", ["unicode"] = "caf\u00e9 \u2603" },
        CreatedAt = createdAt,
        UpdatedAt = updatedAt
    });
}
vaultItems.Add(new LocalVaultItem
{
    Id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("v1\nsettings\n/\nempty"))).ToLowerInvariant(),
    Scope = "settings",
    Path = "/",
    Key = "empty",
    ContentType = "text/plain",
    Value = [],
    CreatedAt = createdAt,
    UpdatedAt = updatedAt
});
await WriteDatabaseAsync("local-vault.db", vaultItems);

var legacyItems = new[]
{
    new LocalSqlCipherSecretsProvider.LocalSecretDocument
    {
        Id = "legacy-binary-id",
        Key = "sherpa-secrets/api-key",
        Value = Enumerable.Range(0, 256).Select(x => (byte)x).ToArray(),
        Metadata = new() { ["source"] = "5.2.2", ["unicode"] = "caf\u00e9 \u2603" },
        CreatedAt = createdAt,
        UpdatedAt = updatedAt
    },
    new LocalSqlCipherSecretsProvider.LocalSecretDocument
    {
        Id = "legacy-empty-id",
        Key = "empty",
        Value = [],
        CreatedAt = createdAt,
        UpdatedAt = updatedAt
    }
};
await WriteDatabaseAsync("local-secrets.db", legacyItems);
SqliteConnection.ClearAllPools();

async Task WriteDatabaseAsync<T>(string name, IEnumerable<T> items) where T : class
{
    var path = Path.Combine(output, name);
    if (File.Exists(path))
        throw new IOException($"Refusing to overwrite fixture {path}");

    var provider = new SqlCipherDatabaseProvider(path, fixtureKey);
    using var store = new DocumentStore(new DocumentStoreOptions { DatabaseProvider = provider });
    foreach (var item in items)
        await store.Insert(item);

    await File.WriteAllTextAsync(path + ".json", JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Created {path}");
}
