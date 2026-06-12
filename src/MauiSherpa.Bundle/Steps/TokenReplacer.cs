using MauiSherpa.Bundle.Pipeline;

namespace MauiSherpa.Bundle.Steps;

/// <summary>
/// Substitutes <c>ReplaceTokens</c> into source/asset files during the build
/// step (spec §5.2). A token <c>Foo</c> replaces the literal <c>${Foo}</c>.
/// <para>
/// The spec does not enumerate which files are scanned, so the default is: all
/// text files under the project directory, excluding build output and VCS/IDE
/// folders. Only known token keys are replaced; other <c>${...}</c> occurrences
/// (e.g. shell or MSBuild syntax) are left untouched.
/// </para>
/// </summary>
public static class TokenReplacer
{
    private static readonly HashSet<string> ExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", ".idea", "node_modules", ".github",
    };

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".ico", ".icns", ".webp", ".bmp",
        ".zip", ".gz", ".tar", ".7z", ".dll", ".exe", ".so", ".dylib", ".a",
        ".aab", ".apk", ".ipa", ".pdf", ".ttf", ".otf", ".woff", ".woff2",
        ".keystore", ".jks", ".p12", ".pfx", ".mobileprovision", ".provisionprofile",
        ".pdb", ".nupkg", ".snk", ".mp4", ".mov", ".mp3", ".wav",
    };

    private const long MaxFileBytes = 5 * 1024 * 1024;

    /// <summary>Returns the number of files modified.</summary>
    public static int Apply(string projectDirectory, IReadOnlyDictionary<string, string> tokens, ISherpaLog log)
    {
        if (tokens.Count == 0)
        {
            log.Info("No replace tokens to apply.");
            return 0;
        }

        var replacements = tokens.ToDictionary(kv => "${" + kv.Key + "}", kv => kv.Value, StringComparer.Ordinal);
        var modified = 0;

        foreach (var file in EnumerateCandidateFiles(projectDirectory))
        {
            string content;
            try
            {
                if (new FileInfo(file).Length > MaxFileBytes)
                    continue;
                content = File.ReadAllText(file);
            }
            catch
            {
                continue; // unreadable / locked — skip rather than fail the build
            }

            if (content.IndexOf('\0') >= 0)
                continue; // looks binary

            var updated = content;
            var hits = 0;
            foreach (var (placeholder, value) in replacements)
            {
                if (updated.Contains(placeholder, StringComparison.Ordinal))
                {
                    updated = updated.Replace(placeholder, value, StringComparison.Ordinal);
                    hits++;
                }
            }

            if (hits > 0 && !ReferenceEquals(updated, content) && updated != content)
            {
                File.WriteAllText(file, updated);
                modified++;
                log.Info($"  tokens → {Path.GetRelativePath(projectDirectory, file)} ({hits} key(s))");
            }
        }

        log.Success($"Applied {tokens.Count} replace token(s) across {modified} file(s).");
        return modified;
    }

    private static IEnumerable<string> EnumerateCandidateFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            string[] subdirs;
            try { subdirs = Directory.GetDirectories(dir); }
            catch { continue; }

            foreach (var sub in subdirs)
                if (!ExcludedDirs.Contains(Path.GetFileName(sub)))
                    stack.Push(sub);

            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch { continue; }

            foreach (var file in files)
                if (!BinaryExtensions.Contains(Path.GetExtension(file)))
                    yield return file;
        }
    }
}
