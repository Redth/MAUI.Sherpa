using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Cloud secrets provider for Vaultwarden / Bitwarden using the REST API directly.
/// No CLI dependency — authenticates via OAuth2, derives encryption keys client-side,
/// and stores secrets as encrypted custom fields on a Secure Note cipher item.
/// Supports Bitwarden cloud, Vaultwarden.net, and custom self-hosted servers.
/// </summary>
public class VaultwardenProvider : ICloudSecretsProvider
{
    private readonly CloudSecretsProviderConfig _config;
    private readonly ILoggingService _logger;
    private readonly HttpClient _httpClient;

    // Cached session state
    private string? _accessToken;
    private string? _refreshToken;
    private DateTime _tokenExpiry;
    private byte[]? _encKey;
    private byte[]? _macKey;

    /// <summary>
    /// Well-known server presets for the ServerMode setting.
    /// </summary>
    public const string ServerModeBitwarden = "bitwarden";
    public const string ServerModeVaultwarden = "vaultwarden";
    public const string ServerModeCustom = "custom";

    public const string BitwardenCloudUrl = "https://vault.bitwarden.com";
    public const string VaultwardenNetUrl = "https://vault.vaultwarden.net";

    public VaultwardenProvider(CloudSecretsProviderConfig config, ILoggingService logger)
        : this(config, logger, null) { }

    internal VaultwardenProvider(CloudSecretsProviderConfig config, ILoggingService logger, HttpClient? httpClient)
    {
        _config = config;
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
    }

    public CloudSecretsProviderType ProviderType => CloudSecretsProviderType.Vaultwarden;
    public string DisplayName => "Vaultwarden / Bitwarden";

    #region Configuration Helpers

    private string ServerMode => _config.Settings.GetValueOrDefault("ServerMode", ServerModeCustom);
    private string CustomServerUrl => _config.Settings.GetValueOrDefault("ServerUrl", "").TrimEnd('/');
    private string Email => _config.Settings.GetValueOrDefault("Email", "");
    private string ClientId => _config.Settings.GetValueOrDefault("ClientId", "");
    private string ClientSecret => _config.Settings.GetValueOrDefault("ClientSecret", "");
    private string MasterPassword => _config.Settings.GetValueOrDefault("MasterPassword", "");
    private string ItemName => _config.Settings.GetValueOrDefault("ItemName", "MAUI.Sherpa");

    /// <summary>
    /// Resolve the effective server URL based on ServerMode.
    /// </summary>
    internal string ServerUrl => ServerMode switch
    {
        ServerModeBitwarden => BitwardenCloudUrl,
        ServerModeVaultwarden => VaultwardenNetUrl,
        _ => CustomServerUrl
    };

    private string ApiBaseUrl => ServerMode == ServerModeBitwarden
        ? "https://api.bitwarden.com"
        : $"{ServerUrl}/api";

    private string IdentityUrl => ServerMode == ServerModeBitwarden
        ? "https://identity.bitwarden.com"
        : $"{ServerUrl}/identity";

    #endregion

    #region ICloudSecretsProvider Implementation

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureAuthenticatedAsync(cancellationToken);
            _logger.LogInformation($"Vaultwarden connection test successful for {ServerUrl}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Vaultwarden connection test error for {ApiBaseUrl}: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> StoreSecretAsync(string key, byte[] value, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureAuthenticatedAsync(cancellationToken);

            var base64Value = Convert.ToBase64String(value);
            var cipher = await GetOrCreateCipherAsync(cancellationToken);
            if (cipher == null)
            {
                _logger.LogError("Failed to get or create Vaultwarden cipher");
                return false;
            }

            var cipherId = cipher.Value.GetStringProperty("Id") ?? cipher.Value.GetStringProperty("id");
            if (cipherId == null) return false;

            var fields = GetFieldsList(cipher.Value);
            SetField(fields, key, base64Value);

            await UpdateCipherFieldsAsync(cipherId, cipher.Value, fields, cancellationToken);
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
            await EnsureAuthenticatedAsync(cancellationToken);

            var cipher = await FindCipherAsync(cancellationToken);
            if (cipher == null) return null;

            var fieldValue = FindFieldValue(cipher.Value, key);
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
            await EnsureAuthenticatedAsync(cancellationToken);

            var cipher = await FindCipherAsync(cancellationToken);
            if (cipher == null)
            {
                _logger.LogInformation($"Secret already deleted or not found: {key}");
                return true;
            }

            var cipherId = cipher.Value.GetStringProperty("Id") ?? cipher.Value.GetStringProperty("id");
            if (cipherId == null) return false;

            var fields = GetFieldsList(cipher.Value);
            if (!RemoveField(fields, key))
            {
                _logger.LogInformation($"Secret field not found: {key}");
                return true;
            }

            await UpdateCipherFieldsAsync(cipherId, cipher.Value, fields, cancellationToken);
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
            await EnsureAuthenticatedAsync(cancellationToken);

            var cipher = await FindCipherAsync(cancellationToken);
            if (cipher == null) return false;

            return FindFieldValue(cipher.Value, key) != null;
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
            await EnsureAuthenticatedAsync(cancellationToken);

            var cipher = await FindCipherAsync(cancellationToken);
            if (cipher == null) return Array.Empty<string>();

            var labels = GetCustomFieldNames(cipher.Value);

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

    #region Authentication & Key Derivation

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        if (_accessToken != null && _encKey != null && DateTime.UtcNow < _tokenExpiry)
            return;

        // If we have a refresh token and keys are still valid, try refreshing
        if (_refreshToken != null && _encKey != null)
        {
            try
            {
                await RefreshTokenAsync(cancellationToken);
                return;
            }
            catch
            {
                // Fall through to full login
            }
        }

        // Step 1: Prelogin to get KDF parameters
        var (kdfIterations, kdfType) = await PreloginAsync(cancellationToken);

        // Step 2: Derive master key from password + email
        var masterKey = DeriveKey(MasterPassword, Email.ToLowerInvariant(), kdfIterations, kdfType);

        // Step 3: Hash password for authentication
        var hashedPassword = HashPassword(masterKey, MasterPassword);

        // Step 4: Login to get access token and protected symmetric key
        var (accessToken, refreshToken, expiresIn, protectedKey) =
            await LoginAsync(hashedPassword, cancellationToken);

        _accessToken = accessToken;
        _refreshToken = refreshToken;
        _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60); // 60s buffer

        // Step 5: Decrypt the protected symmetric key to get encKey and macKey
        DecryptProtectedKey(protectedKey, masterKey);

        _logger.LogInformation($"Authenticated with Vaultwarden at {ServerUrl}");
    }

    internal async Task<(int Iterations, int KdfType)> PreloginAsync(CancellationToken cancellationToken)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { email = Email }),
            Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"{ApiBaseUrl}/accounts/prelogin", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);

        var iterations = doc.GetInt32Property("KdfIterations") ?? doc.GetInt32Property("kdfIterations") ?? 100000;
        var kdfType = doc.GetInt32Property("Kdf") ?? doc.GetInt32Property("kdf") ?? 0;

        return (iterations, kdfType);
    }

    /// <summary>
    /// Derive the master key from password and email using PBKDF2-SHA256.
    /// </summary>
    internal static byte[] DeriveKey(string password, string salt, int iterations, int kdfType = 0)
    {
        // kdfType 0 = PBKDF2-SHA256 (most common for Vaultwarden/Bitwarden)
        var saltBytes = Encoding.UTF8.GetBytes(salt);
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        using var pbkdf2 = new Rfc2898DeriveBytes(passwordBytes, saltBytes, iterations, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(32);
    }

    /// <summary>
    /// Hash the master key with the password for server-side authentication.
    /// Uses 1 round of PBKDF2(masterKey, password).
    /// </summary>
    internal static string HashPassword(byte[] masterKey, string password)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        using var pbkdf2 = new Rfc2898DeriveBytes(masterKey, passwordBytes, 1, HashAlgorithmName.SHA256);
        return Convert.ToBase64String(pbkdf2.GetBytes(32));
    }

    private async Task<(string AccessToken, string RefreshToken, int ExpiresIn, string ProtectedKey)> LoginAsync(
        string hashedPassword, CancellationToken cancellationToken)
    {
        var deviceId = GenerateDeviceId();
        var formData = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = Email,
            ["password"] = hashedPassword,
            ["scope"] = "api offline_access",
            ["client_id"] = "connector",
            ["deviceType"] = "3",
            ["deviceIdentifier"] = deviceId,
            ["deviceName"] = "MAUI Sherpa",
            ["devicePushToken"] = ""
        };

        var content = new FormUrlEncodedContent(formData);
        var response = await _httpClient.PostAsync($"{IdentityUrl}/connect/token", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Vaultwarden login failed ({response.StatusCode}): {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);

        var accessToken = doc.GetStringProperty("access_token")
            ?? throw new InvalidOperationException("No access_token in login response");
        var refreshToken = doc.GetStringProperty("refresh_token") ?? "";
        var expiresIn = doc.GetInt32Property("expires_in") ?? 3600;
        var protectedKey = doc.GetStringProperty("Key")
            ?? throw new InvalidOperationException("No Key in login response");

        return (accessToken, refreshToken, expiresIn, protectedKey);
    }

    private async Task RefreshTokenAsync(CancellationToken cancellationToken)
    {
        var formData = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = "connector",
            ["refresh_token"] = _refreshToken!
        };

        var content = new FormUrlEncodedContent(formData);
        var response = await _httpClient.PostAsync($"{IdentityUrl}/connect/token", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);

        _accessToken = doc.GetStringProperty("access_token");
        _refreshToken = doc.GetStringProperty("refresh_token") ?? _refreshToken;
        var expiresIn = doc.GetInt32Property("expires_in") ?? 3600;
        _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60);
    }

    /// <summary>
    /// Decrypt the protected symmetric key from the server using the master key.
    /// The protected key is a CipherString in format "0.IV|CT" (AesCbc256_B64)
    /// or "2.IV|CT|MAC" (AesCbc256_HmacSha256_B64).
    /// Sets _encKey (32 bytes) and _macKey (32 bytes).
    /// </summary>
    internal void DecryptProtectedKey(string protectedKey, byte[] masterKey)
    {
        var dotIndex = protectedKey.IndexOf('.');
        var encType = int.Parse(protectedKey[..dotIndex]);
        var parts = protectedKey[(dotIndex + 1)..].Split('|');

        byte[] iv = Convert.FromBase64String(parts[0]);
        byte[] ct = Convert.FromBase64String(parts[1]);

        byte[] decryptedKey;

        if (encType == 0)
        {
            // AesCbc256_B64: no MAC, decrypt with masterKey directly
            decryptedKey = AesDecrypt(ct, masterKey, iv);
        }
        else if (encType == 2)
        {
            // AesCbc256_HmacSha256_B64: stretch masterKey via HKDF first
            var stretchedKey = HkdfStretch(masterKey);
            var stretchEncKey = stretchedKey[..32];
            var stretchMacKey = stretchedKey[32..];

            byte[] mac = Convert.FromBase64String(parts[2]);
            VerifyMac(stretchMacKey, iv, ct, mac);
            decryptedKey = AesDecrypt(ct, stretchEncKey, iv);
        }
        else
        {
            throw new NotSupportedException($"Unsupported encryption type: {encType}");
        }

        if (decryptedKey.Length != 64)
            throw new InvalidOperationException($"Expected 64-byte symmetric key, got {decryptedKey.Length}");

        _encKey = decryptedKey[..32];
        _macKey = decryptedKey[32..];
    }

    /// <summary>
    /// Invalidate the cached session, forcing re-auth on next operation.
    /// </summary>
    internal void InvalidateSession()
    {
        _accessToken = null;
        _refreshToken = null;
        _encKey = null;
        _macKey = null;
    }

    private static string GenerateDeviceId()
    {
        var machineBytes = Encoding.UTF8.GetBytes(Environment.MachineName + "-maui-sherpa");
        var hash = SHA256.HashData(machineBytes);
        return new Guid(hash[..16]).ToString();
    }

    #endregion

    #region Bitwarden Crypto

    /// <summary>
    /// AES-256-CBC decrypt with PKCS7 padding.
    /// </summary>
    internal static byte[] AesDecrypt(byte[] cipherText, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);
    }

    /// <summary>
    /// AES-256-CBC encrypt with PKCS7 padding.
    /// </summary>
    internal static byte[] AesEncrypt(byte[] plainText, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(plainText, 0, plainText.Length);
    }

    /// <summary>
    /// Encrypt a plaintext string into a Bitwarden CipherString (type 2: AesCbc256_HmacSha256_B64).
    /// Format: "2.{base64-IV}|{base64-CT}|{base64-MAC}"
    /// </summary>
    internal static string EncryptString(string plainText, byte[] encKey, byte[] macKey)
    {
        var iv = RandomNumberGenerator.GetBytes(16);
        var pt = Encoding.UTF8.GetBytes(plainText);
        var ct = AesEncrypt(pt, encKey, iv);
        var mac = ComputeMac(macKey, iv, ct);

        return $"2.{Convert.ToBase64String(iv)}|{Convert.ToBase64String(ct)}|{Convert.ToBase64String(mac)}";
    }

    /// <summary>
    /// Decrypt a Bitwarden CipherString back to plaintext.
    /// Supports type 0 (AesCbc256_B64) and type 2 (AesCbc256_HmacSha256_B64).
    /// </summary>
    internal static string DecryptString(string cipherString, byte[] encKey, byte[] macKey)
    {
        var dotIndex = cipherString.IndexOf('.');
        var encType = int.Parse(cipherString[..dotIndex]);
        var parts = cipherString[(dotIndex + 1)..].Split('|');

        var iv = Convert.FromBase64String(parts[0]);
        var ct = Convert.FromBase64String(parts[1]);

        if (encType == 2 && parts.Length >= 3)
        {
            var mac = Convert.FromBase64String(parts[2]);
            VerifyMac(macKey, iv, ct, mac);
        }

        var pt = AesDecrypt(ct, encKey, iv);
        return Encoding.UTF8.GetString(pt);
    }

    /// <summary>
    /// Compute HMAC-SHA256 over IV + ciphertext.
    /// </summary>
    internal static byte[] ComputeMac(byte[] macKey, byte[] iv, byte[] cipherText)
    {
        var data = new byte[iv.Length + cipherText.Length];
        iv.CopyTo(data, 0);
        cipherText.CopyTo(data, iv.Length);
        return HMACSHA256.HashData(macKey, data);
    }

    /// <summary>
    /// Verify HMAC-SHA256 MAC using constant-time comparison.
    /// </summary>
    internal static void VerifyMac(byte[] macKey, byte[] iv, byte[] cipherText, byte[] expectedMac)
    {
        var computedMac = ComputeMac(macKey, iv, cipherText);
        if (!CryptographicOperations.FixedTimeEquals(computedMac, expectedMac))
            throw new CryptographicException("MAC verification failed");
    }

    /// <summary>
    /// HKDF stretch for type 2 protected symmetric key decryption.
    /// Expands a 32-byte key into 64 bytes (32 enc + 32 mac) using HKDF-SHA256.
    /// </summary>
    internal static byte[] HkdfStretch(byte[] key)
    {
        var encKeyBytes = HKDF.Expand(HashAlgorithmName.SHA256, key, 32, Encoding.UTF8.GetBytes("enc"));
        var macKeyBytes = HKDF.Expand(HashAlgorithmName.SHA256, key, 32, Encoding.UTF8.GetBytes("mac"));
        var result = new byte[64];
        encKeyBytes.CopyTo(result, 0);
        macKeyBytes.CopyTo(result, 32);
        return result;
    }

    #endregion

    #region Cipher API

    private async Task<JsonElement?> FindCipherAsync(CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/sync?excludeDomains=true");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);

        if (!doc.TryGetProperty("Ciphers", out var ciphers) && !doc.TryGetProperty("ciphers", out ciphers))
            return null;

        foreach (var cipher in ciphers.EnumerateArray())
        {
            var type = cipher.GetInt32Property("Type") ?? cipher.GetInt32Property("type");
            if (type != 2) continue; // Only Secure Notes

            var encryptedName = cipher.GetStringProperty("Name") ?? cipher.GetStringProperty("name");
            if (encryptedName == null) continue;

            try
            {
                var decryptedName = DecryptString(encryptedName, _encKey!, _macKey!);
                if (string.Equals(decryptedName, ItemName, StringComparison.OrdinalIgnoreCase))
                    return cipher;
            }
            catch
            {
                // Skip items we can't decrypt
            }
        }

        return null;
    }

    private async Task<JsonElement?> GetOrCreateCipherAsync(CancellationToken cancellationToken)
    {
        var existing = await FindCipherAsync(cancellationToken);
        if (existing != null) return existing;

        _logger.LogInformation($"Creating Vaultwarden Secure Note '{ItemName}'");

        var encryptedName = EncryptString(ItemName, _encKey!, _macKey!);

        var newCipher = new
        {
            type = 2,
            name = encryptedName,
            notes = (string?)null,
            secureNote = new { type = 0 },
            fields = Array.Empty<object>()
        };

        var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/ciphers")
        {
            Content = new StringContent(JsonSerializer.Serialize(newCipher), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private async Task UpdateCipherFieldsAsync(string cipherId, JsonElement originalCipher,
        List<Dictionary<string, object?>> fields, CancellationToken cancellationToken)
    {
        // Encrypt each field name and value
        var encryptedFields = fields.Select(f => new
        {
            name = EncryptString(f["name"]?.ToString() ?? "", _encKey!, _macKey!),
            value = EncryptString(f["value"]?.ToString() ?? "", _encKey!, _macKey!),
            type = Convert.ToInt32(f["type"] ?? 1)
        }).ToArray();

        var type = originalCipher.GetInt32Property("Type") ?? originalCipher.GetInt32Property("type") ?? 2;
        var name = originalCipher.GetStringProperty("Name") ?? originalCipher.GetStringProperty("name");
        var notes = originalCipher.GetStringProperty("Notes") ?? originalCipher.GetStringProperty("notes");

        var updatePayload = new
        {
            type,
            name,
            notes,
            secureNote = new { type = 0 },
            fields = encryptedFields
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"{ApiBaseUrl}/ciphers/{cipherId}")
        {
            Content = new StringContent(JsonSerializer.Serialize(updatePayload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    #endregion

    #region JSON Field Helpers (decrypt-aware)

    /// <summary>
    /// Find a decrypted field value by key from a cipher's encrypted fields.
    /// </summary>
    internal string? FindFieldValue(JsonElement cipher, string key)
    {
        if (!cipher.TryGetProperty("Fields", out var fields) && !cipher.TryGetProperty("fields", out fields))
            return null;
        if (fields.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var field in fields.EnumerateArray())
        {
            var encName = field.GetStringProperty("Name") ?? field.GetStringProperty("name");
            if (encName == null) continue;

            try
            {
                var decryptedName = DecryptString(encName, _encKey!, _macKey!);
                if (string.Equals(decryptedName, key, StringComparison.OrdinalIgnoreCase))
                {
                    var encValue = field.GetStringProperty("Value") ?? field.GetStringProperty("value");
                    return encValue != null ? DecryptString(encValue, _encKey!, _macKey!) : null;
                }
            }
            catch
            {
                // Skip fields we can't decrypt
            }
        }

        return null;
    }

    /// <summary>
    /// Get all decrypted custom field names from a cipher.
    /// </summary>
    internal List<string> GetCustomFieldNames(JsonElement cipher)
    {
        var names = new List<string>();

        if (!cipher.TryGetProperty("Fields", out var fields) && !cipher.TryGetProperty("fields", out fields))
            return names;
        if (fields.ValueKind != JsonValueKind.Array)
            return names;

        foreach (var field in fields.EnumerateArray())
        {
            var encName = field.GetStringProperty("Name") ?? field.GetStringProperty("name");
            if (encName == null) continue;

            try
            {
                var decryptedName = DecryptString(encName, _encKey!, _macKey!);
                if (!string.IsNullOrEmpty(decryptedName))
                    names.Add(decryptedName);
            }
            catch
            {
                // Skip fields we can't decrypt
            }
        }

        return names;
    }

    /// <summary>
    /// Parse encrypted fields into a mutable list with decrypted names and values.
    /// </summary>
    internal List<Dictionary<string, object?>> GetFieldsList(JsonElement cipher)
    {
        var result = new List<Dictionary<string, object?>>();

        if (!cipher.TryGetProperty("Fields", out var fields) && !cipher.TryGetProperty("fields", out fields))
            return result;
        if (fields.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var field in fields.EnumerateArray())
        {
            var encName = field.GetStringProperty("Name") ?? field.GetStringProperty("name");
            var encValue = field.GetStringProperty("Value") ?? field.GetStringProperty("value");
            var fieldType = field.GetInt32Property("Type") ?? field.GetInt32Property("type") ?? 1;

            if (encName == null) continue;

            try
            {
                var dict = new Dictionary<string, object?>
                {
                    ["name"] = DecryptString(encName, _encKey!, _macKey!),
                    ["value"] = encValue != null ? DecryptString(encValue, _encKey!, _macKey!) : null,
                    ["type"] = fieldType
                };
                result.Add(dict);
            }
            catch
            {
                // Skip fields we can't decrypt
            }
        }

        return result;
    }

    /// <summary>
    /// Add or update a field in the mutable fields list (plaintext names/values).
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
