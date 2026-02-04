using AppleAppStoreConnect;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Apple Connect Service using AppStoreConnectClient library
/// </summary>
public class AppleConnectService : IAppleConnectService
{
    private readonly IAppleIdentityStateService _identityState;
    private readonly IAppleIdentityService _identityService;
    private readonly ISecureStorageService _secureStorage;
    private readonly ILoggingService _logger;

    public AppleConnectService(
        IAppleIdentityStateService identityState,
        IAppleIdentityService identityService,
        ISecureStorageService secureStorage,
        ILoggingService logger)
    {
        _identityState = identityState;
        _identityService = identityService;
        _secureStorage = secureStorage;
        _logger = logger;
    }

    private async Task<AppStoreConnectClient> GetClientAsync()
    {
        var identity = _identityState.SelectedIdentity 
            ?? throw new InvalidOperationException("No Apple identity selected");
        
        _logger.LogInformation($"Getting client for identity: {identity.Name} (ID: {identity.Id})");
        
        // Get P8 content - try secure storage first, fall back to identity
        string? p8Content = null;
        if (!string.IsNullOrEmpty(identity.Id))
        {
            p8Content = await _secureStorage.GetAsync($"apple_identity_p8_{identity.Id}");
            _logger.LogInformation($"P8 from secure storage: {(p8Content != null ? $"{p8Content.Length} chars" : "null")}");
        }
        
        if (string.IsNullOrEmpty(p8Content))
        {
            p8Content = identity.P8KeyContent;
            _logger.LogInformation($"P8 from identity: {(p8Content != null ? $"{p8Content.Length} chars" : "null")}");
        }
        
        if (string.IsNullOrEmpty(p8Content))
        {
            _logger.LogError($"P8 key content not available. Identity has P8KeyPath: {identity.P8KeyPath}");
            throw new InvalidOperationException("P8 key content not available for selected identity. Please edit the identity and re-enter the P8 key.");
        }

        // The configuration expects base64-encoded private key
        // The P8 file content is already PEM format, we need to extract and base64 encode
        var privateKeyBase64 = ConvertP8ToBase64(p8Content);
        
        var config = new AppStoreConnectConfiguration(
            identity.KeyId,
            identity.IssuerId,
            privateKeyBase64);
        
        return new AppStoreConnectClient(config);
    }

    private string ConvertP8ToBase64(string p8Content)
    {
        // P8 file is PEM format, need to extract the base64 content
        var lines = p8Content.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith("-----"))
            .Where(l => !string.IsNullOrEmpty(l));
        return string.Join("", lines);
    }

    // Bundle IDs
    public async Task<IReadOnlyList<AppleBundleId>> GetBundleIdsAsync()
    {
        _logger.LogInformation("Fetching bundle IDs from App Store Connect...");
        try
        {
            var client = await GetClientAsync();
            var response = await client.ListBundleIdsAsync(
                filterId: null, filterIdentifier: null, filterName: null, 
                filterPlatform: null, filterSeedId: null,
                include: null, sort: null, limit: 100,
                limitProfiles: null, limitBundleIdCapabilities: null,
                fieldsBundleIds: null, fieldsProfiles: null, 
                fieldBundleIdCapabilities: null, fieldsApps: null,
                cancellationToken: default);

            return response.Data
                .Select(b => new AppleBundleId(
                    b.Id,
                    b.Attributes?.Identifier ?? "",
                    b.Attributes?.Name ?? "",
                    b.Attributes?.Platform.ToString() ?? "UNIVERSAL",
                    b.Attributes?.SeedId))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to fetch bundle IDs: {ex.Message}", ex);
            throw;
        }
    }

    public async Task<AppleBundleId> CreateBundleIdAsync(string identifier, string name, string platform)
    {
        _logger.LogInformation($"Creating bundle ID: {identifier}");
        try
        {
            var client = await GetClientAsync();
            var platformEnum = platform.ToUpperInvariant() switch
            {
                "IOS" => Platform.IOS,
                "MACOS" or "MAC_OS" => Platform.MAC_OS,
                _ => Platform.UNIVERSAL
            };
            
            var attributes = new BundleIdAttributes
            {
                Identifier = identifier,
                Name = name,
                Platform = platformEnum
            };
            
            var response = await client.CreateBundleIdAsync(attributes, default);
            
            return new AppleBundleId(
                response.Data.Id,
                response.Data.Attributes?.Identifier ?? identifier,
                response.Data.Attributes?.Name ?? name,
                response.Data.Attributes?.Platform.ToString() ?? platform,
                response.Data.Attributes?.SeedId);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to create bundle ID: {ex.Message}", ex);
            throw;
        }
    }

    public async Task DeleteBundleIdAsync(string id)
    {
        _logger.LogInformation($"Deleting bundle ID: {id}");
        try
        {
            var client = await GetClientAsync();
            await client.DeleteBundleIdAsync(id, default);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to delete bundle ID: {ex.Message}", ex);
            throw;
        }
    }

    // Devices
    public async Task<IReadOnlyList<AppleDevice>> GetDevicesAsync()
    {
        _logger.LogInformation("Fetching devices from App Store Connect...");
        try
        {
            var client = await GetClientAsync();
            var response = await client.ListDevicesAsync(
                filterId: null, filterIdentifier: null, filterName: null,
                filterPlatform: null, filterStatus: null, filterUdid: null,
                include: null, sort: null, limit: 100,
                limitProfiles: null, limitBundleIdCapabilities: null,
                fieldsDevices: null,
                cancellationToken: default);

            return response.Data
                .Select(d => new AppleDevice(
                    d.Id,
                    d.Attributes?.Udid ?? "",
                    d.Attributes?.Name ?? "",
                    d.Attributes?.Platform.ToString() ?? "IOS",
                    d.Attributes?.DeviceClass ?? "IPHONE",
                    d.Attributes?.Status.ToString() ?? "ENABLED",
                    d.Attributes?.Model))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to fetch devices: {ex.Message}", ex);
            throw;
        }
    }

    public async Task<AppleDevice> RegisterDeviceAsync(string udid, string name, string platform)
    {
        _logger.LogInformation($"Registering device: {name}");
        try
        {
            var client = await GetClientAsync();
            var platformEnum = platform.ToUpperInvariant() switch
            {
                "IOS" => Platform.IOS,
                "MACOS" or "MAC_OS" => Platform.MAC_OS,
                _ => Platform.IOS
            };
            
            var attributes = new DeviceAttributes
            {
                Udid = udid,
                Name = name,
                Platform = platformEnum
            };
            
            var response = await client.RegisterDeviceAsync(attributes, default);
            
            return new AppleDevice(
                response.Data.Id,
                response.Data.Attributes?.Udid ?? udid,
                response.Data.Attributes?.Name ?? name,
                response.Data.Attributes?.Platform.ToString() ?? platform,
                response.Data.Attributes?.DeviceClass ?? "IPHONE",
                response.Data.Attributes?.Status.ToString() ?? "ENABLED",
                response.Data.Attributes?.Model);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to register device: {ex.Message}", ex);
            throw;
        }
    }

    public async Task UpdateDeviceStatusAsync(string id, bool enabled)
    {
        _logger.LogInformation($"Updating device {id} status: {(enabled ? "ENABLED" : "DISABLED")}");
        try
        {
            var client = await GetClientAsync();
            var attributes = new DeviceAttributes
            {
                Status = enabled ? DeviceStatus.ENABLED : DeviceStatus.DISABLED
            };
            await client.ModifyDeviceAsync(id, attributes, default);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to update device status: {ex.Message}", ex);
            throw;
        }
    }

    // Certificates
    public async Task<IReadOnlyList<AppleCertificate>> GetCertificatesAsync()
    {
        _logger.LogInformation("Fetching certificates from App Store Connect...");
        try
        {
            var client = await GetClientAsync();
            var response = await client.ListCertificatesAsync(
                filterId: null, filterDisplayName: null, filterSerialNumber: null,
                filterCertificateType: null,
                sort: null, limit: 100,
                fieldsCertificates: null,
                cancellationToken: default);

            return response.Data
                .Select(c => new AppleCertificate(
                    c.Id,
                    c.Attributes?.DisplayName ?? c.Attributes?.Name ?? "",
                    c.Attributes?.CertificateType.ToString() ?? "DEVELOPMENT",
                    c.Attributes?.Platform.ToString() ?? "IOS",
                    DateTime.UtcNow.AddYears(1), // CertificateAttributes doesn't have ExpirationDate directly
                    c.Attributes?.SerialNumber ?? ""))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to fetch certificates: {ex.Message}", ex);
            throw;
        }
    }

    public async Task<AppleCertificateCreateResult> CreateCertificateAsync(string certificateType, string? commonName = null, string? passphrase = null)
    {
        _logger.LogInformation($"Creating certificate of type: {certificateType}");
        try
        {
            var client = await GetClientAsync();
            
            // Parse certificate type
            var certType = certificateType.ToUpperInvariant() switch
            {
                "DEVELOPMENT" or "IOS_DEVELOPMENT" => CertificateType.IOS_DEVELOPMENT,
                "DISTRIBUTION" or "IOS_DISTRIBUTION" => CertificateType.IOS_DISTRIBUTION,
                "MAC_APP_DEVELOPMENT" => CertificateType.MAC_APP_DEVELOPMENT,
                "MAC_APP_DISTRIBUTION" => CertificateType.MAC_APP_DISTRIBUTION,
                "MAC_INSTALLER_DISTRIBUTION" => CertificateType.MAC_INSTALLER_DISTRIBUTION,
                "DEVELOPER_ID_APPLICATION" => CertificateType.DEVELOPER_ID_APPLICATION,
                "DEVELOPER_ID_KEXT" => CertificateType.DEVELOPER_ID_KEXT,
                _ => CertificateType.DEVELOPMENT
            };
            
            // Use machine name as default common name
            var cn = commonName ?? Environment.MachineName;
            
            // Create certificate with CSR
            var response = await client.CreateCertificateWithSigningRequestAsync(cn, certType);
            
            // Convert to X509Certificate2 to get details and export as PFX
            var certContent = response.Data.Attributes.CertificateContent;
            var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(
                Convert.FromBase64String(certContent));
            
            var pfxData = cert.Export(
                System.Security.Cryptography.X509Certificates.X509ContentType.Pfx, 
                passphrase);
            
            return new AppleCertificateCreateResult(
                response.Data.Id,
                pfxData,
                cert.NotAfter);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to create certificate: {ex.Message}", ex);
            throw;
        }
    }

    public async Task RevokeCertificateAsync(string id)
    {
        _logger.LogInformation($"Revoking certificate: {id}");
        try
        {
            var client = await GetClientAsync();
            var success = await client.RevokeCertificateAsync(id, default);
            
            if (!success)
            {
                throw new InvalidOperationException($"Failed to revoke certificate '{id}'");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to revoke certificate: {ex.Message}", ex);
            throw;
        }
    }

    // Provisioning Profiles
    public async Task<IReadOnlyList<AppleProfile>> GetProfilesAsync()
    {
        _logger.LogInformation("Fetching profiles from App Store Connect...");
        try
        {
            var client = await GetClientAsync();
            var response = await client.ListProfilesAsync(
                filterId: null, filterName: null, 
                filterProfileState: null, filterProfileType: null,
                include: "bundleId", sort: null, limit: 100,
                limitCertificates: null, limitDevices: null,
                fieldsProfiles: null, fieldsBundleIds: null,
                fieldsCertificates: null, fieldsDevices: null,
                cancellationToken: default);

            return response.Data
                .Select(p => new AppleProfile(
                    p.Id,
                    p.Attributes?.Name ?? "",
                    p.Attributes?.ProfileType.ToString() ?? "IOS_APP_DEVELOPMENT",
                    p.Attributes?.Platform.ToString() ?? "IOS",
                    p.Attributes?.ProfileState.ToString() ?? "ACTIVE",
                    p.Attributes?.ExpirationDate?.DateTime ?? DateTime.UtcNow.AddYears(1),
                    "", // BundleIdIdentifier - would need to resolve from relationships
                    p.Attributes?.Uuid ?? ""))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to fetch profiles: {ex.Message}", ex);
            throw;
        }
    }

    public async Task<AppleProfile> CreateProfileAsync(AppleProfileCreateRequest request)
    {
        _logger.LogInformation($"Creating profile: {request.Name} ({request.ProfileType})");
        try
        {
            var client = await GetClientAsync();
            
            // Parse profile type
            var profileType = Enum.Parse<ProfileType>(request.ProfileType, ignoreCase: true);
            
            // Build the request
            var response = await client.CreateProfileAsync(
                name: request.Name,
                profileType: profileType,
                bundleIdId: request.BundleIdResourceId,
                certificateIds: request.CertificateIds.ToArray(),
                deviceIds: request.DeviceIds?.ToArray(),
                cancellationToken: default);
            
            var p = response.Data;
            return new AppleProfile(
                p.Id,
                p.Attributes?.Name ?? request.Name,
                p.Attributes?.ProfileType.ToString() ?? request.ProfileType,
                p.Attributes?.Platform.ToString() ?? "IOS",
                p.Attributes?.ProfileState.ToString() ?? "ACTIVE",
                p.Attributes?.ExpirationDate?.DateTime ?? DateTime.UtcNow.AddYears(1),
                "", // BundleIdIdentifier
                p.Attributes?.Uuid ?? "");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to create profile: {ex.Message}", ex);
            throw;
        }
    }

    public async Task<byte[]> DownloadProfileAsync(string id)
    {
        _logger.LogInformation($"Downloading profile: {id}");
        try
        {
            // The profile content is included in the list response
            var client = await GetClientAsync();
            var response = await client.ListProfilesAsync(
                filterId: new[] { id }, filterName: null,
                filterProfileState: null, filterProfileType: null,
                include: null, sort: null, limit: 1,
                limitCertificates: null, limitDevices: null,
                fieldsProfiles: null, fieldsBundleIds: null,
                fieldsCertificates: null, fieldsDevices: null,
                cancellationToken: default);

            var profile = response.Data.FirstOrDefault();
            if (profile?.Attributes?.ProfileContent != null)
            {
                return Convert.FromBase64String(profile.Attributes.ProfileContent);
            }
            
            return Array.Empty<byte>();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to download profile: {ex.Message}", ex);
            throw;
        }
    }

    public async Task DeleteProfileAsync(string id)
    {
        _logger.LogInformation($"Deleting profile: {id}");
        try
        {
            var client = await GetClientAsync();
            await client.DeleteProfileAsync(id, default);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to delete profile: {ex.Message}", ex);
            throw;
        }
    }

    public async Task<string> InstallProfileAsync(string id)
    {
        _logger.LogInformation($"Installing profile: {id}");
        try
        {
            var content = await DownloadProfileAsync(id);
            if (content.Length == 0)
            {
                throw new InvalidOperationException("Profile content is empty");
            }

            var profilesDir = GetProvisioningProfilesDirectory();
            if (!Directory.Exists(profilesDir))
            {
                Directory.CreateDirectory(profilesDir);
            }

            // Parse the profile to get UUID for filename
            var uuid = ExtractProfileUuid(content);
            var fileName = $"{uuid}.mobileprovision";
            var filePath = Path.Combine(profilesDir, fileName);

            await File.WriteAllBytesAsync(filePath, content);
            _logger.LogInformation($"Profile installed to: {filePath}");

            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to install profile: {ex.Message}", ex);
            throw;
        }
    }

    public async Task<int> InstallProfilesAsync(IEnumerable<string> ids, IProgress<string>? progress = null)
    {
        var idList = ids.ToList();
        _logger.LogInformation($"Installing {idList.Count} profiles...");
        
        var installed = 0;
        var total = idList.Count;
        
        foreach (var id in idList)
        {
            try
            {
                progress?.Report($"Installing profile {installed + 1} of {total}...");
                await InstallProfileAsync(id);
                installed++;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to install profile {id}: {ex.Message}", ex);
                // Continue with other profiles
            }
        }

        _logger.LogInformation($"Installed {installed} of {total} profiles");
        return installed;
    }

    private static string GetProvisioningProfilesDirectory()
    {
        // macOS: ~/Library/MobileDevice/Provisioning Profiles
        // Windows: Not typically used, but we can use a similar path
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "MobileDevice", "Provisioning Profiles");
        }
        
        // Fallback for other platforms
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MobileDevice", "Provisioning Profiles");
    }

    private static string ExtractProfileUuid(byte[] content)
    {
        // The UUID is in the plist content of the profile
        // Look for <key>UUID</key><string>...</string>
        var text = System.Text.Encoding.UTF8.GetString(content);
        var uuidKeyIndex = text.IndexOf("<key>UUID</key>");
        if (uuidKeyIndex >= 0)
        {
            var stringStart = text.IndexOf("<string>", uuidKeyIndex);
            var stringEnd = text.IndexOf("</string>", stringStart);
            if (stringStart >= 0 && stringEnd > stringStart)
            {
                return text.Substring(stringStart + 8, stringEnd - stringStart - 8);
            }
        }
        
        // Fallback to a generated UUID
        return Guid.NewGuid().ToString("D").ToUpperInvariant();
    }
}
