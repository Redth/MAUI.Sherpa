using System.CommandLine;
using System.Text.Json;
using System.Text.RegularExpressions;
using MauiSherpa.Cli.Helpers;

namespace MauiSherpa.Cli.Commands.Apple;

public static class ProfilesCommand
{
    private static string GetProfilesDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "MobileDevice", "Provisioning Profiles");

    public static Command Create()
    {
        var cmd = new Command("profiles", "Manage locally installed provisioning profiles.\n\nList, inspect, install, and remove .mobileprovision files from ~/Library/MobileDevice/Provisioning Profiles/.");
        cmd.Add(CreateListCommand());
        cmd.Add(CreateShowCommand());
        cmd.Add(CreateInstallCommand());
        cmd.Add(CreateRemoveCommand());
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
