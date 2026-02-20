using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Cloud secrets provider for Vaultwarden / Bitwarden using the bw CLI.
/// Stores secrets as hidden custom fields on a single Secure Note item.
/// </summary>
public class VaultwardenProvider : ICloudSecretsProvider
{
    private readonly CloudSecretsProviderConfig _config;
    private readonly ILoggingService _logger;
    private string? _sessionToken;

    public VaultwardenProvider(CloudSecretsProviderConfig config, ILoggingService logger)
    {
        _config = config;
        _logger = logger;
    }

    public CloudSecretsProviderType ProviderType => CloudSecretsProviderType.Vaultwarden;
    public string DisplayName => "Vaultwarden / Bitwarden";

    #region Configuration Helpers

    private string ServerUrl => _config.Settings.GetValueOrDefault("ServerUrl", "");
    private string Email => _config.Settings.GetValueOrDefault("Email", "");
    private string ClientId => _config.Settings.GetValueOrDefault("ClientId", "");
    private string ClientSecret => _config.Settings.GetValueOrDefault("ClientSecret", "");
    private string MasterPassword => _config.Settings.GetValueOrDefault("MasterPassword", "");
    private string ItemName => _config.Settings.GetValueOrDefault("ItemName", "MAUI.Sherpa");

    #endregion

    #region ICloudSecretsProvider Implementation

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await IsCliInstalledAsync(cancellationToken))
            {
                _logger.LogError("Bitwarden CLI (bw) is not installed or not found in PATH");
                return false;
            }

            var session = await EnsureSessionAsync(cancellationToken);
            if (session == null)
            {
                _logger.LogError("Failed to authenticate with Vaultwarden");
                return false;
            }

            _logger.LogInformation($"Vaultwarden connection test successful for {ServerUrl}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Vaultwarden connection test error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> StoreSecretAsync(string key, byte[] value, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var session = await EnsureSessionAsync(cancellationToken);
            if (session == null) return false;

            var base64Value = Convert.ToBase64String(value);

            // Get or create the Secure Note
            var item = await GetOrCreateItemAsync(session, cancellationToken);
            if (item == null)
            {
                _logger.LogError("Failed to get or create Vaultwarden item");
                return false;
            }

            var itemId = item.Value.GetStringProperty("id");
            if (itemId == null) return false;

            // Update the fields array
            var fields = GetFieldsList(item.Value);
            SetField(fields, key, base64Value);

            // PUT the updated item back
            var updatedItem = RebuildItemWithFields(item.Value, fields);
            var (exitCode, output) = await RunBwAsync(
                new[] { "edit", "item", itemId, "--session", session },
                updatedItem, cancellationToken);

            if (exitCode != 0)
            {
                _logger.LogError($"Vaultwarden store secret failed (exit code {exitCode}): {output}");
                return false;
            }

            _logger.LogInformation($"Stored secret: {key}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Vaultwarden store secret error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<byte[]?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var session = await EnsureSessionAsync(cancellationToken);
            if (session == null) return null;

            var item = await FindItemAsync(session, cancellationToken);
            if (item == null) return null;

            var fieldValue = FindFieldValue(item.Value, key);
            if (fieldValue == null) return null;

            return Convert.FromBase64String(fieldValue);
        }
        catch (FormatException ex)
        {
            _logger.LogError($"Vaultwarden secret not base64 encoded: {key} - {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Vaultwarden get secret error: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<bool> DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var session = await EnsureSessionAsync(cancellationToken);
            if (session == null) return false;

            var item = await FindItemAsync(session, cancellationToken);
            if (item == null)
            {
                _logger.LogInformation($"Secret already deleted or not found: {key}");
                return true;
            }

            var itemId = item.Value.GetStringProperty("id");
            if (itemId == null) return false;

            var fields = GetFieldsList(item.Value);
            if (!RemoveField(fields, key))
            {
                _logger.LogInformation($"Secret field not found: {key}");
                return true;
            }

            var updatedItem = RebuildItemWithFields(item.Value, fields);
            var (exitCode, output) = await RunBwAsync(
                new[] { "edit", "item", itemId, "--session", session },
                updatedItem, cancellationToken);

            if (exitCode != 0)
            {
                _logger.LogError($"Vaultwarden delete secret failed (exit code {exitCode}): {output}");
                return false;
            }

            _logger.LogInformation($"Deleted secret: {key}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Vaultwarden delete secret error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> SecretExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var session = await EnsureSessionAsync(cancellationToken);
            if (session == null) return false;

            var item = await FindItemAsync(session, cancellationToken);
            if (item == null) return false;

            return FindFieldValue(item.Value, key) != null;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Vaultwarden secret exists check error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> ListSecretsAsync(string? prefix = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var session = await EnsureSessionAsync(cancellationToken);
            if (session == null) return Array.Empty<string>();

            var item = await FindItemAsync(session, cancellationToken);
            if (item == null) return Array.Empty<string>();

            var labels = GetCustomFieldNames(item.Value);

            if (!string.IsNullOrEmpty(prefix))
                labels = labels.Where(l => l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

            return labels.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Vaultwarden list secrets error: {ex.Message}", ex);
            return Array.Empty<string>();
        }
    }

    #endregion

    #region Session Management

    internal async Task<bool> IsCliInstalledAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var (exitCode, _) = await RunBwAsync(new[] { "--version" }, cancellationToken: cancellationToken);
            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> EnsureSessionAsync(CancellationToken cancellationToken)
    {
        if (_sessionToken != null)
            return _sessionToken;

        // Configure server URL
        var (cfgExit, cfgOut) = await RunBwAsync(
            new[] { "config", "server", ServerUrl }, cancellationToken: cancellationToken);
        if (cfgExit != 0)
        {
            _logger.LogError($"Failed to configure bw server: {cfgOut}");
            return null;
        }

        // Login with API key
        var (loginExit, loginOut) = await RunBwAsync(
            new[] { "login", "--apikey" },
            cancellationToken: cancellationToken,
            envVars: new Dictionary<string, string>
            {
                ["BW_CLIENTID"] = ClientId,
                ["BW_CLIENTSECRET"] = ClientSecret
            });

        // Exit code 1 with "already logged in" is OK
        if (loginExit != 0 && !loginOut.Contains("already logged in", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError($"Vaultwarden login failed (exit code {loginExit}): {loginOut}");
            return null;
        }

        // Unlock vault to get session token
        var (unlockExit, unlockOut) = await RunBwAsync(
            new[] { "unlock", "--raw" },
            cancellationToken: cancellationToken,
            envVars: new Dictionary<string, string>
            {
                ["BW_PASSWORD"] = MasterPassword
            });

        if (unlockExit != 0 || string.IsNullOrWhiteSpace(unlockOut))
        {
            _logger.LogError($"Vaultwarden unlock failed (exit code {unlockExit}): {unlockOut}");
            return null;
        }

        _sessionToken = unlockOut.Trim();
        return _sessionToken;
    }

    /// <summary>
    /// Invalidate the cached session, forcing re-auth on next operation.
    /// </summary>
    internal void InvalidateSession()
    {
        _sessionToken = null;
    }

    #endregion

    #region CLI Helpers

    internal async Task<(int ExitCode, string Output)> RunBwAsync(
        string[] args,
        string? stdin = null,
        CancellationToken cancellationToken = default,
        Dictionary<string, string>? envVars = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "bw",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin != null,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        // Disable interactive prompts
        psi.Environment["BW_NOINTERACTION"] = "true";

        if (envVars != null)
        {
            foreach (var kvp in envVars)
                psi.Environment[kvp.Key] = kvp.Value;
        }

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (stdin != null)
        {
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
        }

        await process.WaitForExitAsync(cancellationToken);

        var output = stdout.ToString().Trim();
        if (process.ExitCode != 0 && string.IsNullOrEmpty(output))
            output = stderr.ToString().Trim();

        return (process.ExitCode, output);
    }

    private async Task<JsonElement?> FindItemAsync(string session, CancellationToken cancellationToken)
    {
        var (exitCode, output) = await RunBwAsync(
            new[] { "list", "items", "--search", ItemName, "--session", session },
            cancellationToken: cancellationToken);

        if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
            return null;

        var items = JsonSerializer.Deserialize<JsonElement>(output);
        if (items.ValueKind != JsonValueKind.Array)
            return null;

        // Find the Secure Note (type 2) with matching name
        foreach (var item in items.EnumerateArray())
        {
            var type = item.GetInt32Property("type");
            var name = item.GetStringProperty("name");

            if (type == 2 && string.Equals(name, ItemName, StringComparison.OrdinalIgnoreCase))
                return item;
        }

        return null;
    }

    private async Task<JsonElement?> GetOrCreateItemAsync(string session, CancellationToken cancellationToken)
    {
        var existing = await FindItemAsync(session, cancellationToken);
        if (existing != null)
            return existing;

        _logger.LogInformation($"Creating Vaultwarden Secure Note '{ItemName}'");

        // Create a new Secure Note (type 2)
        var newItem = JsonSerializer.Serialize(new
        {
            type = 2,
            name = ItemName,
            notes = "",
            secureNote = new { type = 0 },
            fields = Array.Empty<object>()
        });

        // bw create item expects base64-encoded JSON on stdin
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(newItem));
        var (exitCode, output) = await RunBwAsync(
            new[] { "create", "item", encoded, "--session", session },
            cancellationToken: cancellationToken);

        if (exitCode != 0)
        {
            _logger.LogError($"Failed to create Vaultwarden item: {output}");
            return null;
        }

        if (string.IsNullOrWhiteSpace(output))
            return null;

        return JsonSerializer.Deserialize<JsonElement>(output);
    }

    #endregion

    #region JSON Field Helpers

    internal static string? FindFieldValue(JsonElement item, string key)
    {
        if (!item.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var field in fields.EnumerateArray())
        {
            var name = field.GetStringProperty("name");
            if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                return field.GetStringProperty("value");
        }

        return null;
    }

    internal static List<string> GetCustomFieldNames(JsonElement item)
    {
        var names = new List<string>();

        if (!item.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array)
            return names;

        foreach (var field in fields.EnumerateArray())
        {
            var name = field.GetStringProperty("name");
            if (!string.IsNullOrEmpty(name))
                names.Add(name);
        }

        return names;
    }

    /// <summary>
    /// Parse the fields array into a mutable list of dictionaries.
    /// </summary>
    internal static List<Dictionary<string, object?>> GetFieldsList(JsonElement item)
    {
        var result = new List<Dictionary<string, object?>>();

        if (!item.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var field in fields.EnumerateArray())
        {
            var dict = new Dictionary<string, object?>
            {
                ["name"] = field.GetStringProperty("name"),
                ["value"] = field.GetStringProperty("value"),
                ["type"] = field.GetInt32Property("type") ?? 1
            };
            result.Add(dict);
        }

        return result;
    }

    /// <summary>
    /// Add or update a field in the mutable fields list.
    /// </summary>
    internal static void SetField(List<Dictionary<string, object?>> fields, string key, string value)
    {
        var existing = fields.FirstOrDefault(f =>
            string.Equals(f.GetValueOrDefault("name")?.ToString(), key, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing["value"] = value;
        }
        else
        {
            fields.Add(new Dictionary<string, object?>
            {
                ["name"] = key,
                ["value"] = value,
                ["type"] = 1 // Hidden field
            });
        }
    }

    /// <summary>
    /// Remove a field from the mutable fields list. Returns true if found and removed.
    /// </summary>
    internal static bool RemoveField(List<Dictionary<string, object?>> fields, string key)
    {
        var existing = fields.FirstOrDefault(f =>
            string.Equals(f.GetValueOrDefault("name")?.ToString(), key, StringComparison.OrdinalIgnoreCase));

        if (existing == null)
            return false;

        fields.Remove(existing);
        return true;
    }

    /// <summary>
    /// Rebuild the item JSON with an updated fields array, preserving all other properties.
    /// Returns base64-encoded JSON string for bw edit item.
    /// </summary>
    internal static string RebuildItemWithFields(JsonElement originalItem, List<Dictionary<string, object?>> fields)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(originalItem.GetRawText())
            ?? new Dictionary<string, JsonElement>();

        // Replace the fields array
        dict["fields"] = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(fields));

        var json = JsonSerializer.Serialize(dict);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    #endregion
}

/// <summary>
/// Extension methods for safe JsonElement property access (Vaultwarden provider).
/// </summary>
internal static class VaultwardenJsonExtensions
{
    public static string? GetStringProperty(this JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            return value.GetString();
        return null;
    }

    public static int? GetInt32Property(this JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number)
            return value.GetInt32();
        return null;
    }
}
