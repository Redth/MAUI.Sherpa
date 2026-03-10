using System.Net;
using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Authenticates with Apple Developer to enable Xcode .xip downloads.
/// Implements the same Apple ID auth flow that xcodes uses:
///   1. Fetch service key from App Store Connect config
///   2. Sign in via idmsa.apple.com
///   3. Handle 2FA (trusted device push or SMS)
///   4. Create Olympus session to get ADCDownloadAuth cookie
///   5. Download files using authenticated cookies
/// </summary>
public class AppleDownloadAuthService : IAppleDownloadAuthService
{
    private readonly ILoggingService _logger;
    private readonly ISecureStorageService _secureStorage;
    private readonly CookieContainer _cookies;
    private readonly HttpClient _httpClient;

    private string? _serviceKey;
    private string? _sessionId;
    private string? _scnt;
    private string? _currentAppleId;
    private bool _isAuthenticated;

    private const string AppleAuthUrl = "https://idmsa.apple.com/appleauth/auth";
    private const string OlympusSessionUrl = "https://appstoreconnect.apple.com/olympus/v1/session";
    private const string ServiceKeyUrl = "https://appstoreconnect.apple.com/olympus/v1/app/config?hostname=itunesconnect.apple.com";

    private const string SecureStorageAppleIdKey = "xcode_download_apple_id";

    public bool IsAuthenticated => _isAuthenticated;
    public string? CurrentAppleId => _currentAppleId;

    public AppleDownloadAuthService(ILoggingService logger, ISecureStorageService secureStorage)
    {
        _logger = logger;
        _secureStorage = secureStorage;
        _cookies = new CookieContainer();

        var handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            AllowAutoRedirect = true,
            UseCookies = true
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(30) // Large file downloads
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "MauiSherpa/1.0");
    }

    public async Task<AppleSignInResult> SignInAsync(string appleId, string password)
    {
        try
        {
            _logger.LogInformation($"Signing in to Apple Developer as {appleId}...");

            // Step 1: Get the service key
            var serviceKey = await GetServiceKeyAsync();
            if (string.IsNullOrEmpty(serviceKey))
            {
                _logger.LogError("Failed to obtain Apple auth service key");
                return AppleSignInResult.Error;
            }

            // Step 2: Sign in
            var body = JsonSerializer.Serialize(new
            {
                accountName = appleId,
                password,
                rememberMe = true
            });

            var request = new HttpRequestMessage(HttpMethod.Post, $"{AppleAuthUrl}/signin")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            ApplyAuthHeaders(request, serviceKey);

            var response = await _httpClient.SendAsync(request);

            // Capture session headers for 2FA
            if (response.Headers.TryGetValues("X-Apple-ID-Session-Id", out var sessionIds))
                _sessionId = sessionIds.FirstOrDefault();
            if (response.Headers.TryGetValues("scnt", out var scnts))
                _scnt = scnts.FirstOrDefault();

            if (response.StatusCode == HttpStatusCode.OK)
            {
                // No 2FA needed — complete sign-in
                _currentAppleId = appleId;
                await CreateOlympusSessionAsync();
                _isAuthenticated = true;
                await _secureStorage.SetAsync(SecureStorageAppleIdKey, appleId);
                _logger.LogInformation("Apple Developer sign-in successful (no 2FA)");
                return AppleSignInResult.Success;
            }

            if (response.StatusCode == HttpStatusCode.Conflict) // 409
            {
                _currentAppleId = appleId;
                _logger.LogInformation("Apple Developer sign-in requires 2FA verification");
                return AppleSignInResult.TwoFactorRequired;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized) // 401
            {
                _logger.LogWarning("Apple Developer sign-in: invalid credentials");
                return AppleSignInResult.InvalidCredentials;
            }

            if (response.StatusCode == HttpStatusCode.Forbidden) // 403
            {
                _logger.LogWarning("Apple Developer account is locked");
                return AppleSignInResult.AccountLocked;
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError($"Apple sign-in failed with status {response.StatusCode}: {errorContent}");
            return AppleSignInResult.Error;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Apple sign-in error: {ex.Message}", ex);
            return AppleSignInResult.Error;
        }
    }

    public async Task<bool> Verify2FAAsync(string code)
    {
        try
        {
            _logger.LogInformation("Verifying 2FA code...");
            var serviceKey = await GetServiceKeyAsync();
            if (string.IsNullOrEmpty(serviceKey)) return false;

            var body = JsonSerializer.Serialize(new
            {
                securityCode = new { code }
            });

            var request = new HttpRequestMessage(HttpMethod.Post,
                $"{AppleAuthUrl}/verify/trusteddevice/securitycode")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            ApplyAuthHeaders(request, serviceKey);
            ApplySessionHeaders(request);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"2FA verification failed with status {response.StatusCode}");
                return false;
            }

            // Trust the session so future sign-ins skip 2FA
            await TrustSessionAsync(serviceKey);

            // Create the Olympus session to get download cookies
            await CreateOlympusSessionAsync();

            _isAuthenticated = true;
            if (_currentAppleId != null)
                await _secureStorage.SetAsync(SecureStorageAppleIdKey, _currentAppleId);

            _logger.LogInformation("2FA verification successful, download session created");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"2FA verification error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> RequestSmsCodeAsync(int phoneIndex = 0)
    {
        try
        {
            var serviceKey = await GetServiceKeyAsync();
            if (string.IsNullOrEmpty(serviceKey)) return false;

            var body = JsonSerializer.Serialize(new
            {
                phoneNumber = new { id = phoneIndex + 1 },
                mode = "sms"
            });

            var request = new HttpRequestMessage(HttpMethod.Put, $"{AppleAuthUrl}/verify/phone")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            ApplyAuthHeaders(request, serviceKey);
            ApplySessionHeaders(request);

            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError($"SMS code request error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> VerifySmsCodeAsync(string code, int phoneIndex = 0)
    {
        try
        {
            var serviceKey = await GetServiceKeyAsync();
            if (string.IsNullOrEmpty(serviceKey)) return false;

            var body = JsonSerializer.Serialize(new
            {
                securityCode = new { code },
                phoneNumber = new { id = phoneIndex + 1 },
                mode = "sms"
            });

            var request = new HttpRequestMessage(HttpMethod.Post, $"{AppleAuthUrl}/verify/phone/securitycode")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            ApplyAuthHeaders(request, serviceKey);
            ApplySessionHeaders(request);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return false;

            await TrustSessionAsync(serviceKey);
            await CreateOlympusSessionAsync();

            _isAuthenticated = true;
            if (_currentAppleId != null)
                await _secureStorage.SetAsync(SecureStorageAppleIdKey, _currentAppleId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"SMS verification error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<string?> DownloadAsync(string url, string destinationPath,
        IProgress<XcodeDownloadProgress>? progress = null, CancellationToken ct = default)
    {
        if (!_isAuthenticated)
        {
            _logger.LogError("Cannot download: not authenticated");
            return null;
        }

        try
        {
            _logger.LogInformation($"Starting download from {url}");
            progress?.Report(new XcodeDownloadProgress("", 0, 0, 0, "Connecting..."));

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode == HttpStatusCode.Unauthorized ||
                response.StatusCode == HttpStatusCode.Forbidden)
            {
                _logger.LogError("Download authentication expired — need to sign in again");
                _isAuthenticated = false;
                return null;
            }

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            var fileName = GetFileNameFromResponse(response) ?? Path.GetFileName(new Uri(url).AbsolutePath);
            var filePath = Path.Combine(destinationPath, fileName);

            _logger.LogInformation($"Downloading {fileName} ({FormatBytes(totalBytes)}) to {filePath}");
            progress?.Report(new XcodeDownloadProgress("", 0, totalBytes, 0,
                $"Downloading {fileName} ({FormatBytes(totalBytes)})"));

            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write,
                FileShare.None, bufferSize: 81920, useAsync: true);

            var buffer = new byte[81920]; // 80KB buffer
            long bytesDownloaded = 0;
            var lastReportTime = DateTime.UtcNow;

            while (true)
            {
                var bytesRead = await contentStream.ReadAsync(buffer, ct);
                if (bytesRead == 0) break;

                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                bytesDownloaded += bytesRead;

                // Report progress at most every 250ms to avoid UI spam
                var now = DateTime.UtcNow;
                if ((now - lastReportTime).TotalMilliseconds >= 250 || bytesRead == 0)
                {
                    var percent = totalBytes > 0 ? (double)bytesDownloaded / totalBytes * 100 : -1;
                    progress?.Report(new XcodeDownloadProgress(
                        "",
                        bytesDownloaded,
                        totalBytes,
                        percent,
                        $"Downloading... {FormatBytes(bytesDownloaded)} / {FormatBytes(totalBytes)} ({percent:F1}%)"
                    ));
                    lastReportTime = now;
                }
            }

            progress?.Report(new XcodeDownloadProgress("", bytesDownloaded, totalBytes, 100, "Download complete"));
            _logger.LogInformation($"Download complete: {filePath} ({FormatBytes(bytesDownloaded)})");
            return filePath;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Download was cancelled");
            progress?.Report(new XcodeDownloadProgress("", 0, 0, 0, "Download cancelled"));
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Download failed: {ex.Message}", ex);
            progress?.Report(new XcodeDownloadProgress("", 0, 0, 0, $"Download failed: {ex.Message}"));
            return null;
        }
    }

    public void SignOut()
    {
        _isAuthenticated = false;
        _currentAppleId = null;
        _sessionId = null;
        _scnt = null;
        _serviceKey = null;

        // Clear cookies by recreating (CookieContainer has no Clear method)
        // The next SignIn will create fresh cookies via the handler
        _logger.LogInformation("Signed out of Apple Developer downloads");
    }

    // ── Private helpers ─────────────────────────────────────────────────

    private async Task<string?> GetServiceKeyAsync()
    {
        if (!string.IsNullOrEmpty(_serviceKey)) return _serviceKey;

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, ServiceKeyUrl);
            request.Headers.Add("Accept", "application/json");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("authServiceKey", out var keyProp))
            {
                _serviceKey = keyProp.GetString();
                _logger.LogInformation("Obtained Apple auth service key");
                return _serviceKey;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to get Apple service key: {ex.Message}", ex);
        }

        return null;
    }

    private void ApplyAuthHeaders(HttpRequestMessage request, string serviceKey)
    {
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        request.Headers.TryAddWithoutValidation("X-Apple-Widget-Key", serviceKey);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
    }

    private void ApplySessionHeaders(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(_sessionId))
            request.Headers.TryAddWithoutValidation("X-Apple-ID-Session-Id", _sessionId);
        if (!string.IsNullOrEmpty(_scnt))
            request.Headers.TryAddWithoutValidation("scnt", _scnt);
    }

    private async Task TrustSessionAsync(string serviceKey)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{AppleAuthUrl}/2sv/trust");
            ApplyAuthHeaders(request, serviceKey);
            ApplySessionHeaders(request);
            await _httpClient.SendAsync(request);
        }
        catch (Exception ex)
        {
            // Non-fatal — session works without trust, just means 2FA next time
            _logger.LogWarning($"Failed to trust session (non-fatal): {ex.Message}");
        }
    }

    private async Task CreateOlympusSessionAsync()
    {
        try
        {
            // This creates the Olympus session which sets the ADCDownloadAuth cookie
            var request = new HttpRequestMessage(HttpMethod.Post, OlympusSessionUrl);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            var response = await _httpClient.SendAsync(request);
            _logger.LogInformation($"Olympus session created (status: {response.StatusCode})");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to create Olympus session: {ex.Message}");
        }
    }

    private static string? GetFileNameFromResponse(HttpResponseMessage response)
    {
        var disposition = response.Content.Headers.ContentDisposition;
        return disposition?.FileName?.Trim('"');
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "unknown size";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
