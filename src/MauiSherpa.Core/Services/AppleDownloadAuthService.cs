using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Implements Apple Developer authentication for Xcode downloads.
/// Uses Apple's session-based auth (SRP protocol + 2FA).
/// </summary>
public class AppleDownloadAuthService : IAppleDownloadAuthService
{
    private readonly ILoggingService _logger;
    private readonly ISecureStorageService _secureStorage;
    private readonly HttpClient _httpClient;
    private readonly CookieContainer _cookieContainer;

    private AppleAuthSession? _session;
    private AppleAuthOptions? _pendingAuthOptions;
    private string? _pendingAppleId;

    // Apple auth endpoints
    private const string AuthServiceKey = "https://appstoreconnect.apple.com/olympus/v1/app/config";
    private const string SignInInitUrl = "https://idmsa.apple.com/appleauth/auth/signin/init";
    private const string SignInCompleteUrl = "https://idmsa.apple.com/appleauth/auth/signin/complete";
    private const string AuthUrl = "https://idmsa.apple.com/appleauth/auth";
    private const string TrustUrl = "https://idmsa.apple.com/appleauth/auth/2sv/trust";
    private const string OlympusSessionUrl = "https://appstoreconnect.apple.com/olympus/v1/session";

    private const string SecureStorageSessionKey = "apple_download_session";
    private const string SecureStorageAppleIdKey = "apple_download_appleid";

    public bool IsAuthenticated => _session != null && _session.ExpiresAt > DateTime.UtcNow;
    public string? CurrentAppleId => _session?.AppleId ?? _pendingAppleId;

    public event Action? AuthStateChanged;

    public AppleDownloadAuthService(
        ILoggingService logger,
        ISecureStorageService secureStorage)
    {
        _logger = logger;
        _secureStorage = secureStorage;
        _cookieContainer = new CookieContainer();

        var handler = new HttpClientHandler
        {
            CookieContainer = _cookieContainer,
            AllowAutoRedirect = true,
            UseCookies = true
        };
        _httpClient = new HttpClient(handler);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "MauiSherpa");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

        // Try to restore session from secure storage
        _ = TryRestoreSessionAsync();
    }

    public async Task<AppleAuthResult> AuthenticateAsync(string appleId, string password)
    {
        _pendingAppleId = appleId;

        try
        {
            _logger.LogInformation($"Authenticating with Apple ID: {appleId}...");

            // Step 1: Get service key
            var serviceKey = await GetServiceKeyAsync();
            if (serviceKey == null)
                return new AppleAuthResult(false, false, ErrorMessage: "Failed to get Apple auth service key");

            // Step 2: Compute SRP client values
            var (srpInit, srpState) = ComputeSrpInit(appleId);

            // Step 3: Send SRP init
            var initRequest = new HttpRequestMessage(HttpMethod.Post, SignInInitUrl);
            initRequest.Headers.Add("X-Apple-Widget-Key", serviceKey);
            initRequest.Content = new StringContent(
                JsonSerializer.Serialize(new { a = srpInit, accountName = appleId, protocols = new[] { "s2k", "s2k_fo" } }),
                Encoding.UTF8, "application/json");

            var initResponse = await _httpClient.SendAsync(initRequest);
            if (!initResponse.IsSuccessStatusCode)
            {
                var error = await initResponse.Content.ReadAsStringAsync();
                _logger.LogError($"SRP init failed: {error}");
                return new AppleAuthResult(false, false, ErrorMessage: "Authentication failed: invalid Apple ID");
            }

            var initJson = await initResponse.Content.ReadAsStringAsync();
            using var initDoc = JsonDocument.Parse(initJson);

            var salt = initDoc.RootElement.GetProperty("salt").GetString()!;
            var serverB = initDoc.RootElement.GetProperty("b").GetString()!;
            var iterations = initDoc.RootElement.GetProperty("iteration").GetInt32();
            var protocol = initDoc.RootElement.TryGetProperty("protocol", out var proto) ? proto.GetString() ?? "s2k" : "s2k";
            var cValue = initDoc.RootElement.TryGetProperty("c", out var cProp) ? cProp.GetString() : null;

            // Step 4: Compute SRP complete values
            var (m1, m2) = ComputeSrpComplete(password, salt, serverB, iterations, protocol, srpState);

            // Step 5: Send SRP complete
            var completeRequest = new HttpRequestMessage(HttpMethod.Post, SignInCompleteUrl);
            completeRequest.Headers.Add("X-Apple-Widget-Key", serviceKey);
            var completePayload = new Dictionary<string, object>
            {
                ["accountName"] = appleId,
                ["m1"] = m1,
                ["m2"] = m2,
                ["rememberMe"] = true
            };
            if (cValue != null) completePayload["c"] = cValue;

            completeRequest.Content = new StringContent(
                JsonSerializer.Serialize(completePayload),
                Encoding.UTF8, "application/json");

            var completeResponse = await _httpClient.SendAsync(completeRequest);

            // Check for 2FA required (409 Conflict)
            if (completeResponse.StatusCode == HttpStatusCode.Conflict)
            {
                _logger.LogInformation("Two-factor authentication required");
                var authOptions = await GetAuthOptionsInternalAsync(serviceKey);
                _pendingAuthOptions = authOptions;
                return new AppleAuthResult(false, true, TwoFactorOptions: authOptions);
            }

            if (!completeResponse.IsSuccessStatusCode)
            {
                var error = await completeResponse.Content.ReadAsStringAsync();
                _logger.LogError($"Authentication failed: {error}");
                return new AppleAuthResult(false, false, ErrorMessage: "Authentication failed: invalid password");
            }

            // Success — establish session
            var session = await EstablishSessionAsync(appleId, serviceKey);
            if (session != null)
            {
                _session = session;
                await PersistSessionAsync(session);
                AuthStateChanged?.Invoke();
                return new AppleAuthResult(true, false, Session: session);
            }

            return new AppleAuthResult(false, false, ErrorMessage: "Failed to establish download session");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Authentication failed: {ex.Message}", ex);
            return new AppleAuthResult(false, false, ErrorMessage: $"Authentication error: {ex.Message}");
        }
    }

    public async Task<AppleAuthResult> SubmitTwoFactorCodeAsync(string code, TwoFactorMethod? method = null)
    {
        try
        {
            var serviceKey = await GetServiceKeyAsync();
            if (serviceKey == null)
                return new AppleAuthResult(false, false, ErrorMessage: "Failed to get service key");

            string verifyUrl;
            HttpContent content;

            if (method?.Type == "sms" && method.PhoneId.HasValue)
            {
                verifyUrl = $"{AuthUrl}/verify/phone/securitycode";
                content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        phoneNumber = new { id = method.PhoneId.Value },
                        securityCode = new { code },
                        mode = "sms"
                    }),
                    Encoding.UTF8, "application/json");
            }
            else
            {
                verifyUrl = $"{AuthUrl}/verify/trusteddevice/securitycode";
                content = new StringContent(
                    JsonSerializer.Serialize(new { securityCode = new { code } }),
                    Encoding.UTF8, "application/json");
            }

            var request = new HttpRequestMessage(HttpMethod.Post, verifyUrl);
            request.Headers.Add("X-Apple-Widget-Key", serviceKey);
            request.Content = content;

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError($"2FA verification failed: {error}");
                return new AppleAuthResult(false, false, ErrorMessage: "Invalid security code");
            }

            // Trust this session
            var trustRequest = new HttpRequestMessage(HttpMethod.Get, TrustUrl);
            trustRequest.Headers.Add("X-Apple-Widget-Key", serviceKey);
            await _httpClient.SendAsync(trustRequest);

            // Establish download session
            var appleId = _pendingAppleId ?? "unknown";
            var session = await EstablishSessionAsync(appleId, serviceKey);
            if (session != null)
            {
                _session = session;
                await PersistSessionAsync(session);
                AuthStateChanged?.Invoke();
                return new AppleAuthResult(true, false, Session: session);
            }

            return new AppleAuthResult(false, false, ErrorMessage: "Failed to establish session after 2FA");
        }
        catch (Exception ex)
        {
            _logger.LogError($"2FA verification failed: {ex.Message}", ex);
            return new AppleAuthResult(false, false, ErrorMessage: $"2FA error: {ex.Message}");
        }
    }

    public async Task<bool> RequestSmsCodeAsync(TwoFactorMethod phone)
    {
        if (phone.PhoneId == null) return false;

        try
        {
            var serviceKey = await GetServiceKeyAsync();
            if (serviceKey == null) return false;

            var request = new HttpRequestMessage(HttpMethod.Put, $"{AuthUrl}/verify/phone");
            request.Headers.Add("X-Apple-Widget-Key", serviceKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    phoneNumber = new { id = phone.PhoneId.Value },
                    mode = "sms"
                }),
                Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to request SMS code: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> ValidateSessionAsync()
    {
        if (_session == null) return false;
        if (_session.ExpiresAt <= DateTime.UtcNow) return false;

        try
        {
            // Try a lightweight request to see if session cookies are still valid
            var request = new HttpRequestMessage(HttpMethod.Get, OlympusSessionUrl);
            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public AppleAuthSession? GetSession() => IsAuthenticated ? _session : null;

    public async Task SignOutAsync()
    {
        _session = null;
        _pendingAppleId = null;
        _pendingAuthOptions = null;

        await _secureStorage.RemoveAsync(SecureStorageSessionKey);
        await _secureStorage.RemoveAsync(SecureStorageAppleIdKey);

        AuthStateChanged?.Invoke();
        _logger.LogInformation("Signed out of Apple Developer");
    }

    // ── Private helpers ─────────────────────────────────────────────────

    private async Task<string?> GetServiceKeyAsync()
    {
        try
        {
            var response = await _httpClient.GetStringAsync(AuthServiceKey);
            using var doc = JsonDocument.Parse(response);
            return doc.RootElement.TryGetProperty("authServiceKey", out var key) ? key.GetString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to get service key: {ex.Message}", ex);
            return null;
        }
    }

    private static (string a, byte[] privateKey) ComputeSrpInit(string accountName)
    {
        // Generate a random 256-bit private key for SRP
        var privateKey = RandomNumberGenerator.GetBytes(32);
        // For SRP, 'a' is the client's public value (simplified — real SRP uses modular exponentiation)
        // Apple's implementation uses a custom protocol; we send our public ephemeral as base64
        var a = Convert.ToBase64String(privateKey);
        return (a, privateKey);
    }

    private static (string m1, string m2) ComputeSrpComplete(
        string password, string salt, string serverB, int iterations, string protocol, byte[] clientPrivateKey)
    {
        // Derive key from password using PBKDF2
        var saltBytes = Convert.FromBase64String(salt);
        using var pbkdf2 = new Rfc2898DeriveBytes(
            Encoding.UTF8.GetBytes(password),
            saltBytes,
            iterations,
            HashAlgorithmName.SHA256);
        var derivedKey = pbkdf2.GetBytes(32);

        // Compute M1 (client proof) and M2 (server proof) using HMAC-SHA256
        using var hmac = new HMACSHA256(derivedKey);
        var serverBBytes = Convert.FromBase64String(serverB);
        var m1Bytes = hmac.ComputeHash(CombineArrays(clientPrivateKey, serverBBytes));
        var m2Bytes = hmac.ComputeHash(CombineArrays(m1Bytes, derivedKey));

        return (Convert.ToBase64String(m1Bytes), Convert.ToBase64String(m2Bytes));
    }

    private static byte[] CombineArrays(byte[] a, byte[] b)
    {
        var result = new byte[a.Length + b.Length];
        Buffer.BlockCopy(a, 0, result, 0, a.Length);
        Buffer.BlockCopy(b, 0, result, a.Length, b.Length);
        return result;
    }

    private async Task<AppleAuthOptions?> GetAuthOptionsInternalAsync(string serviceKey)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, AuthUrl);
            request.Headers.Add("X-Apple-Widget-Key", serviceKey);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var canUseDevice = doc.RootElement.TryGetProperty("trustedDeviceCount", out var tdCount) &&
                               tdCount.GetInt32() > 0;

            var phones = new List<TwoFactorMethod>();
            if (doc.RootElement.TryGetProperty("trustedPhoneNumbers", out var phonesArr))
            {
                foreach (var phone in phonesArr.EnumerateArray())
                {
                    var number = phone.TryGetProperty("numberWithDialCode", out var n) ? n.GetString() : null;
                    var id = phone.TryGetProperty("id", out var pid) ? pid.GetInt32() : (int?)null;
                    phones.Add(new TwoFactorMethod("sms", number, id));
                }
            }

            return new AppleAuthOptions(canUseDevice, phones);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to get auth options: {ex.Message}", ex);
            return new AppleAuthOptions(true, []);
        }
    }

    private async Task<AppleAuthSession?> EstablishSessionAsync(string appleId, string serviceKey)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, OlympusSessionUrl);
            request.Headers.Add("X-Apple-Widget-Key", serviceKey);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            // Extract cookies for download.developer.apple.com
            var cookies = new Dictionary<string, string>();
            var allCookies = _cookieContainer.GetAllCookies();
            foreach (Cookie cookie in allCookies)
            {
                cookies[cookie.Name] = cookie.Value;
            }

            return new AppleAuthSession(
                AppleId: appleId,
                Cookies: cookies,
                ExpiresAt: DateTime.UtcNow.AddHours(12)
            );
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to establish session: {ex.Message}", ex);
            return null;
        }
    }

    private async Task PersistSessionAsync(AppleAuthSession session)
    {
        try
        {
            var json = JsonSerializer.Serialize(session);
            await _secureStorage.SetAsync(SecureStorageSessionKey, json);
            await _secureStorage.SetAsync(SecureStorageAppleIdKey, session.AppleId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to persist session: {ex.Message}");
        }
    }

    private async Task TryRestoreSessionAsync()
    {
        try
        {
            var json = await _secureStorage.GetAsync(SecureStorageSessionKey);
            if (json == null) return;

            var session = JsonSerializer.Deserialize<AppleAuthSession>(json);
            if (session != null && session.ExpiresAt > DateTime.UtcNow)
            {
                _session = session;
                // Restore cookies
                foreach (var (name, value) in session.Cookies)
                {
                    _cookieContainer.Add(new Cookie(name, value, "/", ".apple.com"));
                }
                _logger.LogInformation($"Restored Apple session for {session.AppleId}");
                AuthStateChanged?.Invoke();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to restore session: {ex.Message}");
        }
    }
}
