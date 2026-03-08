using System.CommandLine;
using System.Text.Json;
using System.Text.RegularExpressions;
using AppleAppStoreConnect;
using MauiSherpa.Cli.Helpers;

namespace MauiSherpa.Cli.Commands.Apple;

public static class ProfilesCommand
{
    private static readonly string[] ValidProfileTypes =
    [
        "IOS_APP_DEVELOPMENT", "IOS_APP_STORE", "IOS_APP_ADHOC", "IOS_APP_INHOUSE",
        "MAC_APP_DEVELOPMENT", "MAC_APP_STORE", "MAC_APP_DIRECT",
        "MAC_CATALYST_APP_DEVELOPMENT", "MAC_CATALYST_APP_STORE", "MAC_CATALYST_APP_DIRECT",
    ];

    private static string GetProfilesDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "MobileDevice", "Provisioning Profiles");

    public static Command Create()
    {
        var cmd = new Command("profiles", "Manage provisioning profiles — local and App Store Connect.\n\nLocal commands: list, show, install, remove\nApp Store Connect: create, asc-list");
        cmd.Add(CreateListCommand());
        cmd.Add(CreateShowCommand());
        cmd.Add(CreateInstallCommand());
        cmd.Add(CreateRemoveCommand());
        cmd.Add(CreateCreateCommand());
        cmd.Add(CreateAscListCommand());
        return cmd;
    }

    private static Command CreateListCommand()
    {
        var cmd = new Command("list", "List all locally installed provisioning profiles.");
        var expiredOpt = new Option<bool>("--include-expired") { Description = "Include expired profiles" };
        cmd.Add(expiredOpt);
        cmd.SetAction(async (parseResult, ct) =>
        {
            if (!OperatingSystem.IsMacOS()) { Output.WriteError("Provisioning profiles are macOS only."); return; }

            var json = parseResult.GetValue(CliOptions.Json);
            var includeExpired = parseResult.GetValue(expiredOpt);
            var dir = GetProfilesDirectory();

            if (!Directory.Exists(dir))
            {
                if (json) Output.WriteJson(new { profiles = Array.Empty<object>() });
                else Console.WriteLine("No provisioning profiles directory found.");
                return;
            }

            var files = Directory.GetFiles(dir, "*.mobileprovision");
            var profiles = new List<ProfileInfo>();

            foreach (var file in files)
            {
                var info = await DecodeProfileAsync(file);
                if (info is null) continue;
                if (!includeExpired && info.ExpirationDate < DateTime.UtcNow) continue;
                profiles.Add(info);
            }

            profiles.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            if (json)
            {
                Output.WriteJson(new { profiles });
                return;
            }

            if (profiles.Count == 0)
            {
                Console.WriteLine("No provisioning profiles installed.");
                return;
            }

            Output.WriteTable(
                ["Name", "UUID", "Team", "Type", "App ID", "Expires"],
                profiles.Select(p => new[]
                {
                    Truncate(p.Name, 30),
                    p.Uuid[..8] + "…",
                    p.TeamName ?? "",
                    p.ProfileType ?? "",
                    p.AppIdName ?? p.BundleId ?? "",
                    p.ExpirationDate.ToString("yyyy-MM-dd"),
                }));

            Console.WriteLine($"\n  {profiles.Count} profile(s)");
        });
        return cmd;
    }

    private static Command CreateShowCommand()
    {
        var cmd = new Command("show", "Show detailed information about a provisioning profile.\n\nAccepts a UUID, file path, or profile name substring.");
        var idArg = new Argument<string>("profile") { Description = "Profile UUID, file path, or name substring" };
        cmd.Add(idArg);
        cmd.SetAction(async (parseResult, ct) =>
        {
            if (!OperatingSystem.IsMacOS()) { Output.WriteError("macOS only."); return; }

            var json = parseResult.GetValue(CliOptions.Json);
            var id = parseResult.GetValue(idArg);
            var filePath = await ResolveProfilePathAsync(id);

            if (filePath is null)
            {
                Output.WriteError($"Profile not found: {id}");
                return;
            }

            var info = await DecodeProfileAsync(filePath);
            if (info is null)
            {
                Output.WriteError("Failed to decode profile.");
                return;
            }

            if (json)
            {
                Output.WriteJson(info);
                return;
            }

            Console.WriteLine($"  Name:       {info.Name}");
            Console.WriteLine($"  UUID:       {info.Uuid}");
            Console.WriteLine($"  Type:       {info.ProfileType}");
            Console.WriteLine($"  App ID:     {info.AppIdName}");
            Console.WriteLine($"  Bundle ID:  {info.BundleId}");
            Console.WriteLine($"  Team:       {info.TeamName} ({info.TeamId})");
            Console.WriteLine($"  Created:    {info.CreationDate:yyyy-MM-dd}");
            Console.WriteLine($"  Expires:    {info.ExpirationDate:yyyy-MM-dd}");
            Console.WriteLine($"  Expired:    {(info.ExpirationDate < DateTime.UtcNow ? "YES ⚠" : "no")}");
            Console.WriteLine($"  File:       {filePath}");

            if (info.Entitlements?.Count > 0)
            {
                Console.WriteLine($"  Entitlements:");
                foreach (var ent in info.Entitlements)
                    Console.WriteLine($"    • {ent}");
            }

            if (info.DeviceCount > 0)
                Console.WriteLine($"  Devices:    {info.DeviceCount} provisioned device(s)");
        });
        return cmd;
    }

    private static Command CreateInstallCommand()
    {
        var cmd = new Command("install", "Install a .mobileprovision file to the system profiles directory.\n\nExample:\n  maui-sherpa apple profiles install ./MyApp.mobileprovision");
        var fileArg = new Argument<string>("file") { Description = "Path to .mobileprovision file" };
        cmd.Add(fileArg);
        cmd.SetAction(async (parseResult, ct) =>
        {
            if (!OperatingSystem.IsMacOS()) { Output.WriteError("macOS only."); return; }

            var file = parseResult.GetValue(fileArg);
            if (!File.Exists(file))
            {
                Output.WriteError($"File not found: {file}");
                return;
            }

            var info = await DecodeProfileAsync(file);
            if (info is null)
            {
                Output.WriteError("Invalid provisioning profile.");
                return;
            }

            var dir = GetProfilesDirectory();
            Directory.CreateDirectory(dir);

            var destPath = Path.Combine(dir, $"{info.Uuid}.mobileprovision");
            File.Copy(file, destPath, overwrite: true);

            Output.WriteSuccess($"Installed '{info.Name}' ({info.Uuid})");
            Output.WriteInfo($"→ {destPath}");
        });
        return cmd;
    }

    private static Command CreateRemoveCommand()
    {
        var cmd = new Command("remove", "Remove a provisioning profile from the system.\n\nAccepts a UUID, file path, or profile name substring.");
        var idArg = new Argument<string>("profile") { Description = "Profile UUID or name substring" };
        cmd.Add(idArg);
        cmd.SetAction(async (parseResult, ct) =>
        {
            if (!OperatingSystem.IsMacOS()) { Output.WriteError("macOS only."); return; }

            var id = parseResult.GetValue(idArg);
            var filePath = await ResolveProfilePathAsync(id);

            if (filePath is null)
            {
                Output.WriteError($"Profile not found: {id}");
                return;
            }

            var info = await DecodeProfileAsync(filePath);
            File.Delete(filePath);
            Output.WriteSuccess($"Removed '{info?.Name ?? id}'");
        });
        return cmd;
    }

    private static Command CreateCreateCommand()
    {
        var cmd = new Command("create", "Create a new provisioning profile on App Store Connect.\n\nRequires API credentials via options or env vars (APPLE_KEY_ID, APPLE_ISSUER_ID, APPLE_P8_FILE).\n\nExamples:\n  maui-sherpa apple profiles create --name \"MyApp Dev\" --type IOS_APP_DEVELOPMENT --bundle-id com.company.app --all-devices --p8-file ~/AuthKey.p8 --key-id ABC --issuer-id UUID\n  maui-sherpa apple profiles create --name \"MyApp Store\" --type IOS_APP_STORE --bundle-id com.company.app --certificate-id CERT1");

        var nameOpt = new Option<string>("--name") { Description = "Profile name", Required = true };
        var typeOpt = new Option<string>("--type") { Description = $"Profile type: {string.Join(", ", ValidProfileTypes)}", Required = true };
        var bundleIdOpt = new Option<string>("--bundle-id") { Description = "Bundle identifier (e.g. com.company.app) or ASC resource ID", Required = true };
        var certIdsOpt = new Option<string[]>("--certificate-id") { Description = "Certificate resource ID(s). Repeat for multiple. If omitted, auto-selects valid certificates.", AllowMultipleArgumentsPerToken = true };
        var deviceIdsOpt = new Option<string[]>("--device-id") { Description = "Device resource ID(s). Repeat for multiple.", AllowMultipleArgumentsPerToken = true };
        var allDevicesOpt = new Option<bool>("--all-devices") { Description = "Include all registered devices (for Development/Ad Hoc profiles)" };
        var installOpt = new Option<bool>("--install") { Description = "Install the profile locally after creation" };

        cmd.Add(nameOpt);
        cmd.Add(typeOpt);
        cmd.Add(bundleIdOpt);
        cmd.Add(certIdsOpt);
        cmd.Add(deviceIdsOpt);
        cmd.Add(allDevicesOpt);
        cmd.Add(installOpt);
        AppleConnectHelper.AddAuthOptions(cmd);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var json = parseResult.GetValue(CliOptions.Json);
            var name = parseResult.GetValue(nameOpt)!;
            var profileTypeStr = parseResult.GetValue(typeOpt)!;
            var bundleId = parseResult.GetValue(bundleIdOpt)!;
            var certIds = parseResult.GetValue(certIdsOpt) ?? [];
            var deviceIds = parseResult.GetValue(deviceIdsOpt) ?? [];
            var allDevices = parseResult.GetValue(allDevicesOpt);
            var install = parseResult.GetValue(installOpt);

            // Validate profile type
            if (!ValidProfileTypes.Contains(profileTypeStr, StringComparer.OrdinalIgnoreCase))
            {
                Output.WriteError($"Invalid profile type: {profileTypeStr}");
                Output.WriteInfo($"Valid types: {string.Join(", ", ValidProfileTypes)}");
                return;
            }

            var needsDevices = profileTypeStr.Contains("DEVELOPMENT", StringComparison.OrdinalIgnoreCase) ||
                               profileTypeStr.Contains("ADHOC", StringComparison.OrdinalIgnoreCase);

            if (needsDevices && deviceIds.Length == 0 && !allDevices)
            {
                Output.WriteError("Development and Ad Hoc profiles require devices. Use --device-id or --all-devices.");
                return;
            }

            try
            {
                var client = await AppleConnectHelper.CreateClientAsync(
                    parseResult.GetValue(AppleConnectHelper.KeyIdOption),
                    parseResult.GetValue(AppleConnectHelper.IssuerIdOption),
                    parseResult.GetValue(AppleConnectHelper.P8FileOption));

                // Resolve bundle ID identifier → resource ID
                if (!json) Output.WriteInfo($"Resolving bundle ID: {bundleId}");
                var (bundleResourceId, bundleIdentifier, bundleName) =
                    await AppleConnectHelper.ResolveBundleIdAsync(client, bundleId);
                if (!json) Output.WriteInfo($"  → {bundleName} ({bundleIdentifier}) [{bundleResourceId}]");

                // Auto-select certificates if none provided
                var certificateIdList = certIds.ToList();
                if (certificateIdList.Count == 0)
                {
                    if (!json) Output.WriteInfo("Auto-selecting certificates...");
                    var certs = await ListCertificatesAsync(client);
                    var validCerts = certs
                        .Where(c => c.ExpirationDate > DateTime.UtcNow)
                        .Where(c => IsCompatibleCertificate(c.CertificateType, profileTypeStr))
                        .ToList();

                    if (validCerts.Count == 0)
                    {
                        Output.WriteError("No valid certificates found for this profile type.");
                        Output.WriteInfo("Create a certificate first, or specify --certificate-id explicitly.");
                        return;
                    }

                    certificateIdList = validCerts.Select(c => c.Id).ToList();
                    if (!json)
                    {
                        foreach (var c in validCerts)
                            Output.WriteInfo($"  → {c.Name} ({c.CertificateType}) [{c.Id}]");
                    }
                }

                // Resolve devices
                string[]? resolvedDeviceIds = null;
                if (needsDevices)
                {
                    if (allDevices)
                    {
                        if (!json) Output.WriteInfo("Fetching all registered devices...");
                        var devices = await ListDevicesAsync(client);
                        var enabledDevices = devices
                            .Where(d => d.Status.Equals("ENABLED", StringComparison.OrdinalIgnoreCase))
                            .Where(d => IsCompatibleDevice(d.Platform, profileTypeStr))
                            .ToList();

                        if (enabledDevices.Count == 0)
                        {
                            Output.WriteError("No enabled devices found for this platform.");
                            return;
                        }

                        resolvedDeviceIds = enabledDevices.Select(d => d.Id).ToArray();
                        if (!json) Output.WriteInfo($"  → {resolvedDeviceIds.Length} device(s) selected");
                    }
                    else
                    {
                        resolvedDeviceIds = deviceIds;
                    }
                }

                // Create the profile
                if (!json) Output.WriteInfo($"Creating profile '{name}'...");
                var profileType = Enum.Parse<ProfileType>(profileTypeStr, ignoreCase: true);
                var response = await client.CreateProfileAsync(
                    name: name,
                    profileType: profileType,
                    bundleIdId: bundleResourceId,
                    certificateIds: certificateIdList.ToArray(),
                    deviceIds: resolvedDeviceIds,
                    cancellationToken: ct);

                var p = response.Data;
                var result = new
                {
                    id = p.Id,
                    name = p.Attributes?.Name ?? name,
                    profileType = p.Attributes?.ProfileType.ToString() ?? profileTypeStr,
                    platform = p.Attributes?.Platform.ToString() ?? "IOS",
                    state = p.Attributes?.ProfileState.ToString() ?? "ACTIVE",
                    expirationDate = p.Attributes?.ExpirationDate?.DateTime.ToString("yyyy-MM-dd"),
                    uuid = p.Attributes?.Uuid ?? "",
                    bundleId = bundleIdentifier,
                };

                if (json)
                {
                    Output.WriteJson(result);
                }
                else
                {
                    Output.WriteSuccess($"Profile created: {result.name}");
                    Output.WriteInfo($"  ID:       {result.id}");
                    Output.WriteInfo($"  UUID:     {result.uuid}");
                    Output.WriteInfo($"  Type:     {result.profileType}");
                    Output.WriteInfo($"  Bundle:   {result.bundleId}");
                    Output.WriteInfo($"  Expires:  {result.expirationDate}");
                }

                // Install locally if requested
                if (install && OperatingSystem.IsMacOS())
                {
                    await InstallProfileFromAscAsync(client, p.Id, p.Attributes?.Uuid, json);
                }
                else if (install && !OperatingSystem.IsMacOS())
                {
                    if (!json) Output.WriteWarning("--install is only supported on macOS.");
                }
            }
            catch (Exception ex)
            {
                if (json)
                    Output.WriteJson(new { error = ex.Message });
                else
                    Output.WriteError(GetFriendlyError(ex));
            }
        });
        return cmd;
    }

    private static Command CreateAscListCommand()
    {
        var cmd = new Command("asc-list", "List provisioning profiles from App Store Connect.\n\nRequires API credentials via options or env vars (APPLE_KEY_ID, APPLE_ISSUER_ID, APPLE_P8_FILE).");
        var includeExpiredOpt = new Option<bool>("--include-expired") { Description = "Include expired profiles" };
        cmd.Add(includeExpiredOpt);
        AppleConnectHelper.AddAuthOptions(cmd);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var json = parseResult.GetValue(CliOptions.Json);
            var includeExpired = parseResult.GetValue(includeExpiredOpt);

            try
            {
                var client = await AppleConnectHelper.CreateClientAsync(
                    parseResult.GetValue(AppleConnectHelper.KeyIdOption),
                    parseResult.GetValue(AppleConnectHelper.IssuerIdOption),
                    parseResult.GetValue(AppleConnectHelper.P8FileOption));

                var response = await client.ListProfilesAsync(
                    filterId: null, filterName: null,
                    filterProfileState: null, filterProfileType: null,
                    include: "bundleId", sort: null, limit: 100,
                    limitCertificates: null, limitDevices: null,
                    fieldsProfiles: null, fieldsBundleIds: null,
                    fieldsCertificates: null, fieldsDevices: null,
                    cancellationToken: ct);

                var bundleIdLookup = (response.IncludedBundleIds ?? [])
                    .ToDictionary(b => b.Id, b => b.Attributes?.Identifier ?? "");

                var profiles = response.Data
                    .Select(p =>
                    {
                        var bundleIds = p.Relationships?.TryGetValue("bundleId", out var rel) == true
                            ? (rel?.Data ?? [])
                                .Select(d => bundleIdLookup.TryGetValue(d.Id, out var id) ? id : null)
                                .Where(id => !string.IsNullOrEmpty(id)).ToList()
                            : new List<string?>();
                        return new
                        {
                            id = p.Id,
                            name = p.Attributes?.Name ?? "",
                            profileType = p.Attributes?.ProfileType.ToString() ?? "",
                            platform = p.Attributes?.Platform.ToString() ?? "",
                            state = p.Attributes?.ProfileState.ToString() ?? "",
                            expirationDate = p.Attributes?.ExpirationDate?.DateTime ?? DateTime.MinValue,
                            uuid = p.Attributes?.Uuid ?? "",
                            bundleId = string.Join(", ", bundleIds),
                        };
                    })
                    .Where(p => includeExpired || p.expirationDate > DateTime.UtcNow)
                    .OrderBy(p => p.name)
                    .ToList();

                if (json)
                {
                    Output.WriteJson(new { profiles });
                    return;
                }

                if (profiles.Count == 0)
                {
                    Console.WriteLine("No profiles found on App Store Connect.");
                    return;
                }

                Output.WriteTable(
                    ["Name", "ID", "Type", "Bundle ID", "State", "Expires"],
                    profiles.Select(p => new[]
                    {
                        Truncate(p.name, 30),
                        p.id,
                        p.profileType,
                        p.bundleId,
                        p.state,
                        p.expirationDate.ToString("yyyy-MM-dd"),
                    }));

                Console.WriteLine($"\n  {profiles.Count} profile(s)");
            }
            catch (Exception ex)
            {
                if (json)
                    Output.WriteJson(new { error = ex.Message });
                else
                    Output.WriteError(GetFriendlyError(ex));
            }
        });
        return cmd;
    }

    private static async Task InstallProfileFromAscAsync(
        AppStoreConnectClient client, string profileId, string? uuid, bool json)
    {
        try
        {
            var response = await client.ListProfilesAsync(
                filterId: new[] { profileId }, filterName: null,
                filterProfileState: null, filterProfileType: null,
                include: null, sort: null, limit: 1,
                limitCertificates: null, limitDevices: null,
                fieldsProfiles: null, fieldsBundleIds: null,
                fieldsCertificates: null, fieldsDevices: null,
                cancellationToken: default);

            var profile = response.Data.FirstOrDefault();
            if (profile?.Attributes?.ProfileContent is null)
            {
                if (!json) Output.WriteWarning("Could not download profile content for install.");
                return;
            }

            var content = Convert.FromBase64String(profile.Attributes.ProfileContent);

            // Extract UUID from profile content if not provided
            if (string.IsNullOrEmpty(uuid))
                uuid = ExtractUuidFromContent(content) ?? Guid.NewGuid().ToString("D").ToUpperInvariant();

            var dir = GetProfilesDirectory();
            Directory.CreateDirectory(dir);
            var destPath = Path.Combine(dir, $"{uuid}.mobileprovision");
            await File.WriteAllBytesAsync(destPath, content);

            if (!json) Output.WriteSuccess($"Installed to {destPath}");
        }
        catch (Exception ex)
        {
            if (!json) Output.WriteWarning($"Install failed: {ex.Message}");
        }
    }

    private static string? ExtractUuidFromContent(byte[] content)
    {
        var text = System.Text.Encoding.UTF8.GetString(content);
        var idx = text.IndexOf("<key>UUID</key>");
        if (idx < 0) return null;
        var start = text.IndexOf("<string>", idx);
        var end = text.IndexOf("</string>", start);
        if (start >= 0 && end > start)
            return text.Substring(start + 8, end - start - 8);
        return null;
    }

    private record CertInfo(string Id, string Name, string CertificateType, DateTime ExpirationDate);
    private record DeviceInfo(string Id, string Name, string Platform, string Status);

    private static async Task<List<CertInfo>> ListCertificatesAsync(AppStoreConnectClient client)
    {
        var response = await client.ListCertificatesAsync(
            filterId: null, filterDisplayName: null, filterSerialNumber: null,
            filterCertificateType: null,
            sort: null, limit: 100,
            fieldsCertificates: new[] { "displayName", "name", "certificateType", "platform", "serialNumber", "certificateContent" },
            cancellationToken: default);

        return response.Data.Select(c =>
        {
            var expirationDate = DateTime.UtcNow.AddYears(1);
            try
            {
                if (!string.IsNullOrEmpty(c.Attributes?.CertificateContent))
                {
                    using var x509 = System.Security.Cryptography.X509Certificates.X509CertificateLoader
                        .LoadCertificate(Convert.FromBase64String(c.Attributes.CertificateContent));
                    expirationDate = x509.NotAfter;
                }
            }
            catch { }

            return new CertInfo(
                c.Id,
                c.Attributes?.DisplayName ?? c.Attributes?.Name ?? "",
                c.Attributes?.CertificateTypeValue ?? c.Attributes?.CertificateType.ToString() ?? "DEVELOPMENT",
                expirationDate);
        }).ToList();
    }

    private static async Task<List<DeviceInfo>> ListDevicesAsync(AppStoreConnectClient client)
    {
        var response = await client.ListDevicesAsync(
            filterId: null, filterIdentifier: null, filterName: null,
            filterPlatform: null, filterStatus: null, filterUdid: null,
            include: null, sort: null, limit: 100,
            limitProfiles: null, limitBundleIdCapabilities: null,
            fieldsDevices: null,
            cancellationToken: default);

        return response.Data.Select(d => new DeviceInfo(
            d.Id,
            d.Attributes?.Name ?? "",
            d.Attributes?.Platform.ToString() ?? "IOS",
            d.Attributes?.Status.ToString() ?? "ENABLED"
        )).ToList();
    }

    private static bool IsCompatibleCertificate(string certType, string profileType)
    {
        var isDev = profileType.Contains("DEVELOPMENT", StringComparison.OrdinalIgnoreCase);
        var isDirect = profileType.Contains("DIRECT", StringComparison.OrdinalIgnoreCase);
        var isMac = profileType.StartsWith("MAC", StringComparison.OrdinalIgnoreCase);

        if (isDev)
            return certType.Contains("DEVELOPMENT", StringComparison.OrdinalIgnoreCase);
        if (isDirect)
            return certType.Contains("DIRECT", StringComparison.OrdinalIgnoreCase) ||
                   certType.Contains("DEVELOPER_ID", StringComparison.OrdinalIgnoreCase);
        // Distribution (App Store, Ad Hoc, In-House)
        return certType.Contains("DISTRIBUTION", StringComparison.OrdinalIgnoreCase) ||
               certType.Contains("IN_HOUSE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCompatibleDevice(string devicePlatform, string profileType)
    {
        if (profileType.Contains("IOS", StringComparison.OrdinalIgnoreCase))
            return devicePlatform.Contains("IOS", StringComparison.OrdinalIgnoreCase);
        if (profileType.Contains("MAC", StringComparison.OrdinalIgnoreCase))
            return devicePlatform.Contains("MAC", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private static string GetFriendlyError(Exception ex)
    {
        var msg = ex.Message;
        if (msg.Contains("403") || msg.Contains("FORBIDDEN"))
            return "Access Denied (403): Your API key may not have permission for this operation.";
        if (msg.Contains("401") || msg.Contains("UNAUTHORIZED"))
            return "Authentication Failed (401): Check your Key ID, Issuer ID, and P8 key file.";
        if (msg.Contains("409") || msg.Contains("ENTITY_ERROR"))
        {
            if (msg.Contains("already exists"))
                return "A profile with this name or configuration already exists.";
            return $"Conflict: {msg}";
        }
        return msg;
    }

    private static async Task<string?> ResolveProfilePathAsync(string id)
    {
        // Direct file path
        if (File.Exists(id)) return id;

        var dir = GetProfilesDirectory();
        if (!Directory.Exists(dir)) return null;

        // UUID match
        var byUuid = Path.Combine(dir, $"{id}.mobileprovision");
        if (File.Exists(byUuid)) return byUuid;

        // Name substring match — decode all and find
        foreach (var file in Directory.GetFiles(dir, "*.mobileprovision"))
        {
            var info = await DecodeProfileAsync(file);
            if (info is null) continue;
            if (info.Uuid.StartsWith(id, StringComparison.OrdinalIgnoreCase) ||
                info.Name.Contains(id, StringComparison.OrdinalIgnoreCase))
                return file;
        }

        return null;
    }

    private static async Task<ProfileInfo?> DecodeProfileAsync(string filePath)
    {
        try
        {
            var result = await ProcessRunner.RunAsync("security", $"cms -D -i \"{filePath}\"");
            if (result.ExitCode != 0) return null;

            var plist = result.Output;

            return new ProfileInfo(
                Name: ExtractPlistValue(plist, "Name") ?? Path.GetFileNameWithoutExtension(filePath),
                Uuid: ExtractPlistValue(plist, "UUID") ?? "",
                TeamName: ExtractPlistValue(plist, "TeamName"),
                TeamId: ExtractPlistArrayFirst(plist, "TeamIdentifier"),
                ProfileType: DetectProfileType(plist),
                BundleId: ExtractEntitlementValue(plist, "application-identifier"),
                AppIdName: ExtractPlistValue(plist, "AppIDName"),
                CreationDate: ParsePlistDate(ExtractPlistValue(plist, "CreationDate")),
                ExpirationDate: ParsePlistDate(ExtractPlistValue(plist, "ExpirationDate")),
                Entitlements: ExtractEntitlementKeys(plist),
                DeviceCount: CountPlistArrayItems(plist, "ProvisionedDevices"),
                FilePath: filePath
            );
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractPlistValue(string plist, string key)
    {
        // Match both <string> and <date> value elements
        var pattern = $@"<key>{Regex.Escape(key)}</key>\s*<(?:string|date)>(.*?)</(?:string|date)>";
        var match = Regex.Match(plist, pattern, RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractPlistArrayFirst(string plist, string key)
    {
        var pattern = $@"<key>{Regex.Escape(key)}</key>\s*<array>\s*<string>(.*?)</string>";
        var match = Regex.Match(plist, pattern, RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractEntitlementValue(string plist, string key)
    {
        var entPattern = @"<key>Entitlements</key>\s*<dict>(.*?)</dict>";
        var entMatch = Regex.Match(plist, entPattern, RegexOptions.Singleline);
        if (!entMatch.Success) return null;

        var entDict = entMatch.Groups[1].Value;
        return ExtractPlistValue($"<plist>{entDict}</plist>", key);
    }

    private static List<string> ExtractEntitlementKeys(string plist)
    {
        var entPattern = @"<key>Entitlements</key>\s*<dict>(.*?)</dict>";
        var entMatch = Regex.Match(plist, entPattern, RegexOptions.Singleline);
        if (!entMatch.Success) return new();

        return Regex.Matches(entMatch.Groups[1].Value, @"<key>(.*?)</key>")
            .Select(m => m.Groups[1].Value)
            .ToList();
    }

    private static int CountPlistArrayItems(string plist, string key)
    {
        var pattern = $@"<key>{Regex.Escape(key)}</key>\s*<array>(.*?)</array>";
        var match = Regex.Match(plist, pattern, RegexOptions.Singleline);
        if (!match.Success) return 0;
        return Regex.Matches(match.Groups[1].Value, @"<string>").Count;
    }

    private static string DetectProfileType(string plist)
    {
        var hasDevices = plist.Contains("<key>ProvisionedDevices</key>");
        var provisionsAll = plist.Contains("<key>ProvisionsAllDevices</key>") &&
                            plist.Contains("<true/>");

        if (provisionsAll) return "Enterprise/In-House";
        if (hasDevices)
        {
            var getTaskAllow = ExtractEntitlementValue(plist, "get-task-allow");
            return getTaskAllow == null ? "Ad Hoc" : "Development";
        }
        return "App Store";
    }

    private static DateTime ParsePlistDate(string? dateStr)
    {
        if (dateStr is null) return DateTime.MinValue;
        if (DateTime.TryParse(dateStr, out var dt)) return dt;
        return DateTime.MinValue;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    private record ProfileInfo(
        string Name,
        string Uuid,
        string? TeamName,
        string? TeamId,
        string? ProfileType,
        string? BundleId,
        string? AppIdName,
        DateTime CreationDate,
        DateTime ExpirationDate,
        List<string> Entitlements,
        int DeviceCount,
        string? FilePath = null);
}
