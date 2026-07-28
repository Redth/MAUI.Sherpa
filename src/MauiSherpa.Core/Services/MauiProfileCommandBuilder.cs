using System.Globalization;
using MauiSherpa.Core.Models.Profiling;

namespace MauiSherpa.Core.Services;

public static class MauiProfileCommandBuilder
{
    public const string StartupProviderName = "Microsoft.Maui.ProfilingHelper";
    public const string StartupEventName = "StartupComplete";

    public static string[] BuildArguments(MauiProfileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var arguments = new List<string>
        {
            "profile",
            request.Mode == MauiProfileMode.Startup ? "startup" : "manual",
            "--project",
            request.ProjectPath,
            "--platform",
            ToPlatformArgument(request.Platform),
            "--device",
            request.DeviceId,
            "--format",
            ToFormatArgument(request.Format),
            "--configuration",
            request.Configuration,
            "--output",
            request.OutputPath
        };

        if (request.Mode == MauiProfileMode.Startup)
        {
            if (request.Duration is { } duration)
            {
                arguments.Add("--duration");
                arguments.Add(FormatDuration(duration));
            }
            else
            {
                arguments.Add("--stopping-event-provider-name");
                arguments.Add(StartupProviderName);
                arguments.Add("--stopping-event-event-name");
                arguments.Add(StartupEventName);
            }
        }

        if (!string.IsNullOrWhiteSpace(request.TraceProfile))
        {
            arguments.Add("--trace-profile");
            arguments.Add(request.TraceProfile.Trim());
        }

        if (request.NoBuild)
            arguments.Add("--no-build");

        arguments.Add("--json");
        arguments.Add("--ci");

        return [.. arguments];
    }

    public static string FormatForDisplay(string executablePath, MauiProfileRequest request)
    {
        var arguments = BuildArguments(request);
        return string.Join(' ', [Quote(executablePath), .. arguments.Select(Quote)]);
    }

    private static void Validate(MauiProfileRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectPath))
            throw new ArgumentException("A MAUI project path is required.", nameof(request));
        if (request.Platform is not (ProfilingTargetPlatform.Android or ProfilingTargetPlatform.iOS))
            throw new ArgumentException("MAUI CLI profiling currently supports Android and iOS only.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.DeviceId))
            throw new ArgumentException("A running device or simulator is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.OutputPath))
            throw new ArgumentException("An output path is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Configuration))
            throw new ArgumentException("A build configuration is required.", nameof(request));
        if (request.Duration is { } duration && duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(request), "Duration must be greater than zero.");
        if (request.Mode == MauiProfileMode.Interaction && request.Duration is not null)
            throw new ArgumentException("Duration is only supported for startup profiling.", nameof(request));
    }

    private static string ToPlatformArgument(ProfilingTargetPlatform platform) => platform switch
    {
        ProfilingTargetPlatform.Android => "android",
        ProfilingTargetPlatform.iOS => "ios",
        _ => throw new ArgumentOutOfRangeException(nameof(platform))
    };

    private static string ToFormatArgument(MauiProfileOutputFormat format) => format switch
    {
        MauiProfileOutputFormat.NetTrace => "nettrace",
        MauiProfileOutputFormat.Speedscope => "speedscope",
        MauiProfileOutputFormat.Mibc => "mibc",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    private static string FormatDuration(TimeSpan duration)
    {
        var totalHours = (int)duration.TotalHours;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{totalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}");
    }

    private static string Quote(string value)
    {
        if (value.Length > 0 && !value.Any(char.IsWhiteSpace) && !value.Contains('"'))
            return value;

        return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }
}
