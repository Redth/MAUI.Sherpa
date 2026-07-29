using System.Text.RegularExpressions;
using MauiSherpa.Core.Models.Profiling;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Rebuilds a <see cref="MauiProfileResult"/> from the files the MAUI CLI wrote to the
/// requested <c>--output</c> location when the CLI captured a profile but failed to
/// report it.
/// </summary>
/// <remarks>
/// Sherpa always supplies an explicit <c>--output</c> path, so the files on disk are a
/// stronger success signal than the CLI's own reporting. Preview builds of
/// <c>Microsoft.Maui.Cli</c> crash while serializing their profile result because
/// <c>MauiCliJsonContext</c> does not include <c>MauiProfileResult</c>; the trace is
/// already written by that point, so the capture must not be discarded.
/// </remarks>
public static partial class MauiProfileArtifactRecovery
{
    private const string SpeedscopeSuffix = ".speedscope.json";
    private const string NetTraceSuffix = ".nettrace";
    private const string MibcSuffix = ".mibc";

    /// <summary>
    /// Artifacts written before the run started belong to an earlier capture. The window is
    /// generous because Sherpa creates a fresh output directory for every run and file
    /// timestamp granularity varies between file systems.
    /// </summary>
    private static readonly TimeSpan WriteTimeTolerance = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Detects the known <c>Microsoft.Maui.Cli</c> defect where the CLI throws while
    /// serializing a command result because its source-generated JSON context is missing
    /// the result type.
    /// </summary>
    public static bool IsResultSerializationFailure(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return false;

        return output.Contains("JsonTypeInfo metadata for type", StringComparison.OrdinalIgnoreCase) &&
               output.Contains("JsonContext", StringComparison.OrdinalIgnoreCase);
    }

    public static MauiProfileResult? TryRecover(
        MauiProfileRequest request,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        string? processOutput = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.OutputPath))
            return null;

        string outputPath;
        string? directory;
        try
        {
            outputPath = Path.GetFullPath(request.OutputPath);
            directory = Path.GetDirectoryName(outputPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return null;

        var candidates = EnumerateCandidates(directory, startedAtUtc);
        if (candidates.Count == 0)
            return null;

        var baseName = ProfilingArtifactClassifier.GetBaseName(outputPath);
        var primary = SelectPrimary(candidates, request.Format, baseName);
        if (primary is null)
            return null;

        var rawTrace = SelectBySuffix(candidates, NetTraceSuffix, baseName);
        if (rawTrace is not null && PathsEqual(rawTrace, primary))
            rawTrace = null;

        return new MauiProfileResult
        {
            ProjectPath = request.ProjectPath,
            ProjectName = Path.GetFileNameWithoutExtension(request.ProjectPath),
            Framework = ExtractFramework(processOutput) ?? string.Empty,
            Platform = ToPlatformName(request.Platform),
            DeviceId = request.DeviceId,
            DeviceName = string.IsNullOrWhiteSpace(request.DeviceName)
                ? request.DeviceId
                : request.DeviceName,
            Configuration = request.Configuration,
            Format = DescribeFormat(primary),
            OutputPath = primary,
            RawTracePath = rawTrace,
            UsedStoppingEvent = request.Mode == MauiProfileMode.Startup && request.Duration is null,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            RecoveredFromDisk = true
        };
    }

    private static List<string> EnumerateCandidates(string directory, DateTimeOffset startedAtUtc)
    {
        var earliestWrite = startedAtUtc - WriteTimeTolerance;
        var candidates = new List<string>();

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return candidates;
        }

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            if (!fileName.EndsWith(SpeedscopeSuffix, StringComparison.OrdinalIgnoreCase) &&
                !fileName.EndsWith(NetTraceSuffix, StringComparison.OrdinalIgnoreCase) &&
                !fileName.EndsWith(MibcSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            FileInfo info;
            try
            {
                info = new FileInfo(file);
                if (!info.Exists || info.Length == 0)
                    continue;
                if (info.LastWriteTimeUtc < earliestWrite)
                    continue;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            candidates.Add(info.FullName);
        }

        return candidates;
    }

    private static string? SelectPrimary(
        List<string> candidates,
        MauiProfileOutputFormat format,
        string baseName)
    {
        // Fall back to the raw trace when the requested conversion did not produce a file:
        // a captured trace is far more useful than discarding the run.
        return format switch
        {
            MauiProfileOutputFormat.Speedscope =>
                SelectBySuffix(candidates, SpeedscopeSuffix, baseName) ??
                SelectBySuffix(candidates, NetTraceSuffix, baseName),
            MauiProfileOutputFormat.Mibc =>
                SelectBySuffix(candidates, MibcSuffix, baseName) ??
                SelectBySuffix(candidates, NetTraceSuffix, baseName),
            _ => SelectBySuffix(candidates, NetTraceSuffix, baseName)
        };
    }

    private static string? SelectBySuffix(List<string> candidates, string suffix, string baseName)
    {
        var matches = candidates
            .Where(x => Path.GetFileName(x).EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
            return null;

        var expected = matches.FirstOrDefault(x => string.Equals(
            ProfilingArtifactClassifier.GetBaseName(x),
            baseName,
            StringComparison.OrdinalIgnoreCase));

        return expected ?? matches
            .OrderByDescending(x => File.GetLastWriteTimeUtc(x))
            .First();
    }

    private static string DescribeFormat(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.EndsWith(SpeedscopeSuffix, StringComparison.OrdinalIgnoreCase))
            return "speedscope";
        if (fileName.EndsWith(MibcSuffix, StringComparison.OrdinalIgnoreCase))
            return "mibc";
        return "nettrace";
    }

    private static string ToPlatformName(ProfilingTargetPlatform platform) => platform switch
    {
        ProfilingTargetPlatform.Android => "android",
        ProfilingTargetPlatform.iOS => "ios",
        _ => platform.ToString().ToLowerInvariant()
    };

    private static string? ExtractFramework(string? processOutput)
    {
        if (string.IsNullOrWhiteSpace(processOutput))
            return null;

        var match = FrameworkRegex().Match(processOutput);
        return match.Success ? match.Value : null;
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(left, right, PathComparison);

    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    [GeneratedRegex(@"net\d+(?:\.\d+)+-[a-z][a-z0-9.]*", RegexOptions.IgnoreCase)]
    private static partial Regex FrameworkRegex();
}
