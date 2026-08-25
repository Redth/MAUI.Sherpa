using System.IO.Enumeration;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MauiSherpa.Bundles;

public sealed class BundleBuildService(IBundleProcessRunner processRunner)
{
    private const int MaxDestinationAttempts = 10_000;

    private static readonly Regex NetVersionPrefixRegex =
        new(@"^net\d+(?:\.\d+)*", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<IReadOnlyList<BundleArtifact>> BuildAsync(
        BundlePlatform platform,
        BundleBuildConfiguration configuration,
        string workspaceRoot,
        string outputDirectory,
        string? projectOverride,
        IReadOnlyDictionary<string, string> variables,
        IReadOnlyDictionary<string, string> preparationEnvironment,
        IReadOnlySet<string> secretValues,
        bool dryRun,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(preparationEnvironment);
        ArgumentNullException.ThrowIfNull(secretValues);

        var project = ResolveProject(workspaceRoot, projectOverride ?? configuration.Project);
        var targetFramework = configuration.TargetFramework ?? InferTargetFramework(project, platform);

        // Build (and validate) the full process environment before the dry-run check so that
        // configuration errors - invalid variable/property names, unresolved variable
        // references - surface during a dry run instead of only on a real build.
        var processEnvironment = BuildProcessEnvironment(preparationEnvironment, variables, configuration.Properties);

        if (dryRun)
            return [];

        var arguments = new List<string>
        {
            "publish",
            project,
            "--configuration",
            configuration.Configuration,
            "--framework",
            targetFramework,
            "--nologo"
        };
        if (!string.IsNullOrWhiteSpace(configuration.RuntimeIdentifier))
        {
            arguments.Add("--runtime");
            arguments.Add(configuration.RuntimeIdentifier);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var build = await processRunner.RunAsync(
            new BundleProcessRequest
            {
                FileName = "dotnet",
                Arguments = arguments,
                WorkingDirectory = workspaceRoot,
                Environment = processEnvironment,
                SecretValues = secretValues
            },
            progress,
            cancellationToken).ConfigureAwait(false);
        if (build.ExitCode != 0)
        {
            var details = CombineProcessOutput(build.StandardOutput, build.StandardError);
            throw new InvalidOperationException(
                $"dotnet publish failed for {platform} with exit code {build.ExitCode}: {details}");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var discovered = DiscoverArtifacts(
            workspaceRoot,
            platform,
            configuration.ArtifactGlobs);
        if (discovered.Count == 0)
            throw new InvalidOperationException($"No {platform} build artifacts were found.");

        Directory.CreateDirectory(outputDirectory);
        var persisted = new List<BundleArtifact>();
        foreach (var source in discovered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = GetUniqueDestination(outputDirectory, Path.GetFileName(source));
            File.Copy(source, destination);
            persisted.Add(new BundleArtifact
            {
                Path = destination,
                Platform = platform,
                Kind = GetArtifactKind(destination),
                // processEnvironment reflects the fully merged and overridden values (variables,
                // then configuration.Properties last), so it is the authoritative source for
                // metadata even when a value is only supplied via configuration.Properties.
                Version = processEnvironment.GetValueOrDefault("ApplicationDisplayVersion"),
                BuildNumber = processEnvironment.GetValueOrDefault("ApplicationVersion"),
                ApplicationId = processEnvironment.GetValueOrDefault("ApplicationId"),
                Sha256 = await ComputeSha256Async(destination, cancellationToken).ConfigureAwait(false)
            });
        }
        return persisted;
    }

    internal static Dictionary<string, string> BuildProcessEnvironment(
        IReadOnlyDictionary<string, string> preparationEnvironment,
        IReadOnlyDictionary<string, string> variables,
        IReadOnlyDictionary<string, string> properties)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in preparationEnvironment)
        {
            EnsureValidEnvironmentVariableName(name);
            environment[name] = value;
        }
        foreach (var (name, value) in variables)
        {
            EnsureValidEnvironmentVariableName(name);
            environment[name] = value;
        }
        foreach (var (name, value) in properties)
        {
            // Properties are documented to become MSBuild properties via valid environment
            // variable names (see docs/sherpa-bundles.md) rather than command-line -p: switches,
            // so secrets never appear on the process command line. A name that is a valid
            // environment variable but not a valid MSBuild property identifier would silently be
            // ignored by MSBuild, so require the stricter MSBuild-identifier shape here.
            EnsureValidMSBuildPropertyName(name);
            environment[name] = BundleVariableResolver.Expand(value, variables);
        }
        return environment;
    }

    private static void EnsureValidEnvironmentVariableName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(c => c == '=' || char.IsControl(c)))
            throw new BundleValidationException([$"'{name}' is not a valid environment variable name."]);
    }

    private static void EnsureValidMSBuildPropertyName(string name)
    {
        var isValid = name.Length > 0 &&
            (char.IsAsciiLetter(name[0]) || name[0] == '_') &&
            name.Skip(1).All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
        if (!isValid)
        {
            throw new BundleValidationException(
                [$"Build property '{name}' must start with a letter or underscore and contain only letters, digits, or underscores so it is imported as an MSBuild property."]);
        }
    }

    private static string CombineProcessOutput(string standardOutput, string standardError)
    {
        var segments = new[] { standardOutput, standardError }
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();
        return segments.Length > 0
            ? string.Join(Environment.NewLine, segments)
            : "The process produced no output.";
    }

    internal static string ResolveProject(string workspaceRoot, string? configuredProject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var root = Path.GetFullPath(workspaceRoot);
        if (!string.IsNullOrWhiteSpace(configuredProject))
        {
            if (!BundleValidator.IsSafeRelativePath(configuredProject))
                throw new BundleValidationException([$"Project path '{configuredProject}' must be a safe relative path."]);
            var path = Path.GetFullPath(Path.Combine(root, configuredProject));
            if (!IsWithinRoot(path, root))
                throw new BundleValidationException([$"Project path '{configuredProject}' must stay within the workspace."]);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Project '{configuredProject}' was not found.", path);
            return path;
        }

        var projects = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !HasPathSegmentRelativeTo(root, path, "bin", "obj"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        return projects.Length switch
        {
            1 => projects[0],
            0 => throw new InvalidOperationException("No project was found. Specify one with --project or in the bundle."),
            _ => throw new InvalidOperationException("Multiple projects were found. Specify one with --project or in the bundle.")
        };
    }

    private static bool IsWithinRoot(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) &&
            relative != ".." &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks whether <paramref name="path"/> contains any of <paramref name="segments"/> as a
    /// directory segment *relative to* <paramref name="root"/>. Matching is deliberately scoped to
    /// the path underneath the search root rather than the full absolute path, so a workspace or
    /// staging directory that itself happens to live under a "bin"/"obj"/"artifacts"-named
    /// directory (for example a build output folder on a CI agent) cannot make every candidate
    /// file match, or fail to match, this filter.
    /// </summary>
    private static bool HasPathSegmentRelativeTo(string root, string path, params ReadOnlySpan<string> segments)
    {
        var relative = Path.GetRelativePath(root, path);
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            foreach (var candidate in segments)
            {
                if (string.Equals(segment, candidate, StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }

    internal static string InferTargetFramework(string projectPath, BundlePlatform platform)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        var document = XDocument.Load(projectPath);
        var frameworks = document.Descendants()
            .Where(element => element.Name.LocalName is "TargetFramework" or "TargetFrameworks")
            .SelectMany(element => element.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();
        var suffix = platform switch
        {
            BundlePlatform.Android => "-android",
            BundlePlatform.Ios => "-ios",
            BundlePlatform.MacOS => "-macos",
            BundlePlatform.MacCatalyst => "-maccatalyst",
            BundlePlatform.Windows => "-windows",
            _ => throw new ArgumentOutOfRangeException(nameof(platform))
        };

        var match = frameworks.FirstOrDefault(framework => framework.Contains(suffix, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            return match;

        // No declared framework already targets this platform. Derive the "netX.Y" version
        // prefix from whatever framework(s) the project does declare instead of assuming
        // net10.0, so multi-targeting onto a new platform stays on the project's actual SDK
        // version. Fall back to net10.0 only when no framework is declared at all.
        var baseVersion = frameworks
            .Select(framework => NetVersionPrefixRegex.Match(framework))
            .FirstOrDefault(candidate => candidate.Success)
            ?.Value ?? "net10.0";
        return $"{baseVersion}{suffix}";
    }

    private static List<string> DiscoverArtifacts(
        string workspaceRoot,
        BundlePlatform platform,
        IReadOnlyList<string> configuredGlobs)
    {
        var patterns = configuredGlobs.Count > 0
            ? configuredGlobs
            : platform switch
            {
                BundlePlatform.Android => ["*.aab", "*.apk"],
                BundlePlatform.Ios => ["*.ipa"],
                BundlePlatform.MacOS => ["*.pkg", "*.dmg", "*.zip"],
                BundlePlatform.MacCatalyst => ["*.pkg", "*.dmg", "*.zip"],
                BundlePlatform.Windows => ["*.msix", "*.msixbundle", "*.appx", "*.appxbundle"],
                _ => []
            };

        var root = Path.GetFullPath(workspaceRoot);
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => HasPathSegmentRelativeTo(root, path, "bin", "artifacts"))
            .Where(path => patterns.Any(pattern =>
                FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(path), ignoreCase: true)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            // Last-write-time is the primary signal (the newest matching artifact wins the
            // canonical destination name), but mtime resolution can tie on some filesystems, so
            // an ordinal path comparison breaks ties deterministically instead of relying on
            // whatever order the OS happens to enumerate files in.
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ThenBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    private static string GetUniqueDestination(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate))
            return candidate;

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var suffix = 1; suffix <= MaxDestinationAttempts; suffix++)
        {
            candidate = Path.Combine(directory, $"{nameWithoutExtension}-{suffix}{extension}");
            if (!File.Exists(candidate))
                return candidate;
        }

        throw new InvalidOperationException(
            $"Could not find a unique destination for artifact '{fileName}' after {MaxDestinationAttempts} attempts.");
    }

    private static string GetArtifactKind(string path) =>
        Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }
}
