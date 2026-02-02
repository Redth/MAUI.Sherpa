using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Stores Apple identities with P8 keys in secure storage (Keychain on macOS, DPAPI on Windows).
/// Only non-sensitive metadata is stored in the JSON file.
/// </summary>
public class AppleIdentityService : IAppleIdentityService
{
    private readonly ISecureStorageService _secureStorage;
    private readonly IFileSystemService _fileSystem;
    private readonly ILoggingService _logger;
    private readonly string _settingsPath;
    private List<AppleIdentityMetadata> _identities = new();

    // Internal record for storing non-sensitive data in JSON
    private record AppleIdentityMetadata(string Id, string Name, string KeyId, string IssuerId, string? P8KeyPath);

    private const string SecureKeyPrefix = "apple_identity_p8_";

    public AppleIdentityService(ISecureStorageService secureStorage, IFileSystemService fileSystem, ILoggingService logger)
    {
        _secureStorage = secureStorage;
        _fileSystem = fileSystem;
        _logger = logger;
        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MauiSherpa",
            "apple-identities.json");
    }

    public async Task<IReadOnlyList<AppleIdentity>> GetIdentitiesAsync()
    {
        await LoadIdentitiesAsync();
        var result = new List<AppleIdentity>();
        
        foreach (var meta in _identities)
        {
            // Get P8 content from secure storage
            var p8Content = await _secureStorage.GetAsync(SecureKeyPrefix + meta.Id);
            result.Add(new AppleIdentity(
                meta.Id, meta.Name, meta.KeyId, meta.IssuerId, meta.P8KeyPath, p8Content));
        }
        
        return result.AsReadOnly();
    }

    public async Task<AppleIdentity?> GetIdentityAsync(string id)
    {
        await LoadIdentitiesAsync();
        var meta = _identities.FirstOrDefault(i => i.Id == id);
        if (meta == null) return null;

        var p8Content = await _secureStorage.GetAsync(SecureKeyPrefix + id);
        return new AppleIdentity(meta.Id, meta.Name, meta.KeyId, meta.IssuerId, meta.P8KeyPath, p8Content);
    }

    public async Task SaveIdentityAsync(AppleIdentity identity)
    {
        await LoadIdentitiesAsync();
        
        // Store P8 content in secure storage
        if (!string.IsNullOrEmpty(identity.P8KeyContent))
        {
            await _secureStorage.SetAsync(SecureKeyPrefix + identity.Id, identity.P8KeyContent);
        }

        // Store only metadata in JSON (no P8 content)
        var meta = new AppleIdentityMetadata(
            identity.Id, identity.Name, identity.KeyId, identity.IssuerId, identity.P8KeyPath);
        
        var existing = _identities.FindIndex(i => i.Id == identity.Id);
        if (existing >= 0)
            _identities[existing] = meta;
        else
            _identities.Add(meta);

        await PersistIdentitiesAsync();
        _logger.LogInformation($"Saved Apple identity: {identity.Name} (P8 key stored securely)");
    }

    public async Task DeleteIdentityAsync(string id)
    {
        await LoadIdentitiesAsync();
        
        // Remove P8 from secure storage
        await _secureStorage.RemoveAsync(SecureKeyPrefix + id);
        
        var removed = _identities.RemoveAll(i => i.Id == id);
        if (removed > 0)
        {
            await PersistIdentitiesAsync();
            _logger.LogInformation($"Deleted Apple identity: {id}");
        }
    }

    public async Task<bool> TestConnectionAsync(AppleIdentity identity)
    {
        try
        {
            var p8Key = identity.P8KeyContent;
            if (string.IsNullOrEmpty(p8Key) && !string.IsNullOrEmpty(identity.P8KeyPath))
            {
                p8Key = await _fileSystem.ReadFileAsync(identity.P8KeyPath);
            }

            if (string.IsNullOrEmpty(p8Key))
            {
                _logger.LogError("No P8 key content available");
                return false;
            }

            // TODO: Implement actual API test when AppStoreConnectClient supports .NET 10
            if (p8Key.Contains("BEGIN PRIVATE KEY") && p8Key.Contains("END PRIVATE KEY"))
            {
                _logger.LogInformation($"Connection test passed (validation only) for: {identity.Name}");
                return true;
            }

            _logger.LogError("Invalid P8 key format");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Connection test failed: {ex.Message}", ex);
            return false;
        }
    }

    private async Task LoadIdentitiesAsync()
    {
        try
        {
            if (await _fileSystem.FileExistsAsync(_settingsPath))
            {
                var json = await _fileSystem.ReadFileAsync(_settingsPath);
                if (!string.IsNullOrEmpty(json))
                {
                    _identities = JsonSerializer.Deserialize<List<AppleIdentityMetadata>>(json) ?? new();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to load identities: {ex.Message}");
        }
        _identities = new();
    }

    private async Task PersistIdentitiesAsync()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory))
                await _fileSystem.CreateDirectoryAsync(directory);

            var json = JsonSerializer.Serialize(_identities, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            await _fileSystem.WriteFileAsync(_settingsPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to persist identities: {ex.Message}", ex);
        }
    }
}
