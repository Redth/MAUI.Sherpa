using Microsoft.Win32;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public sealed class AndroidSdkPathService(
    IAndroidSdkService sdk, IProcessExecutionService processes, ILoggingService logger) : IAndroidSdkPathService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public bool CanConfigureMachinePath => sdk.SdkPath is { } path && AndroidSdkPathConfiguration.IsSharedLocation(path) &&
        DiscoverTools(path, true).Where(t => t.Path != null).All(t => AndroidSdkPathConfiguration.IsSharedLocation(t.Path!));
    public AndroidPathShell DefaultShell => OperatingSystem.IsWindows() ? AndroidPathShell.Windows :
        Path.GetFileName(Environment.GetEnvironmentVariable("SHELL")) switch
        {
            "bash" => OperatingSystem.IsMacOS() ? AndroidPathShell.BashLogin : AndroidPathShell.Bash,
            "fish" => AndroidPathShell.Fish, "sh" => AndroidPathShell.Sh,
            _ => OperatingSystem.IsMacOS() ? AndroidPathShell.Zsh : AndroidPathShell.Bash
        };

    public async Task<AndroidPathPlan> CheckAsync(AndroidPathOptions options, CancellationToken cancellationToken = default)
    {
        if (Screenshots.ScreenshotMode.Current.IsEnabled)
            throw Screenshots.ScreenshotMode.Current.Blocked("Check Android SDK PATH");
        var sdkPath = sdk.SdkPath ?? throw new InvalidOperationException("Select an Android SDK first.");
        if (!Enum.IsDefined(options.Shell) || !Enum.IsDefined(options.Scope))
            throw new ArgumentException("Select a supported shell and PATH scope.");
        var toolPaths = DiscoverTools(sdkPath, options.IncludeBuildTools);
        var directories = toolPaths.Where(t => t.Path != null).Select(t => Path.GetDirectoryName(t.Path!)!)
            .Distinct(AndroidSdkLocations.PathComparer).ToArray();
        if (options.Scope == AndroidPathScope.Machine &&
            (!CanConfigureMachinePath || directories.Any(d => !AndroidSdkPathConfiguration.IsSharedLocation(d))))
            throw new InvalidOperationException("Machine-wide PATH is only available for SDK tools installed outside user-local folders.");
        if (directories.Length == 0)
            throw new InvalidOperationException("No Android command-line tools were found. Install platform-tools or command-line tools from SDK & JDK.");
        string? warning = null;
        string? shellProfileDirectory = null;
        string currentPath;
        if (options.Shell == AndroidPathShell.Windows)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows PATH is only available on Windows.");
            currentPath = string.Join(";", new[] {
                ReadWindowsPath(AndroidPathScope.Machine).Value,
                ReadWindowsPath(AndroidPathScope.User).Value }.Where(p => !string.IsNullOrEmpty(p)));
            if (options.Scope == AndroidPathScope.User)
                warning = "Windows searches Machine PATH before User PATH. A machine-wide Android SDK entry can still override this user-only update.";
        }
        else
        {
            if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Shell profiles are only available on macOS and Linux.");
            (currentPath, shellProfileDirectory) = await GetShellPathAsync(options.Shell, cancellationToken);
        }
        var tools = toolPaths.Select(t => new AndroidPathTool(t.Name, t.Path, Resolve(t.Name, currentPath, options.Shell))).ToArray();
        var target = GetTarget(options, shellProfileDirectory);
        var exists = options.Shell == AndroidPathShell.Windows || File.Exists(target);
        var original = "";
        string? windowsKind = null;
        var bom = false;
        if (options.Shell == AndroidPathShell.Windows)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            (original, windowsKind) = ReadWindowsPath(options.Scope);
        }
        else if (exists)
            (original, bom) = AndroidSdkPathConfiguration.DecodeProfile(await File.ReadAllBytesAsync(target, cancellationToken));
        var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var preview = options.Shell == AndroidPathShell.Windows
            ? AndroidSdkPathConfiguration.UpdateWindowsPath(original, directories)
            : AndroidSdkPathConfiguration.BuildBlock(directories, options.Shell, newline);
        var updated = options.Shell == AndroidPathShell.Windows ? preview : AndroidSdkPathConfiguration.ReplaceBlock(original, preview);
        return new(sdkPath, options, target, directories, tools, preview, original, updated, exists, bom, warning, windowsKind);
    }

    public async Task ApplyAsync(AndroidPathPlan plan, CancellationToken cancellationToken = default)
    {
        if (Screenshots.ScreenshotMode.Current.IsEnabled)
            throw Screenshots.ScreenshotMode.Current.Blocked("Apply Android SDK PATH");
        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = await CheckAsync(plan.Options, cancellationToken);
            if (current.SdkPath != plan.SdkPath || current.Target != plan.Target ||
                current.OriginalContent != plan.OriginalContent || current.UpdatedContent != plan.UpdatedContent ||
                current.TargetExists != plan.TargetExists || current.HasUtf8Bom != plan.HasUtf8Bom ||
                current.WindowsValueKind != plan.WindowsValueKind)
                throw new InvalidOperationException("The SDK or PATH configuration changed since this preview. Check it again before applying.");
            if (!current.NeedsUpdate) return;
            if (current.Options.Shell == AndroidPathShell.Windows)
                await ApplyWindowsAsync(current, cancellationToken);
            else if (current.Options.Scope == AndroidPathScope.User)
                await ApplyProfileAsync(current, cancellationToken);
            else
                await ApplyElevatedProfileAsync(current, cancellationToken);

            var saved = OperatingSystem.IsWindows()
                ? ReadWindowsPath(current.Options.Scope).Value
                : AndroidSdkPathConfiguration.DecodeProfile(await File.ReadAllBytesAsync(current.Target, cancellationToken)).Content;
            if (saved != current.UpdatedContent)
                throw new IOException("PATH verification failed: the saved configuration does not match the requested update.");
            logger.LogInformation($"Android SDK PATH configured in {current.Target}. Open a new terminal to use it.");
        }
        catch (Exception ex) { logger.LogError("Could not configure Android SDK PATH.", ex); throw; }
        finally { gate.Release(); }
    }

    internal static IReadOnlyList<(string Name, string? Path)> DiscoverTools(string sdkPath, bool buildTools)
    {
        var commandBin = FindVersionDirectory(Path.Combine(sdkPath, "cmdline-tools"), "sdkmanager")
            ?? Path.Combine(sdkPath, "tools", "bin");
        var tools = new List<(string Name, string? Path)>
        {
            ("adb", FindTool(Path.Combine(sdkPath, "platform-tools"), "adb")),
            ("fastboot", FindTool(Path.Combine(sdkPath, "platform-tools"), "fastboot")),
            ("emulator", FindTool(Path.Combine(sdkPath, "emulator"), "emulator")),
            ("sdkmanager", FindTool(commandBin, "sdkmanager")),
            ("avdmanager", FindTool(commandBin, "avdmanager"))
        };
        if (buildTools)
        {
            var directory = FindVersionDirectory(Path.Combine(sdkPath, "build-tools"), "aapt2", binSubfolder: false);
            foreach (var name in new[] { "aapt", "aapt2", "apksigner", "zipalign" })
                tools.Add((name, directory == null ? null : FindTool(directory, name)));
        }
        return tools;
    }

    private static string? FindVersionDirectory(string root, string tool, bool binSubfolder = true)
    {
        if (!Directory.Exists(root)) return null;
        return Directory.EnumerateDirectories(root)
            .OrderByDescending(d => Path.GetFileName(d) == "latest")
            .ThenByDescending(d => !Path.GetFileName(d).Contains('-'))
            .ThenByDescending(d => Version.TryParse(Path.GetFileName(d).Split('-')[0], out var version) ? version : new Version())
            .Select(d => binSubfolder ? Path.Combine(d, "bin") : d)
            .FirstOrDefault(d => FindTool(d, tool) != null);
    }

    private static string? FindTool(string directory, string name)
    {
        foreach (var suffix in OperatingSystem.IsWindows() ? new[] { ".exe", ".bat", ".cmd", "" } : new[] { "" })
        {
            var file = Path.Combine(directory, name + suffix);
            if (File.Exists(file) && (OperatingSystem.IsWindows() ||
                (File.GetUnixFileMode(file) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0))
                return file;
        }
        return null;
    }

    private static string? Resolve(string name, string path, AndroidPathShell shell)
    {
        foreach (var entry in path.Split(shell == AndroidPathShell.Windows ? ';' : ':'))
        {
            var directory = shell == AndroidPathShell.Windows ? Environment.ExpandEnvironmentVariables(entry.Trim().Trim('"')) : entry;
            if (string.IsNullOrEmpty(directory)) continue;
            if (FindTool(directory, name) is { } tool) return tool;
        }
        return null;
    }

    private async Task<(string Path, string? ProfileDirectory)> GetShellPathAsync(AndroidPathShell shell, CancellationToken ct)
    {
        var executable = GetShellExecutable(shell);
        if (executable == null)
            throw new InvalidOperationException($"{shell} is not installed. Choose an installed shell before configuring PATH.");
        var command = shell == AndroidPathShell.Fish
            ? "printf '\\n__MAUI_SHERPA_PATH__%s\\n' (string join : $PATH); printf '__MAUI_SHERPA_PROFILE__%s\\n' \"$__fish_config_dir\""
            : "printf '\\n__MAUI_SHERPA_PATH__%s\\n' \"$PATH\"" +
                (shell == AndroidPathShell.Zsh ? "; printf '__MAUI_SHERPA_PROFILE__%s\\n' \"${ZDOTDIR-$HOME}\"" : "");
        var result = await processes.ExecuteAsync(new ProcessRequest(executable,
            [shell is AndroidPathShell.Sh ? "-lc" : shell is AndroidPathShell.Bash or AndroidPathShell.Fish ? "-ic" : "-lic",
                command]), ct);
        if (!result.Success)
            throw new InvalidOperationException($"Could not inspect {shell} PATH: {result.Error}");
        const string marker = "__MAUI_SHERPA_PATH__";
        var line = result.Output.Split('\n').LastOrDefault(l => l.StartsWith(marker, StringComparison.Ordinal));
        if (line == null) throw new InvalidOperationException("The shell did not return its PATH. Check the shell startup files for errors.");
        const string profileMarker = "__MAUI_SHERPA_PROFILE__";
        var profile = result.Output.Split('\n').LastOrDefault(l => l.StartsWith(profileMarker, StringComparison.Ordinal))?[profileMarker.Length..].TrimEnd('\r');
        if (shell is AndroidPathShell.Zsh or AndroidPathShell.Fish && (profile == null || !Path.IsPathFullyQualified(profile)))
            throw new InvalidOperationException("The shell did not return an absolute configuration directory. Check ZDOTDIR or the fish configuration.");
        return (line[marker.Length..].TrimEnd('\r'), profile);
    }

    private static string? GetShellExecutable(AndroidPathShell shell)
    {
        var name = shell == AndroidPathShell.BashLogin ? "bash" : shell.ToString().ToLowerInvariant();
        var configured = Environment.GetEnvironmentVariable("SHELL");
        if (Path.GetFileName(configured) == name && File.Exists(configured)) return configured;
        return new[] { $"/bin/{name}", $"/usr/bin/{name}", $"/opt/homebrew/bin/{name}", $"/usr/local/bin/{name}" }.FirstOrDefault(File.Exists);
    }

    private static string GetTarget(AndroidPathOptions options, string? shellProfileDirectory)
    {
        if (options.Shell == AndroidPathShell.Windows)
            return options.Scope == AndroidPathScope.User ? "Windows User PATH" : "Windows Machine PATH";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string target;
        if (options.Scope == AndroidPathScope.Machine)
            target = options.Shell switch
            {
                AndroidPathShell.Zsh => File.Exists("/etc/zsh/zshrc") ? "/etc/zsh/zshrc" : "/etc/zshrc",
                AndroidPathShell.Bash => File.Exists("/etc/bash.bashrc") ? "/etc/bash.bashrc" : "/etc/bashrc",
                AndroidPathShell.BashLogin => "/etc/profile",
                AndroidPathShell.Fish => "/etc/fish/conf.d/maui-sherpa-android-sdk.fish",
                _ => "/etc/profile"
            };
        else
            target = options.Shell switch
            {
                AndroidPathShell.Zsh => Path.Combine(shellProfileDirectory ?? home, ".zshrc"),
                AndroidPathShell.Bash => Path.Combine(home, ".bashrc"),
                AndroidPathShell.BashLogin => new[] { ".bash_profile", ".bash_login", ".profile" }.Select(p => Path.Combine(home, p)).FirstOrDefault(File.Exists) ?? Path.Combine(home, ".bash_profile"),
                AndroidPathShell.Fish => Path.Combine(shellProfileDirectory ?? Path.Combine(home, ".config", "fish"), "conf.d", "maui-sherpa-android-sdk.fish"),
                _ => Path.Combine(home, ".profile")
            };
        return AndroidSdkPathConfiguration.ResolveDirectory(AndroidSdkLocations.Normalize(target));
    }

    private static byte[] GetBytes(AndroidPathPlan plan) =>
        new UTF8Encoding(plan.HasUtf8Bom).GetPreamble().Concat(Encoding.UTF8.GetBytes(plan.UpdatedContent)).ToArray();

    private static string BackupPath(string target) => $"{target}.sherpa-backup-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";

    private static async Task ApplyProfileAsync(AndroidPathPlan plan, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(plan.Target)!);
        var temp = plan.Target + $".sherpa-{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllBytesAsync(temp, GetBytes(plan), ct);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temp, plan.TargetExists ? File.GetUnixFileMode(plan.Target) : UnixFileMode.UserRead | UnixFileMode.UserWrite);
            ct.ThrowIfCancellationRequested();
            if (File.Exists(plan.Target) != plan.TargetExists ||
                (plan.TargetExists && !File.ReadAllBytes(plan.Target).SequenceEqual(
                    (plan.HasUtf8Bom ? Encoding.UTF8.Preamble.ToArray() : []).Concat(Encoding.UTF8.GetBytes(plan.OriginalContent)))))
                throw new IOException("The shell profile changed. Check PATH again before applying.");
            if (plan.TargetExists) File.Copy(plan.Target, BackupPath(plan.Target), overwrite: false);
            File.Move(temp, plan.Target, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private async Task ApplyElevatedProfileAsync(AndroidPathPlan plan, CancellationToken ct)
    {
        var staging = Path.Combine(AppDataPath.GetAppDataDirectory(), "path-updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(staging, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var stagedFile = Path.Combine(staging, "profile");
        try
        {
            await File.WriteAllBytesAsync(stagedFile, GetBytes(plan), ct);
            var q = AndroidSdkPathConfiguration.Quote;
            var target = q(plan.Target);
            var hashCommand = OperatingSystem.IsMacOS() ? "/usr/bin/shasum -a 256" : "/usr/bin/sha256sum";
            var expectedHash = Convert.ToHexString(SHA256.HashData((plan.HasUtf8Bom ? Encoding.UTF8.Preamble.ToArray() : [])
                .Concat(Encoding.UTF8.GetBytes(plan.OriginalContent)).ToArray())).ToLowerInvariant();
            var guard = plan.TargetExists
                ? $"actual=$({hashCommand} {target}); [ \"${{actual%% *}}\" = {q(expectedHash)} ] || {{ echo 'Profile changed; refresh the preview.' >&2; exit 1; }}"
                : $"[ ! -e {target} ] && [ ! -L {target} ] || {{ echo 'Profile now exists; refresh the preview.' >&2; exit 1; }}";
            var destinationTemp = plan.Target + $".sherpa-{Guid.NewGuid():N}";
            var script = $"set -eu\n[ ! -L {target} ] || {{ echo 'Profile became a symbolic link; refresh the preview.' >&2; exit 1; }}\n{guard}\nmkdir -p {q(Path.GetDirectoryName(plan.Target)!)}\n" +
                $"trap {q($"rm -f {q(destinationTemp)}")} 0\n" +
                (plan.TargetExists ? $"cp -p {target} {q(BackupPath(plan.Target))}\n" : "") +
                (plan.TargetExists ? $"cp -p {target} {q(destinationTemp)}\ncat {q(stagedFile)} > {q(destinationTemp)}\n" :
                    $"cp {q(stagedFile)} {q(destinationTemp)}\nchmod 644 {q(destinationTemp)}\n") +
                $"{guard}\nmv -f {q(destinationTemp)} {target}\n";
            var request = OperatingSystem.IsMacOS()
                ? new ProcessRequest("/bin/sh", ["-c", script], RequiresElevation: true, Title: "Configure machine-wide Android SDK PATH")
                : File.Exists("/usr/bin/pkexec")
                    ? new ProcessRequest("/usr/bin/pkexec", ["/bin/sh", "-c", script], Title: "Configure machine-wide Android SDK PATH")
                    : throw new PlatformNotSupportedException("Machine-wide updates require polkit (pkexec). Install it or use a user-only PATH update.");
            var result = await processes.ExecuteAsync(request, ct);
            if (!result.Success) throw new IOException($"Administrator PATH update failed or was cancelled: {result.Error}");
        }
        finally
        {
            if (File.Exists(stagedFile)) File.Delete(stagedFile);
            Directory.Delete(staging);
        }
    }

    private async Task ApplyWindowsAsync(AndroidPathPlan plan, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var backup = Path.Combine(AppDataPath.GetAppDataDirectory(), $"windows-{plan.Options.Scope.ToString().ToLowerInvariant()}-path-backup-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(backup, plan.OriginalContent, ct);
        var q = AndroidSdkPathConfiguration.PowerShellQuote;
        var hive = plan.Options.Scope == AndroidPathScope.User ? "CurrentUser" : "LocalMachine";
        var registryPath = plan.Options.Scope == AndroidPathScope.User ? "Environment" : @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
        var script = "$ErrorActionPreference='Stop'; " +
                $"$key=[Microsoft.Win32.Registry]::{hive}.OpenSubKey({q(registryPath)},$true); " +
                "try { if ($null -eq $key) { throw 'PATH registry key is unavailable.' }; " +
                $"if (([string]$key.GetValue('Path','',[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)) -cne {q(plan.OriginalContent)}) {{ throw 'PATH changed; refresh the preview.' }}; " +
                $"if (($key.GetValueNames() -contains 'Path') -and $key.GetValueKind('Path').ToString() -ne {q(plan.WindowsValueKind!)}) {{ throw 'PATH registry format changed; refresh the preview.' }}; " +
                $"$key.SetValue('Path',{q(plan.UpdatedContent)},[Microsoft.Win32.RegistryValueKind]::{plan.WindowsValueKind}); " +
                "} finally { if ($null -ne $key) { $key.Dispose() } }; " +
                "Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class SherpaPathBroadcast { " +
                "[DllImport(\"user32.dll\", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageTimeout(IntPtr h, uint m, UIntPtr w, string l, uint f, uint t, out UIntPtr r); }'; " +
                "$r=[UIntPtr]::Zero; [void][SherpaPathBroadcast]::SendMessageTimeout([IntPtr]0xffff,0x1a,[UIntPtr]::Zero,'Environment',2,5000,[ref]$r)";
            var powerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            var result = await processes.ExecuteAsync(new ProcessRequest(powerShell,
                ["-NoProfile", "-NonInteractive", "-Command", script], RequiresElevation: plan.Options.Scope == AndroidPathScope.Machine), ct);
            if (!result.Success) throw new IOException($"Windows PATH update failed or was cancelled: {result.Error}");
    }

    [SupportedOSPlatform("windows")]
    private static (string Value, string Kind) ReadWindowsPath(AndroidPathScope scope)
    {
        using var key = (scope == AndroidPathScope.User ? Registry.CurrentUser : Registry.LocalMachine)
            .OpenSubKey(scope == AndroidPathScope.User ? "Environment" : @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment");
        var value = key?.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames);
        var kind = key?.GetValueNames().Contains("Path", StringComparer.OrdinalIgnoreCase) == true
            ? key.GetValueKind("Path") : RegistryValueKind.ExpandString;
        if (value is not null and not string || kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString))
            throw new InvalidOperationException("Windows PATH must be a string registry value.");
        return (value as string ?? "", kind.ToString());
    }
}
