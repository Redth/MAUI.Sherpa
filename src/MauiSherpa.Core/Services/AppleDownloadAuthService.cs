using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Implements Apple Developer authentication for Xcode downloads.
/// Uses Apple's session-based auth (SRP-6a protocol + hashcash + 2FA).
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
    private string? _pendingSessionId;
    private string? _pendingScnt;
    private string? _pendingServiceKey;

    // Apple auth endpoints
    // Olympus only returns authServiceKey for the iTunes Connect hostname variant.
    private const string AuthServiceKey = "https://appstoreconnect.apple.com/olympus/v1/app/config?hostname=itunesconnect.apple.com";
    private const string FederateUrl = "https://idmsa.apple.com/appleauth/auth/federate";
    private const string SignInInitUrl = "https://idmsa.apple.com/appleauth/auth/signin/init";
    private const string SignInCompleteUrl = "https://idmsa.apple.com/appleauth/auth/signin/complete";
    private const string AuthUrl = "https://idmsa.apple.com/appleauth/auth";
    private const string TrustUrl = "https://idmsa.apple.com/appleauth/auth/2sv/trust";
    private const string OlympusSessionUrl = "https://appstoreconnect.apple.com/olympus/v1/session";

    private const string SecureStorageSessionKey = "apple_download_session";
    private const string SecureStorageAppleIdKey = "apple_download_appleid";

    // RFC 5054 2048-bit SRP group
    // RFC 5054 2048-bit SRP group prime
    private static readonly BigInteger SrpN = BigInteger.Parse(
        "00AC6BDB41324A9A9BF166DE5E1389582FAF72B6651987EE07FC3192943DB56050" +
        "A37329CBB4A099ED8193E0757767A13DD52312AB4B03310DCD7F48A9DA04FD50" +
        "E8083969EDB767B0CF6095179A163AB3661A05FBD5FAAAE82918A9962F0B93B8" +
        "55F97993EC975EEAA80D740ADBF4FF747359D041D5C33EA71D281E446B14773B" +
        "CA97B43A23FB801676BD207A436C6481F1D2B9078717461A5B9D32E688F87748" +
        "544523B524B0D57D5EA77A2775D2ECFA032CFBDBF52FB3786160279004E57AE6" +
        "AF874E7303CE53299CCC041C7BC308D82A5698F3A8D0C38271AE35F8E9DBFBB6" +
        "94B5C803D89F7AE435DE236D525F54759B65E372FCD68EF20FA7111F9E4AFF73", System.Globalization.NumberStyles.HexNumber);
    private static readonly BigInteger SrpG = new BigInteger(2);

    // N as big-endian unsigned bytes (256 bytes for 2048-bit)
    private static readonly byte[] NBytes = SrpN.ToByteArray(isUnsigned: true, isBigEndian: true);
    private static readonly int PadLength = NBytes.Length; // 256

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

        // Use SocketsHttpHandler explicitly — the platform default (NSUrlSessionHandler
        // on macOS) gets a 403 from Apple's Olympus endpoint.
        var handler = new SocketsHttpHandler
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

            // Step 2: Compute SRP client ephemeral
            var (aPublicBase64, aPrivate) = ComputeSrpInit();

            // Step 3: Federate and mint hashcash
            var hashcashToken = await FederateAndMintHashcashAsync(appleId, serviceKey);

            // Step 4: Send SRP init
            var initRequest = new HttpRequestMessage(HttpMethod.Post, SignInInitUrl);
            initRequest.Headers.Add("X-Apple-Widget-Key", serviceKey);
            initRequest.Headers.Add("X-Requested-With", "XMLHttpRequest");
            initRequest.Content = new StringContent(
                JsonSerializer.Serialize(new { a = aPublicBase64, accountName = appleId, protocols = new[] { "s2k", "s2k_fo" } }),
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

            // Step 5: Compute SRP complete values
            var (m1, m2) = ComputeSrpComplete(appleId, password, salt, serverB, iterations, protocol, aPrivate);

            // Step 6: Send SRP complete with hashcash
            var completeRequest = new HttpRequestMessage(HttpMethod.Post, SignInCompleteUrl);
            completeRequest.Headers.Add("X-Apple-Widget-Key", serviceKey);
            completeRequest.Headers.Add("X-Requested-With", "XMLHttpRequest");
            if (hashcashToken != null)
                completeRequest.Headers.Add("X-Apple-HC", hashcashToken);

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

                // Save session headers needed for 2FA requests
                _pendingServiceKey = serviceKey;
                _pendingSessionId = completeResponse.Headers.TryGetValues("X-Apple-ID-Session-Id", out var sidValues)
                    ? sidValues.FirstOrDefault() : null;
                _pendingScnt = completeResponse.Headers.TryGetValues("scnt", out var scntValues)
                    ? scntValues.FirstOrDefault() : null;
                _logger.LogInformation($"Session headers - SessionId: {(_pendingSessionId != null ? "present" : "MISSING")}, scnt: {(_pendingScnt != null ? "present" : "MISSING")}");

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
            var serviceKey = _pendingServiceKey ?? await GetServiceKeyAsync();
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

            _logger.LogInformation($"Submitting 2FA code to: {verifyUrl}, SessionId: {(_pendingSessionId != null ? "present" : "MISSING")}, scnt: {(_pendingScnt != null ? "present" : "MISSING")}");

            var request = new HttpRequestMessage(HttpMethod.Post, verifyUrl);
            request.Headers.Add("X-Apple-Widget-Key", serviceKey);
            if (_pendingSessionId != null)
                request.Headers.Add("X-Apple-ID-Session-Id", _pendingSessionId);
            if (_pendingScnt != null)
                request.Headers.Add("scnt", _pendingScnt);
            request.Content = content;

            var response = await _httpClient.SendAsync(request);
            _logger.LogInformation($"2FA response status: {(int)response.StatusCode} {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError($"2FA verification failed: {error}");
                return new AppleAuthResult(false, false, ErrorMessage: "Invalid security code");
            }

            // Trust this session
            var trustRequest = new HttpRequestMessage(HttpMethod.Get, TrustUrl);
            trustRequest.Headers.Add("X-Apple-Widget-Key", serviceKey);
            if (_pendingSessionId != null)
                trustRequest.Headers.Add("X-Apple-ID-Session-Id", _pendingSessionId);
            if (_pendingScnt != null)
                trustRequest.Headers.Add("scnt", _pendingScnt);
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

    public CookieCollection GetAllCookies() => _cookieContainer.GetAllCookies();

    public HttpClient CreateAuthenticatedHttpClient()
    {
        // Share the same CookieContainer so cookies from listDownloads.action
        // flow to the actual download request
        var handler = new SocketsHttpHandler
        {
            CookieContainer = _cookieContainer,
            AllowAutoRedirect = true,
            UseCookies = true
        };
        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("User-Agent", "MauiSherpa");
        return client;
    }

    public async Task SignOutAsync()
    {
        _session = null;
        _pendingAppleId = null;
        _pendingAuthOptions = null;
        _pendingSessionId = null;
        _pendingScnt = null;
        _pendingServiceKey = null;

        await _secureStorage.RemoveAsync(SecureStorageSessionKey);
        await _secureStorage.RemoveAsync(SecureStorageAppleIdKey);

        AuthStateChanged?.Invoke();
        _logger.LogInformation("Signed out of Apple Developer");
    }

    // ── SRP-6a Implementation ───────────────────────────────────────────

    private static (string ABase64, BigInteger aPrivate) ComputeSrpInit()
    {
        // Generate random 256-bit private exponent
        var aBytes = RandomNumberGenerator.GetBytes(32);
        var aPrivate = new BigInteger(aBytes, isUnsigned: true, isBigEndian: true);

        // A = g^a mod N
        var A = BigInteger.ModPow(SrpG, aPrivate, SrpN);
        var ABytes = A.ToByteArray(isUnsigned: true, isBigEndian: true);

        return (Convert.ToBase64String(ABytes), aPrivate);
    }

    private static (string M1Base64, string M2Base64) ComputeSrpComplete(
        string accountName, string password, string saltBase64, string serverBBase64,
        int iterations, string protocol, BigInteger aPrivate)
    {
        var saltBytes = Convert.FromBase64String(saltBase64);
        var BBytes = Convert.FromBase64String(serverBBase64);
        var B = new BigInteger(BBytes, isUnsigned: true, isBigEndian: true);

        // Recompute A from aPrivate
        var A = BigInteger.ModPow(SrpG, aPrivate, SrpN);
        var ABytes = A.ToByteArray(isUnsigned: true, isBigEndian: true);

        // Password derivation: SHA256(password) → PBKDF2
        var passwordHash = SHA256.HashData(Encoding.UTF8.GetBytes(password));

        byte[] derivedKey;
        if (protocol == "s2k_fo")
        {
            // s2k_fo: hex-encode the SHA256 hash, then use hex string bytes as PBKDF2 password
            var passHex = Encoding.UTF8.GetBytes(Convert.ToHexString(passwordHash).ToLowerInvariant());
            using var pbkdf2 = new Rfc2898DeriveBytes(passHex, saltBytes, iterations, HashAlgorithmName.SHA256);
            derivedKey = pbkdf2.GetBytes(32);
        }
        else
        {
            // s2k: raw SHA256 bytes as PBKDF2 password
            using var pbkdf2 = new Rfc2898DeriveBytes(passwordHash, saltBytes, iterations, HashAlgorithmName.SHA256);
            derivedKey = pbkdf2.GetBytes(32);
        }

        // x = SHA256(salt || SHA256(0x3A || derivedKey))  — Apple GSA variant (no username in x)
        var innerHash = SHA256.HashData(CombineArrays(new byte[] { 0x3A }, derivedKey));
        var xHash = SHA256.HashData(CombineArrays(saltBytes, innerHash));
        var x = new BigInteger(xHash, isUnsigned: true, isBigEndian: true);

        // u = SHA256(pad(A) || pad(B))
        var uHash = SHA256.HashData(CombineArrays(PadTo(ABytes, PadLength), PadTo(BBytes, PadLength)));
        var u = new BigInteger(uHash, isUnsigned: true, isBigEndian: true);

        // k = SHA256(N || pad(g))
        var gBytes = SrpG.ToByteArray(isUnsigned: true, isBigEndian: true);
        var kHash = SHA256.HashData(CombineArrays(NBytes, PadTo(gBytes, PadLength)));
        var k = new BigInteger(kHash, isUnsigned: true, isBigEndian: true);

        // v = g^x mod N
        var v = BigInteger.ModPow(SrpG, x, SrpN);

        // S = (B - k*v) ^ (a + u*x) mod N
        // Handle negative intermediate: ((B - k*v) % N + N) % N
        var kv = (k * v) % SrpN;
        var diff = (B - kv) % SrpN;
        if (diff.Sign < 0) diff += SrpN;

        var exp = aPrivate + u * x;
        var S = BigInteger.ModPow(diff, exp, SrpN);
        var SBytes = S.ToByteArray(isUnsigned: true, isBigEndian: true);

        // K = SHA256(S) — unpadded
        var K = SHA256.HashData(SBytes);

        // M1 = SHA256(SHA256(N) XOR SHA256(g) || SHA256(accountName) || salt || A || B || K)
        var hashN = SHA256.HashData(NBytes);
        var hashG = SHA256.HashData(PadTo(gBytes, PadLength));
        var xorNg = new byte[hashN.Length];
        for (int i = 0; i < hashN.Length; i++)
            xorNg[i] = (byte)(hashN[i] ^ hashG[i]);

        var hashUser = SHA256.HashData(Encoding.UTF8.GetBytes(accountName));

        var m1Input = CombineArrays(
            xorNg,
            hashUser,
            saltBytes,
            ABytes,
            BBytes,
            K);
        var M1 = SHA256.HashData(m1Input);

        // M2 = SHA256(A || M1 || K)
        var m2Input = CombineArrays(ABytes, M1, K);
        var M2 = SHA256.HashData(m2Input);

        return (Convert.ToBase64String(M1), Convert.ToBase64String(M2));
    }

    // ── Hashcash Implementation ─────────────────────────────────────────

    private async Task<string?> FederateAndMintHashcashAsync(string accountName, string serviceKey)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, FederateUrl);
            request.Headers.Add("X-Apple-Widget-Key", serviceKey);
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { accountName }),
                Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);

            // Extract hashcash parameters from response headers
            string? hcBitsStr = null;
            string? hcChallenge = null;

            if (response.Headers.TryGetValues("X-Apple-HC-Bits", out var bitsValues))
                hcBitsStr = bitsValues.FirstOrDefault();
            if (response.Headers.TryGetValues("X-Apple-HC-Challenge", out var challengeValues))
                hcChallenge = challengeValues.FirstOrDefault();

            if (hcBitsStr == null || hcChallenge == null)
            {
                _logger.LogWarning("No hashcash challenge received from federate endpoint");
                return null;
            }

            if (!int.TryParse(hcBitsStr, out var hcBits))
            {
                _logger.LogWarning($"Invalid hashcash bits value: {hcBitsStr}");
                return null;
            }

            _logger.LogInformation($"Minting hashcash: {hcBits} bits, challenge: {hcChallenge}");
            var token = MintHashcash(hcBits, hcChallenge);
            _logger.LogInformation($"Hashcash minted successfully");
            return token;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Federate/hashcash failed (continuing without): {ex.Message}");
            return null;
        }
    }

    private static string MintHashcash(int bits, string challenge)
    {
        var date = DateTime.UtcNow.ToString("yyMMddHHmmss");

        for (long counter = 0; ; counter++)
        {
            var stamp = $"1:{bits}:{date}:{challenge}::{counter}";
            var hash = SHA1.HashData(Encoding.UTF8.GetBytes(stamp));

            if (HasLeadingZeroBits(hash, bits))
                return stamp;
        }
    }

    private static bool HasLeadingZeroBits(byte[] hash, int requiredBits)
    {
        int fullBytes = requiredBits / 8;
        int remainingBits = requiredBits % 8;

        for (int i = 0; i < fullBytes; i++)
        {
            if (hash[i] != 0) return false;
        }

        if (remainingBits > 0)
        {
            // Check the top `remainingBits` bits of the next byte are zero
            var mask = (byte)(0xFF << (8 - remainingBits));
            if ((hash[fullBytes] & mask) != 0) return false;
        }

        return true;
    }

    // ── Helper Methods ──────────────────────────────────────────────────

    private static byte[] PadTo(byte[] data, int length)
    {
        if (data.Length >= length) return data;
        var padded = new byte[length];
        Buffer.BlockCopy(data, 0, padded, length - data.Length, data.Length);
        return padded;
    }

    private static byte[] CombineArrays(params byte[][] arrays)
    {
        int totalLength = 0;
        foreach (var arr in arrays) totalLength += arr.Length;

        var result = new byte[totalLength];
        int offset = 0;
        foreach (var arr in arrays)
        {
            Buffer.BlockCopy(arr, 0, result, offset, arr.Length);
            offset += arr.Length;
        }
        return result;
    }

    // ── Private helpers ─────────────────────────────────────────────────

    private async Task<string?> GetServiceKeyAsync()
    {
        try
        {
            // Use SocketsHttpHandler explicitly — the platform default (NSUrlSessionHandler
            // on macOS) gets a 403 from Apple's Olympus endpoint.
            using var handler = new SocketsHttpHandler();
            using var cleanClient = new HttpClient(handler);
            cleanClient.DefaultRequestHeaders.Add("User-Agent", "MauiSherpa");
            cleanClient.DefaultRequestHeaders.Add("Accept", "application/json");

            var responseText = await cleanClient.GetStringAsync(AuthServiceKey);

            using var doc = JsonDocument.Parse(responseText);
            if (doc.RootElement.TryGetProperty("authServiceKey", out var key))
                return key.GetString();

            _logger.LogError($"Failed to get service key: authServiceKey missing from response: {responseText}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to get service key: {ex.Message}", ex);
            return null;
        }
    }

    private async Task<AppleAuthOptions?> GetAuthOptionsInternalAsync(string serviceKey)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, AuthUrl);
            request.Headers.Add("X-Apple-Widget-Key", serviceKey);
            if (_pendingSessionId != null)
                request.Headers.Add("X-Apple-ID-Session-Id", _pendingSessionId);
            if (_pendingScnt != null)
                request.Headers.Add("scnt", _pendingScnt);

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            _logger.LogInformation($"Auth options response: {(int)response.StatusCode}, body: {json[..Math.Min(json.Length, 500)]}");
            if (!response.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(json);

            // hsa2 accounts support trusted device codes even without trustedDeviceCount
            var isHsa2 = doc.RootElement.TryGetProperty("authenticationType", out var authType) &&
                         authType.GetString() == "hsa2";
            var canUseDevice = isHsa2 ||
                               (doc.RootElement.TryGetProperty("trustedDeviceCount", out var tdCount) &&
                                tdCount.GetInt32() > 0);

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

            // Extract cookies — store domain info for proper download auth
            var cookies = new Dictionary<string, string>();
            var allCookies = _cookieContainer.GetAllCookies();
            foreach (Cookie cookie in allCookies)
            {
                // Store as domain|name=value so we can restore to correct domain
                var key = $"{cookie.Domain}|{cookie.Name}";
                cookies[key] = cookie.Value;
            }
            _logger.LogInformation($"Session cookies: {string.Join(", ", allCookies.Select(c => $"{c.Name}@{c.Domain}"))}");


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
                // Restore cookies with proper domains
                foreach (var (key, value) in session.Cookies)
                {
                    string domain, name;
                    if (key.Contains('|'))
                    {
                        var parts = key.Split('|', 2);
                        domain = parts[0];
                        name = parts[1];
                    }
                    else
                    {
                        domain = ".apple.com";
                        name = key;
                    }
                    _cookieContainer.Add(new Cookie(name, value, "/", domain));
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
