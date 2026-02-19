using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Cloud secrets provider implementation for 1Password using the op CLI.
/// Stores secrets as concealed fields on a single Secure Note item.
/// </summary>
public class OnePasswordProvider : ICloudSecretsProvider
{
    private readonly CloudSecretsProviderConfig _config;
    private readonly ILoggingService _logger;

    public OnePasswordProvider(CloudSecretsProviderConfig config, ILoggingService logger)
    {
        _config = config;
        _logger = logger;
    }

    public CloudSecretsProviderType ProviderType => CloudSecretsProviderType.OnePassword;
    public string DisplayName => "1Password";

    #region Configuration Helpers

    private string Vault => _config.Settings.GetValueOrDefault("Vault", "");
    private string ItemTitle => _config.Settings.GetValueOrDefault("ItemTitle", "MAUI.Sherpa");
    private string? ServiceAccountToken
    {
        get
        {
            var token = _config.Settings.GetValueOrDefault("ServiceAccountToken", "");
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }
    }

    #endregion

    #region ICloudSecretsProvider Implementation

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await IsCliInstalledAsync(cancellationToken))
            {
                _logger.LogError("1Password CLI (op) is not installed or not found in PATH");
                return false;
            }

            var (exitCode, output) = await RunOpAsync(new[] { "vault", "list", "--format", "json" }, cancellationToken);
            if (exitCode != 0)
            {
                _logger.LogError($"1Password connection test failed (exit code {exitCode}): {output}");
                return false;
            }

            var vaults = JsonSerializer.Deserialize<JsonElement>(output);
            if (vaults.ValueKind == JsonValueKind.Array)
            {
                foreach (var vault in vaults.EnumerateArray())
                {
                    var name = vault.GetPropertyOrDefault("name");
                    var id = vault.GetPropertyOrDefault("id");
                    if (string.Equals(name, Vault, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(id, Vault, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation($"1Password connection test successful, vault '{Vault}' found");
                        return true;
                    }
                }
            }

            _logger.LogError($"1Password vault '{Vault}' not found");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"1Password connection test error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> StoreSecretAsync(string key, byte[] value, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var base64Value = Convert.ToBase64String(value);

            // Ensure the Secure Note item exists
            await EnsureItemExistsAsync(cancellationToken);

            // Use assignment statement to add/update a concealed field
            var assignment = $"{SanitizeFieldLabel(key)}[concealed]={base64Value}";
            var (exitCode, output) = await RunOpAsync(
                new[] { "item", "edit", ItemTitle, "--vault", Vault, assignment }, cancellationToken);

            if (exitCode != 0)
            {
                _logger.LogError($"1Password store secret failed (exit code {exitCode}): {output}");
                return false;
            }

            _logger.LogInformation($"Stored secret: {key}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"1Password store secret error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<byte[]?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var item = await GetItemJsonAsync(cancellationToken);
            if (item == null)
                return null;

            var fieldValue = FindFieldValue(item.Value, key);
            if (fieldValue == null)
                return null;

            return Convert.FromBase64String(fieldValue);
        }
        catch (FormatException ex)
        {
            _logger.LogError($"1Password secret not base64 encoded: {key} - {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError($"1Password get secret error: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<bool> DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var assignment = $"{SanitizeFieldLabel(key)}[delete]";
            var (exitCode, output) = await RunOpAsync(
                new[] { "item", "edit", ItemTitle, "--vault", Vault, assignment }, cancellationToken);

            if (exitCode != 0)
            {
                // If field doesn't exist, that's fine
                if (output.Contains("isn't a field", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("not found", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation($"Secret already deleted or not found: {key}");
                    return true;
                }

                _logger.LogError($"1Password delete secret failed (exit code {exitCode}): {output}");
                return false;
            }

            _logger.LogInformation($"Deleted secret: {key}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"1Password delete secret error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> SecretExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var item = await GetItemJsonAsync(cancellationToken);
            if (item == null)
                return false;

            return FindFieldValue(item.Value, key) != null;
        }
        catch (Exception ex)
        {
            _logger.LogError($"1Password secret exists check error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> ListSecretsAsync(string? prefix = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var item = await GetItemJsonAsync(cancellationToken);
            if (item == null)
                return Array.Empty<string>();

            var labels = GetCustomFieldLabels(item.Value);

            if (!string.IsNullOrEmpty(prefix))
                labels = labels.Where(l => l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

            return labels.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError($"1Password list secrets error: {ex.Message}", ex);
            return Array.Empty<string>();
        }
    }

    #endregion

    #region CLI Helpers

    internal async Task<bool> IsCliInstalledAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var (exitCode, _) = await RunOpAsync(new[] { "--version" }, cancellationToken);
            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    internal async Task<(int ExitCode, string Output)> RunOpAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "op",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        // Set service account token if configured
        if (ServiceAccountToken != null)
            psi.Environment["OP_SERVICE_ACCOUNT_TOKEN"] = ServiceAccountToken;

        // Disable interactive prompts
        psi.Environment["OP_FORMAT"] = "json";

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        var output = stdout.ToString().Trim();
        if (process.ExitCode != 0 && string.IsNullOrEmpty(output))
            output = stderr.ToString().Trim();

        return (process.ExitCode, output);
    }

    private async Task EnsureItemExistsAsync(CancellationToken cancellationToken)
    {
        var item = await GetItemJsonAsync(cancellationToken);
        if (item != null)
            return;

        _logger.LogInformation($"Creating 1Password Secure Note '{ItemTitle}' in vault '{Vault}'");

        var (exitCode, output) = await RunOpAsync(
            new[] { "item", "create", "--category", "Secure Note", "--title", ItemTitle, "--vault", Vault },
            cancellationToken);

        if (exitCode != 0)
            throw new InvalidOperationException($"Failed to create 1Password item: {output}");
    }

    private async Task<JsonElement?> GetItemJsonAsync(CancellationToken cancellationToken)
    {
        var (exitCode, output) = await RunOpAsync(
            new[] { "item", "get", ItemTitle, "--vault", Vault, "--format", "json" }, cancellationToken);

        if (exitCode != 0)
            return null;

        if (string.IsNullOrWhiteSpace(output))
            return null;

        return JsonSerializer.Deserialize<JsonElement>(output);
    }

    #endregion

    #region JSON Parsing Helpers

    internal static string? FindFieldValue(JsonElement item, string key)
    {
        if (!item.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array)
            return null;

        var sanitizedKey = SanitizeFieldLabel(key);

        foreach (var field in fields.EnumerateArray())
        {
            var label = field.GetPropertyOrDefault("label");
            var id = field.GetPropertyOrDefault("id");

            // Skip built-in fields (notesPlain, etc.)
            var purpose = field.GetPropertyOrDefault("purpose");
            if (!string.IsNullOrEmpty(purpose))
                continue;

            if (string.Equals(label, sanitizedKey, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(label, key, StringComparison.OrdinalIgnoreCase))
            {
                return field.GetPropertyOrDefault("value");
            }
        }

        return null;
    }

    internal static List<string> GetCustomFieldLabels(JsonElement item)
    {
        var labels = new List<string>();

        if (!item.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array)
            return labels;

        foreach (var field in fields.EnumerateArray())
        {
            // Skip built-in fields
            var purpose = field.GetPropertyOrDefault("purpose");
            if (!string.IsNullOrEmpty(purpose))
                continue;

            var label = field.GetPropertyOrDefault("label");
            if (!string.IsNullOrEmpty(label))
                labels.Add(label);
        }

        return labels;
    }

    /// <summary>
    /// Sanitize a key for use as a 1Password field label.
    /// Field labels in 1Password support most characters, but we escape
    /// periods and backslashes which have special meaning in assignment statements.
    /// </summary>
    internal static string SanitizeFieldLabel(string key)
    {
        // In op CLI assignment statements, periods separate section.field
        // and backslashes/equals signs need escaping
        var sb = new StringBuilder(key.Length);
        foreach (var c in key)
        {
            if (c is '.' or '=' or '\\')
                sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    #endregion
}

/// <summary>
/// Extension method for safe JsonElement property access
/// </summary>
internal static class JsonElementExtensions
{
    public static string? GetPropertyOrDefault(this JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            return value.GetString();
        return null;
    }
}
