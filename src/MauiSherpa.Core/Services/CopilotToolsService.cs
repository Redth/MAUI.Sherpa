using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Provides Copilot SDK tool definitions for Apple Developer and Android SDK operations
/// </summary>
public class CopilotToolsService : ICopilotToolsService
{
    private readonly IAppleConnectService _appleService;
    private readonly IAppleIdentityStateService _identityState;
    private readonly IAppleIdentityService _identityService;
    private readonly IAndroidSdkService _androidService;
    private readonly ILoggingService _logger;
    
    private readonly List<CopilotTool> _tools = new();
    private readonly HashSet<string> _readOnlyToolNames = new();

    public CopilotToolsService(
        IAppleConnectService appleService,
        IAppleIdentityStateService identityState,
        IAppleIdentityService identityService,
        IAndroidSdkService androidService,
        ILoggingService logger)
    {
        _appleService = appleService;
        _identityState = identityState;
        _identityService = identityService;
        _androidService = androidService;
        _logger = logger;
        
        InitializeTools();
    }
    
    public IReadOnlyList<string> ReadOnlyToolNames => _readOnlyToolNames.ToList();
    
    public IReadOnlyList<AIFunction> GetTools()
    {
        return _tools.Select(t => t.Function).ToList();
    }
    
    public CopilotTool? GetTool(string name)
    {
        return _tools.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
    
    private void AddTool(AIFunction function, bool isReadOnly = false)
    {
        _tools.Add(new CopilotTool(function, isReadOnly));
        if (isReadOnly)
        {
            _readOnlyToolNames.Add(function.Name);
        }
    }
    
    private void InitializeTools()
    {
        // Apple Identity Tools
        AddTool(AIFunctionFactory.Create(ListAppleIdentitiesAsync, "list_apple_identities", 
            "List all configured Apple Developer identities (App Store Connect API keys). Shows which one is currently selected."), isReadOnly: true);
        AddTool(AIFunctionFactory.Create(GetCurrentAppleIdentity, "get_current_apple_identity", 
            "Get the currently selected Apple Developer identity."), isReadOnly: true);
        AddTool(AIFunctionFactory.Create(SelectAppleIdentityAsync, "select_apple_identity", 
            "Select an Apple Developer identity by name or ID for subsequent operations."), isReadOnly: false);

        // Bundle ID Tools
        AddTool(AIFunctionFactory.Create(ListBundleIdsAsync, "list_bundle_ids", 
            "List all Bundle IDs (App IDs) for the current Apple Developer account. Optionally filter by search query."), isReadOnly: true);
        AddTool(AIFunctionFactory.Create(CreateBundleIdAsync, "create_bundle_id", 
            "Create a new Bundle ID (App ID) in App Store Connect. Supports explicit IDs (com.company.appname) or wildcard IDs (com.company.*). Platform should be 'IOS' or 'MAC_OS'."), isReadOnly: false);
        AddTool(AIFunctionFactory.Create(GetAppIdPrefixesAsync, "get_app_id_prefixes", 
            "Get the list of App ID Prefixes (Team IDs) available for your account. These are assigned by Apple and shown on Bundle IDs."), isReadOnly: true);
        AddTool(AIFunctionFactory.Create(DeleteBundleIdAsync, "delete_bundle_id", 
            "Delete a Bundle ID from App Store Connect."), isReadOnly: false);

        // Device Tools
        AddTool(AIFunctionFactory.Create(ListDevicesAsync, "list_devices", 
            "List all registered Apple devices. Optionally filter by name or UDID."), isReadOnly: true);
        AddTool(AIFunctionFactory.Create(RegisterDeviceAsync, "register_device", 
            "Register a new device for development or ad-hoc distribution."), isReadOnly: false);
        AddTool(AIFunctionFactory.Create(EnableDeviceAsync, "enable_device", 
            "Enable a disabled device."), isReadOnly: false);
        AddTool(AIFunctionFactory.Create(DisableDeviceAsync, "disable_device", 
            "Disable a device (it will no longer be usable for development)."), isReadOnly: false);

        // Certificate Tools
        AddTool(AIFunctionFactory.Create(ListCertificatesAsync, "list_certificates", 
            "List all signing certificates. Optionally filter by name or type (e.g., 'DEVELOPMENT', 'DISTRIBUTION')."), isReadOnly: true);
        AddTool(AIFunctionFactory.Create(CreateCertificateAsync, "create_certificate", 
            "Create a new signing certificate. The PFX file will be saved to your Downloads folder."), isReadOnly: false);
        AddTool(AIFunctionFactory.Create(RevokeCertificateAsync, "revoke_certificate", 
            "Revoke a signing certificate. This action cannot be undone."), isReadOnly: false);

        // Provisioning Profile Tools
        AddTool(AIFunctionFactory.Create(ListProvisioningProfilesAsync, "list_provisioning_profiles", 
            "List all provisioning profiles. Optionally filter by name or bundle ID."), isReadOnly: true);
        AddTool(AIFunctionFactory.Create(GetProfileTypesAsync, "get_profile_types", 
            "Get all valid provisioning profile types with descriptions. Use this to understand available profile types before creating."), isReadOnly: true);
        AddTool(AIFunctionFactory.Create(CreateProvisioningProfileAsync, "create_provisioning_profile", 
            "Create a new provisioning profile. Requires profile type, bundle ID, and certificate IDs. For development/adhoc profiles, device IDs are also required."), isReadOnly: false);
        AddTool(AIFunctionFactory.Create(RegenerateProvisioningProfileAsync, "regenerate_provisioning_profile", 
            "Regenerate (update) an existing provisioning profile with new certificates and/or devices. This deletes the old profile and creates a new one with the same name and bundle ID."), isReadOnly: false);
        AddTool(AIFunctionFactory.Create(DownloadProvisioningProfileAsync, "download_provisioning_profile", 
            "Download a provisioning profile to your Downloads folder."), isReadOnly: false);
        AddTool(AIFunctionFactory.Create(InstallProvisioningProfileAsync, "install_provisioning_profile", 
            "Install a provisioning profile to the system (~/Library/MobileDevice/Provisioning Profiles)."), isReadOnly: false);
        AddTool(AIFunctionFactory.Create(InstallAllProvisioningProfilesAsync, "install_all_provisioning_profiles", 
            "Install all valid (active, non-expired) provisioning profiles to the system."), isReadOnly: false);
        AddTool(AIFunctionFactory.Create(DeleteProvisioningProfileAsync, "delete_provisioning_profile", 
            "Delete a provisioning profile from App Store Connect."), isReadOnly: false);

        // Android SDK Tools
        AddTool(AIFunctionFactory.Create(GetAndroidSdkPathAsync, "get_android_sdk_path", 
            "Get the Android SDK installation path."), isReadOnly: true);
        AddTool(AIFunctionFactory.Create(ListAndroidPackagesAsync, "list_android_packages", 
            "List Android SDK packages. Can filter by installed, available, or search query."), isReadOnly: true);
        AddTool(AIFunctionFactory.Create(InstallAndroidPackageAsync, "install_android_package", 
            "Install an Android SDK package."), isReadOnly: false);
        AddTool(AIFunctionFactory.Create(UninstallAndroidPackageAsync, "uninstall_android_package", 
            "Uninstall an Android SDK package."), isReadOnly: false);

        // Android Emulator/AVD Tools
        AddTool(AIFunctionFactory.Create(ListEmulatorsAsync, "list_emulators", 
            "List all Android emulators (AVDs). Shows running status."), isReadOnly: true);
        AddTool(AIFunctionFactory.Create(CreateEmulatorAsync, "create_emulator", 
            "Create a new Android emulator (AVD)."), isReadOnly: false);
        AddTool(AIFunctionFactory.Create(DeleteEmulatorAsync, "delete_emulator", 
            "Delete an Android emulator (AVD)."), isReadOnly: false);
        AddTool(AIFunctionFactory.Create(StartEmulatorAsync, "start_emulator", 
            "Start an Android emulator."), isReadOnly: false);
        AddTool(AIFunctionFactory.Create(StopEmulatorAsync, "stop_emulator", 
            "Stop a running Android emulator."), isReadOnly: false);

        // Android Device Tools
        AddTool(AIFunctionFactory.Create(ListAndroidDevicesAsync, "list_android_devices", 
            "List connected Android devices and running emulators."), isReadOnly: true);

        // Android System Images & Device Definitions
        AddTool(AIFunctionFactory.Create(ListSystemImagesAsync, "list_system_images", 
            "List available Android system images for creating emulators."), isReadOnly: true);
        AddTool(AIFunctionFactory.Create(ListDeviceDefinitionsAsync, "list_device_definitions", 
            "List available device definitions for creating emulators."), isReadOnly: true);
    }

    private string? CheckIdentitySelected()
    {
        if (_identityState.SelectedIdentity == null)
        {
            return "ERROR: No Apple Developer identity selected. Please select one first using the identity picker in the app, or use select_apple_identity tool.";
        }
        return null;
    }

    #region Apple Identity Tools

    [Description("List all configured Apple Developer identities")]
    private async Task<string> ListAppleIdentitiesAsync()
    {
        _logger.LogDebug("Tool: list_apple_identities called");
        var identities = await _identityService.GetIdentitiesAsync();
        if (!identities.Any())
        {
            return "No Apple Developer identities configured. Add one in Settings → Apple Identities.";
        }

        var current = _identityState.SelectedIdentity;
        var results = identities.Select(i => new
        {
            i.Id,
            i.Name,
            i.KeyId,
            IsSelected = current?.Id == i.Id
        });
        _logger.LogDebug($"Tool: list_apple_identities returning {identities.Count()} identities");
        return JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Get the currently selected Apple Developer identity")]
    private string GetCurrentAppleIdentity()
    {
        _logger.LogDebug("Tool: get_current_apple_identity called");
        var identity = _identityState.SelectedIdentity;
        if (identity == null)
        {
            _logger.LogDebug("Tool: get_current_apple_identity - no identity selected");
            return JsonSerializer.Serialize(new { Selected = false, Message = "No Apple Developer identity is currently selected." });
        }
        _logger.LogDebug($"Tool: get_current_apple_identity - returning {identity.Name}");
        return JsonSerializer.Serialize(new
        {
            Selected = true,
            identity.Id,
            identity.Name,
            identity.KeyId,
            identity.IssuerId
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Select an Apple Developer identity by name or ID")]
    private async Task<string> SelectAppleIdentityAsync(
        [Description("The name or ID of the Apple identity to select")] string identityNameOrId)
    {
        _logger.LogDebug($"Tool: select_apple_identity called with: {identityNameOrId}");
        var identities = await _identityService.GetIdentitiesAsync();
        var identity = identities.FirstOrDefault(i =>
            i.Id.Equals(identityNameOrId, StringComparison.OrdinalIgnoreCase) ||
            i.Name.Equals(identityNameOrId, StringComparison.OrdinalIgnoreCase));

        if (identity == null)
        {
            var available = string.Join(", ", identities.Select(i => i.Name));
            _logger.LogWarning($"Tool: select_apple_identity - identity not found: {identityNameOrId}");
            return $"Identity '{identityNameOrId}' not found. Available identities: {available}";
        }

        _identityState.SetSelectedIdentity(identity);
        _logger.LogInformation($"Tool: select_apple_identity - selected: {identity.Name}");
        return $"Selected Apple identity: {identity.Name}";
    }

    #endregion

    #region Bundle ID Tools

    [Description("List all Bundle IDs for the current Apple Developer account")]
    private async Task<string> ListBundleIdsAsync(
        [Description("Optional search query to filter by identifier or name")] string? query = null)
    {
        var error = CheckIdentitySelected();
        if (error != null) return error;

        var bundleIds = await _appleService.GetBundleIdsAsync();

        if (!string.IsNullOrEmpty(query))
        {
            bundleIds = bundleIds
                .Where(b => b.Identifier.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            b.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!bundleIds.Any())
        {
            return string.IsNullOrEmpty(query)
                ? "No Bundle IDs found."
                : $"No Bundle IDs matching '{query}' found.";
        }

        var results = bundleIds.Select(b => new { b.Identifier, b.Name, b.Platform });
        return JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Create a new Bundle ID in App Store Connect")]
    private async Task<string> CreateBundleIdAsync(
        [Description("The bundle identifier (e.g., 'com.company.appname' for explicit or 'com.company.*' for wildcard)")] string identifier,
        [Description("A descriptive name for the Bundle ID")] string name,
        [Description("Platform: 'IOS' for iPhone/iPad or 'MAC_OS' for Mac apps. Defaults to IOS.")] string platform = "IOS")
    {
        var error = CheckIdentitySelected();
        if (error != null) return error;

        try
        {
            // Validate identifier format
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return JsonSerializer.Serialize(new { Success = false, Message = "Bundle identifier cannot be empty" });
            }
            
            // Check for valid wildcard format
            bool isWildcard = identifier.EndsWith(".*");
            if (identifier.Contains("*") && !isWildcard)
            {
                return JsonSerializer.Serialize(new { Success = false, Message = "Wildcard bundle IDs must end with '.*' (e.g., 'com.company.*')" });
            }
            
            var result = await _appleService.CreateBundleIdAsync(identifier, name, platform.ToUpperInvariant());
            return JsonSerializer.Serialize(new
            {
                Success = true,
                Message = $"Created {(isWildcard ? "wildcard " : "")}Bundle ID: {result.Identifier}",
                result.Id,
                result.Identifier,
                result.Name,
                result.Platform,
                result.SeedId,
                IsWildcard = isWildcard
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { Success = false, Message = $"Failed to create Bundle ID: {ex.Message}" });
        }
    }

    [Description("Get the list of App ID Prefixes (Team IDs/Seed IDs) available for your account")]
    private async Task<string> GetAppIdPrefixesAsync()
    {
        var error = CheckIdentitySelected();
        if (error != null) return error;

        try
        {
            // Get all bundle IDs and extract unique seed IDs
            var bundleIds = await _appleService.GetBundleIdsAsync();
            var prefixes = bundleIds
                .Where(b => !string.IsNullOrEmpty(b.SeedId))
                .Select(b => b.SeedId!)
                .Distinct()
                .ToList();
            
            return JsonSerializer.Serialize(new
            {
                Success = true,
                Message = $"Found {prefixes.Count} App ID prefix(es)",
                Prefixes = prefixes,
                Note = "App ID Prefixes are assigned by Apple to your team. When creating a Bundle ID, Apple automatically assigns an appropriate prefix."
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { Success = false, Message = $"Failed to get App ID prefixes: {ex.Message}" });
        }
    }

    [Description("Delete a Bundle ID from App Store Connect")]
    private async Task<string> DeleteBundleIdAsync(
        [Description("The Bundle ID identifier or internal ID to delete")] string bundleIdOrIdentifier)
    {
        var error = CheckIdentitySelected();
        if (error != null) return error;

        try
        {
            var bundleIds = await _appleService.GetBundleIdsAsync();
            var bundleId = bundleIds.FirstOrDefault(b =>
                b.Id.Equals(bundleIdOrIdentifier, StringComparison.OrdinalIgnoreCase) ||
                b.Identifier.Equals(bundleIdOrIdentifier, StringComparison.OrdinalIgnoreCase));

            if (bundleId == null)
            {
                return $"Bundle ID '{bundleIdOrIdentifier}' not found.";
            }

            await _appleService.DeleteBundleIdAsync(bundleId.Id);
            return $"Deleted Bundle ID: {bundleId.Identifier}";
        }
        catch (Exception ex)
        {
            return $"Failed to delete Bundle ID: {ex.Message}";
        }
    }

    #endregion

    #region Device Tools

    [Description("List all registered Apple devices")]
    private async Task<string> ListDevicesAsync(
        [Description("Optional search query to filter by name or UDID")] string? query = null)
    {
        var error = CheckIdentitySelected();
        if (error != null) return error;

        var devices = await _appleService.GetDevicesAsync();

        if (!string.IsNullOrEmpty(query))
        {
            devices = devices
                .Where(d => d.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            d.Udid.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!devices.Any())
        {
            return string.IsNullOrEmpty(query)
                ? "No devices found."
                : $"No devices matching '{query}' found.";
        }

        var results = devices.Select(d => new { d.Name, d.Udid, d.Platform, d.DeviceClass, d.Status, d.Model });
        return JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Register a new device for development")]
    private async Task<string> RegisterDeviceAsync(
        [Description("The device UDID")] string udid,
        [Description("A name for the device")] string name,
        [Description("Platform: 'IOS' or 'MAC_OS'")] string platform)
    {
        var error = CheckIdentitySelected();
        if (error != null) return error;

        try
        {
            var result = await _appleService.RegisterDeviceAsync(udid, name, platform.ToUpperInvariant());
            return JsonSerializer.Serialize(new
            {
                Success = true,
                Message = $"Registered device: {result.Name}",
                result.Id,
                result.Name,
                result.Udid,
                result.Platform,
                result.DeviceClass
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { Success = false, Message = $"Failed to register device: {ex.Message}" });
        }
    }

    [Description("Enable a disabled device")]
    private async Task<string> EnableDeviceAsync(
        [Description("The device UDID or internal ID")] string deviceIdOrUdid)
    {
        var error = CheckIdentitySelected();
        if (error != null) return error;

        try
        {
            var devices = await _appleService.GetDevicesAsync();
            var device = devices.FirstOrDefault(d =>
                d.Id.Equals(deviceIdOrUdid, StringComparison.OrdinalIgnoreCase) ||
                d.Udid.Equals(deviceIdOrUdid, StringComparison.OrdinalIgnoreCase));

            if (device == null)
            {
                return $"Device '{deviceIdOrUdid}' not found.";
            }

            await _appleService.UpdateDeviceStatusAsync(device.Id, enabled: true);
            return $"Enabled device: {device.Name}";
        }
        catch (Exception ex)
        {
            return $"Failed to enable device: {ex.Message}";
        }
    }

    [Description("Disable a device")]
    private async Task<string> DisableDeviceAsync(
        [Description("The device UDID or internal ID")] string deviceIdOrUdid)
    {
        var error = CheckIdentitySelected();
        if (error != null) return error;

        try
        {
            var devices = await _appleService.GetDevicesAsync();
            var device = devices.FirstOrDefault(d =>
                d.Id.Equals(deviceIdOrUdid, StringComparison.OrdinalIgnoreCase) ||
                d.Udid.Equals(deviceIdOrUdid, StringComparison.OrdinalIgnoreCase));

            if (device == null)
            {
                return $"Device '{deviceIdOrUdid}' not found.";
            }

            await _appleService.UpdateDeviceStatusAsync(device.Id, enabled: false);
            return $"Disabled device: {device.Name}";
        }
        catch (Exception ex)
        {
            return $"Failed to disable device: {ex.Message}";
        }
    }

    #endregion

    #region Certificate Tools

    [Description("List all signing certificates")]
    private async Task<string> ListCertificatesAsync(
        [Description("Optional search query to filter by name or type")] string? query = null)
    {
        var error = CheckIdentitySelected();
        if (error != null) return error;

        var certs = await _appleService.GetCertificatesAsync();

        if (!string.IsNullOrEmpty(query))
        {
            certs = certs
                .Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            c.CertificateType.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!certs.Any())
        {
            return string.IsNullOrEmpty(query)
                ? "No certificates found."
                : $"No certificates matching '{query}' found.";
        }

        var results = certs.Select(c => new
        {
            c.Name,
            Type = c.CertificateType,
            c.Platform,
            c.SerialNumber,
            ExpirationDate = c.ExpirationDate.ToString("yyyy-MM-dd"),
            IsExpired = c.ExpirationDate < DateTime.UtcNow
        });
        return JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Create a new signing certificate")]
    private async Task<string> CreateCertificateAsync(
        [Description("Certificate type: IOS_DEVELOPMENT, IOS_DISTRIBUTION, MAC_APP_DEVELOPMENT, MAC_APP_DISTRIBUTION, DEVELOPER_ID_APPLICATION")] string certificateType,
        [Description("Optional common name for the certificate")] string? commonName = null)
    {
        var error = CheckIdentitySelected();
        if (error != null) return error;

        try
        {
            var result = await _appleService.CreateCertificateAsync(certificateType.ToUpperInvariant(), commonName);
            return JsonSerializer.Serialize(new
            {
                Success = true,
                Message = $"Created certificate. The PFX file has been saved. Certificate ID: {result.CertificateId}",
                CertificateId = result.CertificateId,
                ExpirationDate = result.ExpirationDate.ToString("yyyy-MM-dd")
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { Success = false, Message = $"Failed to create certificate: {ex.Message}" });
        }
    }

    [Description("Revoke a signing certificate")]
    private async Task<string> RevokeCertificateAsync(
        [Description("The certificate ID or serial number")] string certificateIdOrSerial)
    {
        var error = CheckIdentitySelected();
        if (error != null) return error;

        try
        {
            var certs = await _appleService.GetCertificatesAsync();
            var cert = certs.FirstOrDefault(c =>
                c.Id.Equals(certificateIdOrSerial, StringComparison.OrdinalIgnoreCase) ||
                c.SerialNumber.Equals(certificateIdOrSerial, StringComparison.OrdinalIgnoreCase));

            if (cert == null)
            {
                return $"Certificate '{certificateIdOrSerial}' not found.";
            }

            await _appleService.RevokeCertificateAsync(cert.Id);
            return $"Revoked certificate: {cert.Name} (Serial: {cert.SerialNumber})";
        }
        catch (Exception ex)
        {
            return $"Failed to revoke certificate: {ex.Message}";
        }
    }

    #endregion

    #region Provisioning Profile Tools

    [Description("List all provisioning profiles")]
    private async Task<string> ListProvisioningProfilesAsync(
        [Description("Optional search query to filter by name or bundle ID")] string? query = null)
    {
        var error = CheckIdentitySelected();
        if (error != null) return error;

        var profiles = await _appleService.GetProfilesAsync();

        if (!string.IsNullOrEmpty(query))
        {
            profiles = profiles
                .Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            (p.BundleId?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        if (!profiles.Any())
        {
            return string.IsNullOrEmpty(query)
                ? "No provisioning profiles found."
                : $"No provisioning profiles matching '{query}' found.";
        }

        var results = profiles.Select(p => new
        {
            p.Name,
            Type = p.ProfileType,
            p.Platform,
            p.State,
            p.BundleId,
            p.Uuid,
            ExpirationDate = p.ExpirationDate.ToString("yyyy-MM-dd"),
            IsExpired = p.ExpirationDate < DateTime.UtcNow
        });
        return JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Download a provisioning profile to Downloads folder")]
    private async Task<string> DownloadProvisioningProfileAsync(
        [Description("The profile name or UUID")] string profileNameOrUuid)
    {
        var error = CheckIdentitySelected();
        if (error != null) return error;

        try
        {
            var profiles = await _appleService.GetProfilesAsync();
            var profile = profiles.FirstOrDefault(p =>
                p.Id.Equals(profileNameOrUuid, StringComparison.OrdinalIgnoreCase) ||
                p.Uuid.Equals(profileNameOrUuid, StringComparison.OrdinalIgnoreCase) ||
                p.Name.Equals(profileNameOrUuid, StringComparison.OrdinalIgnoreCase));

            if (profile == null)
            {
                return $"Profile '{profileNameOrUuid}' not found.";
            }

            var data = await _appleService.DownloadProfileAsync(profile.Id);
            var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var fileName = $"{profile.Name.Replace(" ", "_")}.mobileprovision";
            var filePath = Path.Combine(downloadsPath, fileName);
            await File.WriteAllBytesAsync(filePath, data);

            return JsonSerializer.Serialize(new
            {
                Success = true,
                Message = $"Downloaded profile to: {filePath}",
                FilePath = filePath,
                ProfileName = profile.Name
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { Success = false, Message = $"Failed to download profile: {ex.Message}" });
        }
    }

    [Description("Install a provisioning profile to the system")]
    private async Task<string> InstallProvisioningProfileAsync(
        [Description("The profile name or UUID")] string profileNameOrUuid)
    {
        var error = CheckIdentitySelected();
        if (error != null) return error;

        try
        {
            var profiles = await _appleService.GetProfilesAsync();
            var profile = profiles.FirstOrDefault(p =>
                p.Id.Equals(profileNameOrUuid, StringComparison.OrdinalIgnoreCase) ||
                p.Uuid.Equals(profileNameOrUuid, StringComparison.OrdinalIgnoreCase) ||
                p.Name.Equals(profileNameOrUuid, StringComparison.OrdinalIgnoreCase));

            if (profile == null)
            {
                return $"Profile '{profileNameOrUuid}' not found.";
            }

            var installedPath = await _appleService.InstallProfileAsync(profile.Id);
            return JsonSerializer.Serialize(new
            {
                Success = true,
                Message = $"Installed profile: {profile.Name}",
                InstalledPath = installedPath,
                ProfileName = profile.Name
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { Success = false, Message = $"Failed to install profile: {ex.Message}" });
        }
    }

    [Description("Install all valid provisioning profiles to the system")]
    private async Task<string> InstallAllProvisioningProfilesAsync()
    {
        var error = CheckIdentitySelected();
        if (error != null) return error;

        try
        {
            var profiles = await _appleService.GetProfilesAsync();
            var validProfiles = profiles
                .Where(p => p.State == "ACTIVE" && p.ExpirationDate > DateTime.UtcNow)
                .ToList();

            if (!validProfiles.Any())
            {
                return "No valid (active, non-expired) profiles to install.";
            }

            var count = await _appleService.InstallProfilesAsync(validProfiles.Select(p => p.Id));
            return JsonSerializer.Serialize(new
            {
                Success = true,
                Message = $"Installed {count} provisioning profiles.",
                InstalledCount = count
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { Success = false, Message = $"Failed to install profiles: {ex.Message}" });
        }
    }

    [Description("Delete a provisioning profile from App Store Connect")]
    private async Task<string> DeleteProvisioningProfileAsync(
        [Description("The profile name or UUID")] string profileNameOrUuid)
    {
        var error = CheckIdentitySelected();
        if (error != null) return error;

        try
        {
            var profiles = await _appleService.GetProfilesAsync();
            var profile = profiles.FirstOrDefault(p =>
                p.Id.Equals(profileNameOrUuid, StringComparison.OrdinalIgnoreCase) ||
                p.Uuid.Equals(profileNameOrUuid, StringComparison.OrdinalIgnoreCase) ||
                p.Name.Equals(profileNameOrUuid, StringComparison.OrdinalIgnoreCase));

            if (profile == null)
            {
                return $"Profile '{profileNameOrUuid}' not found.";
            }

            await _appleService.DeleteProfileAsync(profile.Id);
            return $"Deleted profile: {profile.Name}";
        }
        catch (Exception ex)
        {
            return $"Failed to delete profile: {ex.Message}";
        }
    }

    [Description("Get all valid provisioning profile types")]
    private Task<string> GetProfileTypesAsync()
    {
        _logger.LogDebug("Tool: get_profile_types called");
        
        var profileTypes = new[]
        {
            new { Type = "IOS_APP_DEVELOPMENT", Platform = "iOS", Name = "Development", Description = "Run app on registered devices during development", RequiresDevices = true },
            new { Type = "IOS_APP_ADHOC", Platform = "iOS", Name = "Ad Hoc", Description = "Distribute to a limited number of registered devices", RequiresDevices = true },
            new { Type = "IOS_APP_STORE", Platform = "iOS", Name = "App Store", Description = "Submit to the App Store or TestFlight", RequiresDevices = false },
            new { Type = "MAC_APP_DEVELOPMENT", Platform = "macOS", Name = "Development", Description = "Run app on registered Macs during development", RequiresDevices = true },
            new { Type = "MAC_APP_STORE", Platform = "macOS", Name = "Mac App Store", Description = "Submit to the Mac App Store", RequiresDevices = false },
            new { Type = "MAC_APP_DIRECT", Platform = "macOS", Name = "Direct Distribution", Description = "Distribute outside the Mac App Store (notarized)", RequiresDevices = false },
            new { Type = "MAC_CATALYST_APP_DEVELOPMENT", Platform = "Mac Catalyst", Name = "Development", Description = "Run iPad app on Mac during development", RequiresDevices = true },
            new { Type = "MAC_CATALYST_APP_STORE", Platform = "Mac Catalyst", Name = "Mac App Store", Description = "Submit iPad app to Mac App Store", RequiresDevices = false },
            new { Type = "MAC_CATALYST_APP_DIRECT", Platform = "Mac Catalyst", Name = "Direct Distribution", Description = "Distribute iPad app outside Mac App Store", RequiresDevices = false }
        };

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            ProfileTypes = profileTypes,
            Notes = new[]
            {
                "Development profiles require development certificates",
                "Distribution profiles (Ad Hoc, App Store, Direct) require distribution certificates",
                "Profiles with RequiresDevices=true must include at least one device ID"
            }
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    [Description("Create a new provisioning profile")]
    private async Task<string> CreateProvisioningProfileAsync(
        [Description("Profile name (e.g., 'My App Development')")] string name,
        [Description("Profile type (e.g., 'IOS_APP_DEVELOPMENT', 'IOS_APP_STORE'). Use get_profile_types to see all options.")] string profileType,
        [Description("Bundle ID identifier (e.g., 'com.company.myapp') or App Store Connect Bundle ID resource ID")] string bundleId,
        [Description("Comma-separated certificate IDs or names to include")] string certificates,
        [Description("Comma-separated device IDs or UDIDs to include (required for development/adhoc profiles, omit for App Store profiles)")] string? devices = null)
    {
        var error = CheckIdentitySelected();
        if (error != null) return error;

        try
        {
            _logger.LogDebug($"Tool: create_provisioning_profile called - name={name}, type={profileType}, bundleId={bundleId}");

            // Validate profile type
            var validTypes = new[] { "IOS_APP_DEVELOPMENT", "IOS_APP_ADHOC", "IOS_APP_STORE", "IOS_APP_INHOUSE",
                "MAC_APP_DEVELOPMENT", "MAC_APP_STORE", "MAC_APP_DIRECT",
                "MAC_CATALYST_APP_DEVELOPMENT", "MAC_CATALYST_APP_STORE", "MAC_CATALYST_APP_DIRECT" };
            
            if (!validTypes.Contains(profileType.ToUpperInvariant()))
            {
                return JsonSerializer.Serialize(new { Success = false, Message = $"Invalid profile type '{profileType}'. Use get_profile_types to see valid options." });
            }

            var requiresDevices = profileType.Contains("DEVELOPMENT") || profileType.Contains("ADHOC");

            // Resolve bundle ID
            var bundleIds = await _appleService.GetBundleIdsAsync();
            var matchedBundle = bundleIds.FirstOrDefault(b =>
                b.Id.Equals(bundleId, StringComparison.OrdinalIgnoreCase) ||
                b.Identifier.Equals(bundleId, StringComparison.OrdinalIgnoreCase) ||
                b.Name.Equals(bundleId, StringComparison.OrdinalIgnoreCase));

            if (matchedBundle == null)
            {
                return JsonSerializer.Serialize(new { 
                    Success = false, 
                    Message = $"Bundle ID '{bundleId}' not found. Use list_bundle_ids to see available bundle IDs." 
                });
            }

            // Resolve certificates
            var allCerts = await _appleService.GetCertificatesAsync();
            var certInputs = certificates.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var resolvedCertIds = new List<string>();
            
            foreach (var certInput in certInputs)
            {
                var cert = allCerts.FirstOrDefault(c =>
                    c.Id.Equals(certInput, StringComparison.OrdinalIgnoreCase) ||
                    c.Name.Contains(certInput, StringComparison.OrdinalIgnoreCase) ||
                    c.SerialNumber.Equals(certInput, StringComparison.OrdinalIgnoreCase));
                
                if (cert == null)
                {
                    return JsonSerializer.Serialize(new { 
                        Success = false, 
                        Message = $"Certificate '{certInput}' not found. Use list_certificates to see available certificates." 
                    });
                }
                resolvedCertIds.Add(cert.Id);
            }

            if (!resolvedCertIds.Any())
            {
                return JsonSerializer.Serialize(new { Success = false, Message = "At least one certificate is required." });
            }

            // Resolve devices (if required)
            List<string>? resolvedDeviceIds = null;
            if (requiresDevices)
            {
                if (string.IsNullOrWhiteSpace(devices))
                {
                    return JsonSerializer.Serialize(new { 
                        Success = false, 
                        Message = $"Profile type '{profileType}' requires devices. Provide device IDs or UDIDs." 
                    });
                }

                var allDevices = await _appleService.GetDevicesAsync();
                var deviceInputs = devices.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                resolvedDeviceIds = new List<string>();

                foreach (var deviceInput in deviceInputs)
                {
                    // Handle special "all" keyword
                    if (deviceInput.Equals("all", StringComparison.OrdinalIgnoreCase))
                    {
                        resolvedDeviceIds = allDevices.Where(d => d.Status == "ENABLED").Select(d => d.Id).ToList();
                        break;
                    }

                    var device = allDevices.FirstOrDefault(d =>
                        d.Id.Equals(deviceInput, StringComparison.OrdinalIgnoreCase) ||
                        d.Udid.Equals(deviceInput, StringComparison.OrdinalIgnoreCase) ||
                        d.Name.Contains(deviceInput, StringComparison.OrdinalIgnoreCase));

                    if (device == null)
                    {
                        return JsonSerializer.Serialize(new { 
                            Success = false, 
                            Message = $"Device '{deviceInput}' not found. Use list_devices to see available devices, or use 'all' to include all enabled devices." 
                        });
                    }
                    resolvedDeviceIds.Add(device.Id);
                }

                if (!resolvedDeviceIds.Any())
                {
                    return JsonSerializer.Serialize(new { Success = false, Message = "At least one device is required for this profile type." });
                }
            }

            // Create the profile
            var request = new AppleProfileCreateRequest(
                name,
                profileType.ToUpperInvariant(),
                matchedBundle.Id,
                resolvedCertIds,
                resolvedDeviceIds);

            var profile = await _appleService.CreateProfileAsync(request);

            return JsonSerializer.Serialize(new
            {
                Success = true,
                Message = $"Created profile: {profile.Name}",
                Profile = new
                {
                    profile.Id,
                    profile.Name,
                    Type = profile.ProfileType,
                    profile.Platform,
                    profile.State,
                    profile.Uuid,
                    ExpirationDate = profile.ExpirationDate.ToString("yyyy-MM-dd"),
                    BundleId = matchedBundle.Identifier,
                    CertificateCount = resolvedCertIds.Count,
                    DeviceCount = resolvedDeviceIds?.Count ?? 0
                }
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Tool: create_provisioning_profile failed: {ex.Message}", ex);
            return JsonSerializer.Serialize(new { Success = false, Message = $"Failed to create profile: {ex.Message}" });
        }
    }

    [Description("Regenerate (update) an existing provisioning profile")]
    private async Task<string> RegenerateProvisioningProfileAsync(
        [Description("The profile name or UUID to regenerate")] string profileNameOrUuid,
        [Description("Comma-separated certificate IDs or names (optional, uses existing if not specified)")] string? certificates = null,
        [Description("Comma-separated device IDs or UDIDs (optional for dev/adhoc, use 'all' for all enabled devices)")] string? devices = null)
    {
        var error = CheckIdentitySelected();
        if (error != null) return error;

        try
        {
            _logger.LogDebug($"Tool: regenerate_provisioning_profile called - profile={profileNameOrUuid}");

            // Find the existing profile
            var profiles = await _appleService.GetProfilesAsync();
            var existingProfile = profiles.FirstOrDefault(p =>
                p.Id.Equals(profileNameOrUuid, StringComparison.OrdinalIgnoreCase) ||
                p.Uuid.Equals(profileNameOrUuid, StringComparison.OrdinalIgnoreCase) ||
                p.Name.Equals(profileNameOrUuid, StringComparison.OrdinalIgnoreCase));

            if (existingProfile == null)
            {
                return JsonSerializer.Serialize(new { 
                    Success = false, 
                    Message = $"Profile '{profileNameOrUuid}' not found." 
                });
            }

            var profileType = existingProfile.ProfileType;
            var requiresDevices = profileType.Contains("DEVELOPMENT") || profileType.Contains("ADHOC");

            // Resolve bundle ID from existing profile
            var bundleIds = await _appleService.GetBundleIdsAsync();
            var matchedBundle = bundleIds.FirstOrDefault(b =>
                (existingProfile.BundleId != null && b.Identifier.Equals(existingProfile.BundleId, StringComparison.OrdinalIgnoreCase)));

            if (matchedBundle == null)
            {
                // Try to find by matching profile name pattern
                var possibleBundleName = existingProfile.Name.Replace(" Development", "").Replace(" Ad Hoc", "").Replace(" App Store", "").Trim();
                matchedBundle = bundleIds.FirstOrDefault(b => b.Name.Equals(possibleBundleName, StringComparison.OrdinalIgnoreCase));
            }

            if (matchedBundle == null)
            {
                return JsonSerializer.Serialize(new { 
                    Success = false, 
                    Message = $"Could not determine bundle ID for profile. Please specify it using create_provisioning_profile instead." 
                });
            }

            // Resolve certificates
            var allCerts = await _appleService.GetCertificatesAsync();
            List<string> resolvedCertIds;

            if (!string.IsNullOrWhiteSpace(certificates))
            {
                var certInputs = certificates.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                resolvedCertIds = new List<string>();
                
                foreach (var certInput in certInputs)
                {
                    var cert = allCerts.FirstOrDefault(c =>
                        c.Id.Equals(certInput, StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains(certInput, StringComparison.OrdinalIgnoreCase) ||
                        c.SerialNumber.Equals(certInput, StringComparison.OrdinalIgnoreCase));
                    
                    if (cert == null)
                    {
                        return JsonSerializer.Serialize(new { 
                            Success = false, 
                            Message = $"Certificate '{certInput}' not found." 
                        });
                    }
                    resolvedCertIds.Add(cert.Id);
                }
            }
            else
            {
                // Use all compatible certificates
                var isDev = profileType.Contains("DEVELOPMENT");
                resolvedCertIds = allCerts
                    .Where(c => isDev ? c.CertificateType.Contains("DEVELOPMENT") : 
                                       (c.CertificateType.Contains("DISTRIBUTION") || c.CertificateType.Contains("STORE")))
                    .Where(c => c.ExpirationDate > DateTime.UtcNow)
                    .Select(c => c.Id)
                    .ToList();

                if (!resolvedCertIds.Any())
                {
                    return JsonSerializer.Serialize(new { 
                        Success = false, 
                        Message = $"No valid {(isDev ? "development" : "distribution")} certificates found." 
                    });
                }
            }

            // Resolve devices
            List<string>? resolvedDeviceIds = null;
            if (requiresDevices)
            {
                var allDevices = await _appleService.GetDevicesAsync();

                if (!string.IsNullOrWhiteSpace(devices))
                {
                    if (devices.Equals("all", StringComparison.OrdinalIgnoreCase))
                    {
                        resolvedDeviceIds = allDevices.Where(d => d.Status == "ENABLED").Select(d => d.Id).ToList();
                    }
                    else
                    {
                        var deviceInputs = devices.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        resolvedDeviceIds = new List<string>();

                        foreach (var deviceInput in deviceInputs)
                        {
                            var device = allDevices.FirstOrDefault(d =>
                                d.Id.Equals(deviceInput, StringComparison.OrdinalIgnoreCase) ||
                                d.Udid.Equals(deviceInput, StringComparison.OrdinalIgnoreCase) ||
                                d.Name.Contains(deviceInput, StringComparison.OrdinalIgnoreCase));

                            if (device == null)
                            {
                                return JsonSerializer.Serialize(new { 
                                    Success = false, 
                                    Message = $"Device '{deviceInput}' not found." 
                                });
                            }
                            resolvedDeviceIds.Add(device.Id);
                        }
                    }
                }
                else
                {
                    // Use all enabled devices
                    resolvedDeviceIds = allDevices.Where(d => d.Status == "ENABLED").Select(d => d.Id).ToList();
                }

                if (!resolvedDeviceIds.Any())
                {
                    return JsonSerializer.Serialize(new { 
                        Success = false, 
                        Message = "At least one device is required for this profile type." 
                    });
                }
            }

            // Delete the old profile
            await _appleService.DeleteProfileAsync(existingProfile.Id);
            _logger.LogInformation($"Deleted old profile: {existingProfile.Name}");

            // Create the new profile with the same name
            var request = new AppleProfileCreateRequest(
                existingProfile.Name,
                profileType,
                matchedBundle.Id,
                resolvedCertIds,
                resolvedDeviceIds);

            var newProfile = await _appleService.CreateProfileAsync(request);

            return JsonSerializer.Serialize(new
            {
                Success = true,
                Message = $"Regenerated profile: {newProfile.Name}",
                Profile = new
                {
                    newProfile.Id,
                    newProfile.Name,
                    Type = newProfile.ProfileType,
                    newProfile.Platform,
                    newProfile.State,
                    newProfile.Uuid,
                    ExpirationDate = newProfile.ExpirationDate.ToString("yyyy-MM-dd"),
                    BundleId = matchedBundle.Identifier,
                    CertificateCount = resolvedCertIds.Count,
                    DeviceCount = resolvedDeviceIds?.Count ?? 0
                },
                Note = "The profile UUID has changed. You may need to update your Xcode project settings."
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Tool: regenerate_provisioning_profile failed: {ex.Message}", ex);
            return JsonSerializer.Serialize(new { Success = false, Message = $"Failed to regenerate profile: {ex.Message}" });
        }
    }

    #endregion

    #region Android SDK Tools

    private string? CheckAndroidSdkInstalled()
    {
        if (!_androidService.IsSdkInstalled)
        {
            return "ERROR: Android SDK is not installed or not detected. Please configure the Android SDK path in Settings.";
        }
        return null;
    }

    [Description("Get the Android SDK installation path")]
    private async Task<string> GetAndroidSdkPathAsync()
    {
        _logger.LogDebug("Tool: get_android_sdk_path called");
        
        // Try to detect SDK if not already done
        if (!_androidService.IsSdkInstalled)
        {
            _logger.LogDebug("Tool: get_android_sdk_path - SDK not detected, attempting detection...");
            await _androidService.DetectSdkAsync();
        }
        
        if (!_androidService.IsSdkInstalled)
        {
            _logger.LogWarning("Tool: get_android_sdk_path - SDK not found");
            return JsonSerializer.Serialize(new { Installed = false, Message = "Android SDK is not installed or not detected. Check ANDROID_HOME environment variable or install Android SDK." });
        }
        
        _logger.LogDebug($"Tool: get_android_sdk_path - found at {_androidService.SdkPath}");
        return JsonSerializer.Serialize(new
        {
            Installed = true,
            Path = _androidService.SdkPath
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("List Android SDK packages")]
    private async Task<string> ListAndroidPackagesAsync(
        [Description("Filter: 'installed', 'available', or 'all' (default)")] string? filter = null,
        [Description("Optional search query to filter by package path or description")] string? query = null)
    {
        var error = CheckAndroidSdkInstalled();
        if (error != null) return error;

        var installed = await _androidService.GetInstalledPackagesAsync();
        var available = await _androidService.GetAvailablePackagesAsync();

        IEnumerable<SdkPackageInfo> packages = filter?.ToLowerInvariant() switch
        {
            "installed" => installed,
            "available" => available.Where(a => !installed.Any(i => i.Path == a.Path)),
            _ => installed.Concat(available.Where(a => !installed.Any(i => i.Path == a.Path)))
        };

        if (!string.IsNullOrEmpty(query))
        {
            packages = packages.Where(p =>
                p.Path.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.Description.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var results = packages.Select(p => new
        {
            p.Path,
            p.Description,
            p.Version,
            p.IsInstalled
        }).Take(50); // Limit results

        if (!results.Any())
        {
            return "No packages found matching the criteria.";
        }

        return JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Install an Android SDK package")]
    private async Task<string> InstallAndroidPackageAsync(
        [Description("The package path to install (e.g., 'platforms;android-34', 'build-tools;34.0.0')")] string packagePath)
    {
        var error = CheckAndroidSdkInstalled();
        if (error != null) return error;

        try
        {
            var success = await _androidService.InstallPackageAsync(packagePath);
            if (success)
            {
                return JsonSerializer.Serialize(new
                {
                    Success = true,
                    Message = $"Successfully installed package: {packagePath}"
                });
            }
            return JsonSerializer.Serialize(new { Success = false, Message = $"Failed to install package: {packagePath}" });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { Success = false, Message = $"Failed to install package: {ex.Message}" });
        }
    }

    [Description("Uninstall an Android SDK package")]
    private async Task<string> UninstallAndroidPackageAsync(
        [Description("The package path to uninstall")] string packagePath)
    {
        var error = CheckAndroidSdkInstalled();
        if (error != null) return error;

        try
        {
            var success = await _androidService.UninstallPackageAsync(packagePath);
            if (success)
            {
                return $"Successfully uninstalled package: {packagePath}";
            }
            return $"Failed to uninstall package: {packagePath}";
        }
        catch (Exception ex)
        {
            return $"Failed to uninstall package: {ex.Message}";
        }
    }

    #endregion

    #region Android Emulator/AVD Tools

    [Description("List all Android emulators (AVDs)")]
    private async Task<string> ListEmulatorsAsync(
        [Description("Optional search query to filter by name")] string? query = null)
    {
        var error = CheckAndroidSdkInstalled();
        if (error != null) return error;

        var avds = await _androidService.GetAvdsAsync();
        var devices = await _androidService.GetDevicesAsync();
        var runningEmulators = devices.Where(d => d.IsEmulator).ToList();

        if (!string.IsNullOrEmpty(query))
        {
            avds = avds.Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!avds.Any())
        {
            return string.IsNullOrEmpty(query)
                ? "No emulators (AVDs) found."
                : $"No emulators matching '{query}' found.";
        }

        var results = avds.Select(a => new
        {
            a.Name,
            a.Device,
            a.Target,
            a.BasedOn,
            IsRunning = runningEmulators.Any(r => r.Serial.Contains(a.Name, StringComparison.OrdinalIgnoreCase)),
            RunningSerial = runningEmulators.FirstOrDefault(r => r.Serial.Contains(a.Name, StringComparison.OrdinalIgnoreCase))?.Serial
        });

        return JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Create a new Android emulator (AVD)")]
    private async Task<string> CreateEmulatorAsync(
        [Description("Name for the new emulator")] string name,
        [Description("System image path (e.g., 'system-images;android-34;google_apis;arm64-v8a')")] string systemImage,
        [Description("Optional device definition ID (e.g., 'pixel_7')")] string? device = null,
        [Description("Optional RAM size in MB")] int? ramSizeMb = null,
        [Description("Optional internal storage size in MB")] int? internalStorageMb = null)
    {
        var error = CheckAndroidSdkInstalled();
        if (error != null) return error;

        try
        {
            var options = new EmulatorCreateOptions(
                Device: device,
                RamSizeMb: ramSizeMb,
                InternalStorageMb: internalStorageMb
            );

            var success = await _androidService.CreateAvdAsync(name, systemImage, options);
            if (success)
            {
                return JsonSerializer.Serialize(new
                {
                    Success = true,
                    Message = $"Successfully created emulator: {name}",
                    Name = name,
                    SystemImage = systemImage
                }, new JsonSerializerOptions { WriteIndented = true });
            }
            return JsonSerializer.Serialize(new { Success = false, Message = $"Failed to create emulator: {name}" });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { Success = false, Message = $"Failed to create emulator: {ex.Message}" });
        }
    }

    [Description("Delete an Android emulator (AVD)")]
    private async Task<string> DeleteEmulatorAsync(
        [Description("Name of the emulator to delete")] string name)
    {
        var error = CheckAndroidSdkInstalled();
        if (error != null) return error;

        try
        {
            var success = await _androidService.DeleteAvdAsync(name);
            if (success)
            {
                return $"Successfully deleted emulator: {name}";
            }
            return $"Failed to delete emulator: {name}";
        }
        catch (Exception ex)
        {
            return $"Failed to delete emulator: {ex.Message}";
        }
    }

    [Description("Start an Android emulator")]
    private async Task<string> StartEmulatorAsync(
        [Description("Name of the emulator to start")] string name,
        [Description("Whether to perform a cold boot (default: false)")] bool coldBoot = false)
    {
        var error = CheckAndroidSdkInstalled();
        if (error != null) return error;

        try
        {
            var success = await _androidService.StartEmulatorAsync(name, coldBoot);
            if (success)
            {
                return JsonSerializer.Serialize(new
                {
                    Success = true,
                    Message = $"Started emulator: {name}" + (coldBoot ? " (cold boot)" : "")
                });
            }
            return JsonSerializer.Serialize(new { Success = false, Message = $"Failed to start emulator: {name}" });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { Success = false, Message = $"Failed to start emulator: {ex.Message}" });
        }
    }

    [Description("Stop a running Android emulator")]
    private async Task<string> StopEmulatorAsync(
        [Description("Name or serial of the emulator to stop")] string nameOrSerial)
    {
        var error = CheckAndroidSdkInstalled();
        if (error != null) return error;

        try
        {
            // Try to find the emulator by name first
            var devices = await _androidService.GetDevicesAsync();
            var emulator = devices.FirstOrDefault(d =>
                d.IsEmulator &&
                (d.Serial.Equals(nameOrSerial, StringComparison.OrdinalIgnoreCase) ||
                 d.Serial.Contains(nameOrSerial, StringComparison.OrdinalIgnoreCase)));

            if (emulator == null)
            {
                // Try to find by AVD name
                var avds = await _androidService.GetAvdsAsync();
                var avd = avds.FirstOrDefault(a => a.Name.Equals(nameOrSerial, StringComparison.OrdinalIgnoreCase));
                if (avd != null)
                {
                    emulator = devices.FirstOrDefault(d => d.IsEmulator && d.Serial.Contains(avd.Name, StringComparison.OrdinalIgnoreCase));
                }
            }

            if (emulator == null)
            {
                return $"No running emulator found matching '{nameOrSerial}'.";
            }

            var success = await _androidService.StopEmulatorAsync(emulator.Serial);
            if (success)
            {
                return $"Stopped emulator: {emulator.Serial}";
            }
            return $"Failed to stop emulator: {emulator.Serial}";
        }
        catch (Exception ex)
        {
            return $"Failed to stop emulator: {ex.Message}";
        }
    }

    #endregion

    #region Android Device Tools

    [Description("List connected Android devices and running emulators")]
    private async Task<string> ListAndroidDevicesAsync()
    {
        var error = CheckAndroidSdkInstalled();
        if (error != null) return error;

        var devices = await _androidService.GetDevicesAsync();

        if (!devices.Any())
        {
            return "No Android devices or emulators connected.";
        }

        var results = devices.Select(d => new
        {
            d.Serial,
            d.State,
            d.Model,
            d.IsEmulator,
            Type = d.IsEmulator ? "Emulator" : "Physical Device"
        });

        return JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
    }

    #endregion

    #region Android System Images & Device Definitions

    [Description("List available Android system images for creating emulators")]
    private async Task<string> ListSystemImagesAsync(
        [Description("Optional search query to filter (e.g., 'android-34', 'google_apis', 'arm64')")] string? query = null)
    {
        var error = CheckAndroidSdkInstalled();
        if (error != null) return error;

        var images = await _androidService.GetSystemImagesAsync();

        if (!string.IsNullOrEmpty(query))
        {
            images = images.Where(i => i.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!images.Any())
        {
            return string.IsNullOrEmpty(query)
                ? "No system images found. Install them using: install_android_package with a system-images package path."
                : $"No system images matching '{query}' found.";
        }

        // Parse and structure the system images
        var results = images.Select(i =>
        {
            var parts = i.Split(';');
            return new
            {
                Path = i,
                ApiLevel = parts.Length > 1 ? parts[1] : null,
                Variant = parts.Length > 2 ? parts[2] : null,
                Architecture = parts.Length > 3 ? parts[3] : null
            };
        });

        return JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("List available device definitions for creating emulators")]
    private async Task<string> ListDeviceDefinitionsAsync(
        [Description("Optional search query to filter by name or manufacturer")] string? query = null)
    {
        var error = CheckAndroidSdkInstalled();
        if (error != null) return error;

        var definitions = await _androidService.GetAvdDeviceDefinitionsAsync();

        if (!string.IsNullOrEmpty(query))
        {
            definitions = definitions.Where(d =>
                d.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (d.Oem?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                d.Id.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!definitions.Any())
        {
            return string.IsNullOrEmpty(query)
                ? "No device definitions found."
                : $"No device definitions matching '{query}' found.";
        }

        var results = definitions.Select(d => new
        {
            d.Id,
            d.Name,
            Manufacturer = d.Oem
        }).Take(30); // Limit results

        return JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
    }

    #endregion
}
