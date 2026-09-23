// Frozen from the pre-upgrade application (f13dd451). Do not link current application models:
// changing them must not silently change the historical database contract.
namespace MauiSherpa.Core.Services;

public sealed class LocalVaultItem
{
    public string Id { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Path { get; set; } = "/";
    public string Key { get; set; } = "";
    public string ContentType { get; set; } = "";
    public byte[] Value { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class LocalSqlCipherSecretsProvider
{
    internal sealed class LocalSecretDocument
    {
        public string Id { get; set; } = "";
        public string Key { get; set; } = "";
        public byte[] Value { get; set; } = [];
        public Dictionary<string, string> Metadata { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
