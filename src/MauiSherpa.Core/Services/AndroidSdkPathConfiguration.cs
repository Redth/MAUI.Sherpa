using System.Text;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

internal static class AndroidSdkPathConfiguration
{
    internal const string StartMarker = "# >>> MAUI Sherpa Android SDK PATH >>>";
    internal const string EndMarker = "# <<< MAUI Sherpa Android SDK PATH <<<";

    internal static bool IsSharedLocation(string path)
    {
        var canonical = ResolveDirectory(path);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new List<string> { home };
        foreach (var variable in new[] { "LOCALAPPDATA", "APPDATA", "XDG_DATA_HOME", "XDG_CONFIG_HOME", "XDG_CACHE_HOME" })
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value && Path.IsPathFullyQualified(value))
                roots.Add(value);
        if (OperatingSystem.IsWindows())
        {
            roots.Add(Path.Combine(Path.GetPathRoot(home)!, "Users"));
            roots.Add(Path.Combine(Path.GetPathRoot(canonical)!, "Users"));
            var profiles = Path.GetDirectoryName(home);
            if (profiles != null && profiles != Path.GetPathRoot(profiles)) roots.Add(profiles);
        }
        else
            roots.AddRange(["/Users", "/System/Volumes/Data/Users", "/home", "/root", "/var/home", "/run/user", "/var/folders", "/private/var/folders", "/tmp", "/private/tmp"]);
        return !roots.Where(Path.IsPathFullyQualified).Any(root => IsWithin(canonical, ResolveDirectory(root)));
    }

    internal static string ResolveDirectory(string path)
    {
        path = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
        if (parent == null) return path;
        var resolved = Path.Combine(ResolveDirectory(parent), Path.GetFileName(Path.TrimEndingDirectorySeparator(path)));
        FileSystemInfo info = Directory.Exists(resolved) ? new DirectoryInfo(resolved) : new FileInfo(resolved);
        return info.Exists ? info.ResolveLinkTarget(true)?.FullName ?? resolved : resolved;
    }

    internal static bool IsWithin(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        root = Path.TrimEndingDirectorySeparator(root);
        return string.Equals(path, root, comparison) ||
            path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, comparison);
    }

    internal static string Quote(string value) => $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";
    internal static string PowerShellQuote(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    internal static string BuildBlock(IReadOnlyList<string> directories, AndroidPathShell shell, string newline)
    {
        if (directories.Count == 0) throw new InvalidOperationException("No installed Android tools can be added to PATH.");
        if (directories.Any(p => p.Contains('\r') || p.Contains('\n') || p.Contains(shell == AndroidPathShell.Windows ? ';' : ':')))
            throw new InvalidOperationException("An SDK tools folder contains a PATH separator or newline and cannot be added safely.");
        var lines = new List<string> { StartMarker };
        if (shell == AndroidPathShell.Fish)
        {
            // Use PATH rather than persistent fish_user_paths so replacing this block also replaces old SDK entries.
            lines.AddRange([
                "begin",
                $"    set -l _sherpa_android_dirs {string.Join(" ", directories.Select(QuoteFish))}",
                "    set -l _sherpa_android_rest",
                "    for _sherpa_android_entry in $PATH",
                "        if not contains -- $_sherpa_android_entry $_sherpa_android_dirs",
                "            set -a _sherpa_android_rest $_sherpa_android_entry",
                "        end",
                "    end",
                "    set -gx PATH $_sherpa_android_dirs $_sherpa_android_rest",
                "end"
            ]);
        }
        else
        {
            var prefix = string.Join(":", directories);
            lines.AddRange([
                $"_sherpa_android_prefix={Quote(prefix)}",
                "_sherpa_android_remaining=${PATH-}",
                "_sherpa_android_rest=",
                "_sherpa_android_separator=",
                "_sherpa_android_kept=",
                "while :; do",
                "    _sherpa_android_entry=${_sherpa_android_remaining%%:*}",
                "    case \":$_sherpa_android_prefix:\" in",
                "        *\":$_sherpa_android_entry:\"*) ;;",
                "        *) _sherpa_android_rest=\"$_sherpa_android_rest$_sherpa_android_separator$_sherpa_android_entry\"; _sherpa_android_separator=:; _sherpa_android_kept=1 ;;",
                "    esac",
                "    case $_sherpa_android_remaining in",
                "        *:*) _sherpa_android_remaining=${_sherpa_android_remaining#*:} ;;",
                "        *) break ;;",
                "    esac",
                "done",
                "if [ \"$_sherpa_android_kept\" = 1 ]; then",
                "    PATH=\"$_sherpa_android_prefix:$_sherpa_android_rest\"",
                "else",
                "    PATH=$_sherpa_android_prefix",
                "fi",
                "export PATH",
                "unset _sherpa_android_prefix _sherpa_android_remaining _sherpa_android_rest _sherpa_android_separator _sherpa_android_kept _sherpa_android_entry"
            ]);
        }
        lines.Add(EndMarker);
        return string.Join(newline, lines) + newline;
    }

    private static string QuoteFish(string value) => $"'{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal)}'";

    internal static string ReplaceBlock(string original, string block)
    {
        var start = original.IndexOf(StartMarker, StringComparison.Ordinal);
        var end = original.IndexOf(EndMarker, StringComparison.Ordinal);
        if (start < 0 && end < 0)
        {
            var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            return original + (original.Length > 0 && !original.EndsWith('\n') ? newline : "") + block;
        }
        if (start < 0 || end < start ||
            original.IndexOf(StartMarker, start + StartMarker.Length, StringComparison.Ordinal) >= 0 ||
            original.IndexOf(EndMarker, end + EndMarker.Length, StringComparison.Ordinal) >= 0 ||
            (start > 0 && original[start - 1] != '\n') ||
            (end > 0 && original[end - 1] != '\n') ||
            (original.Length > start + StartMarker.Length && original[start + StartMarker.Length] is not ('\r' or '\n')))
            throw new InvalidOperationException("The existing Sherpa PATH block is malformed. Repair its markers before applying changes.");
        var after = end + EndMarker.Length;
        if (after < original.Length && original[after] == '\r') after++;
        if (after < original.Length && original[after] == '\n') after++;
        else if (after < original.Length)
            throw new InvalidOperationException("The Sherpa PATH end marker must be on its own line.");
        return original[..start] + block + original[after..];
    }

    internal static string UpdateWindowsPath(string original, IReadOnlyList<string> directories)
    {
        BuildBlock(directories, AndroidPathShell.Windows, "\n");
        var remaining = (original.Length == 0 ? Array.Empty<string>() : original.Split(';'))
            .Where(p => !directories.Any(d => StringComparer.OrdinalIgnoreCase.Equals(
                d.TrimEnd('\\', '/'), Environment.ExpandEnvironmentVariables(p.Trim().Trim('"')).TrimEnd('\\', '/'))));
        return string.Join(";", directories.Concat(remaining));
    }

    internal static (string Content, bool Bom) DecodeProfile(byte[] bytes)
    {
        var bom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        var content = new UTF8Encoding(false, true).GetString(bytes.AsSpan(bom ? 3 : 0));
        if (content.Contains('\0')) throw new InvalidOperationException("The shell profile must be UTF-8 text; its current encoding is not supported.");
        return (content, bom);
    }
}
