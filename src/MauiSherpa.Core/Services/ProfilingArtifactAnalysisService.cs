using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;

namespace MauiSherpa.Core.Services;

public class ProfilingArtifactAnalysisService : IProfilingArtifactAnalysisService
{
    private static readonly Regex HexValueRegex = new(@"0x[0-9a-fA-F]+", RegexOptions.Compiled);
    private static readonly Regex IntegerRegex = new(@"\b\d+\b", RegexOptions.Compiled);
    private static readonly Regex MultiWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex TimestampPrefixRegex = new(
        @"^\s*(?:\[[^\]]+\]\s*)?(?<stamp>\d{4}-\d{2}-\d{2}[T ][0-9:\.,+\-Z]+|\d{2}:\d{2}:\d{2}(?:[\.,]\d+)?)\s*",
        RegexOptions.Compiled);

    private readonly IProfilingArtifactLibraryService _profilingArtifactLibraryService;
    private readonly ILoggingService _loggingService;

    public ProfilingArtifactAnalysisService(
        IProfilingArtifactLibraryService profilingArtifactLibraryService,
        ILoggingService loggingService)
    {
        _profilingArtifactLibraryService = profilingArtifactLibraryService;
        _loggingService = loggingService;
    }

    public async Task<ProfilingArtifactAnalysisResult> AnalyzeArtifactAsync(string artifactId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(artifactId))
        {
            throw new ArgumentException("A profiling artifact id is required.", nameof(artifactId));
        }

        var entry = await _profilingArtifactLibraryService.GetArtifactAsync(artifactId, ct);
        if (entry is null)
        {
            return new ProfilingArtifactAnalysisResult(
                null,
                $"Profiling artifact '{artifactId}' was not found in the artifact library.");
        }

        var analysis = await AnalyzeEntryAsync(entry, ct);
        return new ProfilingArtifactAnalysisResult(analysis);
    }

    public async Task<IReadOnlyList<ProfilingArtifactAnalysis>> AnalyzeArtifactsAsync(
        ProfilingArtifactLibraryQuery? query = null,
        CancellationToken ct = default)
    {
        var artifacts = await _profilingArtifactLibraryService.GetArtifactsAsync(query, ct);
        var analyses = new List<ProfilingArtifactAnalysis>(artifacts.Count);

        foreach (var artifact in artifacts)
        {
            ct.ThrowIfCancellationRequested();
            analyses.Add(await AnalyzeEntryAsync(artifact, ct));
        }

        return analyses;
    }

    private async Task<ProfilingArtifactAnalysis> AnalyzeEntryAsync(
        ProfilingArtifactLibraryEntry artifact,
        CancellationToken ct)
    {
        var artifactPath = await _profilingArtifactLibraryService.GetArtifactPathAsync(artifact.Metadata.Id, ct);
        var artifactExists = !string.IsNullOrWhiteSpace(artifactPath) && File.Exists(artifactPath);

        if (!artifactExists)
        {
            return CreateMetadataAnalysis(
                artifact.Metadata,
                artifactPath,
                artifactExists: false,
                summary: $"The artifact file for {artifact.Metadata.DisplayName} is no longer available on disk.",
                insights:
                [
                    new ProfilingAnalysisInsight(
                        ProfilingAnalysisInsightSeverity.Warning,
                        "Artifact file is missing",
                        "The artifact metadata is still available, but the referenced file could not be found.")
                ]);
        }

        try
        {
            if (IsJsonArtifact(artifact.Metadata, artifactPath!))
            {
                using var stream = File.OpenRead(artifactPath!);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

                if (LooksLikeSpeedscope(document.RootElement, artifact.Metadata))
                {
                    var speedscopeAnalysis = AnalyzeSpeedscopeArtifact(artifact.Metadata, artifactPath!, document.RootElement);
                    if (speedscopeAnalysis is not null)
                    {
                        return speedscopeAnalysis;
                    }
                }

                return AnalyzeJsonArtifact(artifact.Metadata, artifactPath!, document.RootElement);
            }

            if (IsLogArtifact(artifact.Metadata, artifactPath!))
            {
                return AnalyzeLogArtifact(artifact.Metadata, artifactPath!);
            }
        }
        catch (JsonException ex)
        {
            _loggingService.LogDebug($"Portable profiling analysis could not parse JSON for '{artifact.Metadata.Id}': {ex.Message}");
            return CreateMetadataAnalysis(
                artifact.Metadata,
                artifactPath,
                artifactExists: true,
                summary: $"Sherpa found {artifact.Metadata.DisplayName}, but the JSON payload could not be analyzed as a portable summary.",
                notes: ["The artifact may require a specialized viewer or exporter to inspect in detail."],
                insights:
                [
                    new ProfilingAnalysisInsight(
                        ProfilingAnalysisInsightSeverity.Warning,
                        "JSON artifact could not be parsed",
                        ex.Message)
                ]);
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Failed to analyze profiling artifact '{artifact.Metadata.Id}': {ex.Message}", ex);
            return CreateMetadataAnalysis(
                artifact.Metadata,
                artifactPath,
                artifactExists: true,
                summary: $"Sherpa found {artifact.Metadata.DisplayName}, but portable analysis fell back to metadata because the artifact could not be parsed.",
                insights:
                [
                    new ProfilingAnalysisInsight(
                        ProfilingAnalysisInsightSeverity.Warning,
                        "Portable analysis failed",
                        ex.Message)
                ]);
        }

        return CreateMetadataAnalysis(
            artifact.Metadata,
            artifactPath,
            artifactExists: true,
            summary: CreateMetadataSummary(artifact.Metadata),
            notes: [CreateMetadataNote(artifact.Metadata)]);
    }

    private static ProfilingArtifactAnalysis? AnalyzeSpeedscopeArtifact(
        ProfilingArtifactMetadata metadata,
        string artifactPath,
        JsonElement root)
    {
        if (!TryGetProperty(root, "profiles", out var profilesElement) || profilesElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        if (!TryGetProperty(root, "shared", out var sharedElement) ||
            !TryGetProperty(sharedElement, "frames", out var framesElement) ||
            framesElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var frames = framesElement
            .EnumerateArray()
            .Select(ParseFrame)
            .ToArray();

        var aggregate = new SpeedscopeAggregate();
        foreach (var profile in profilesElement.EnumerateArray())
        {
            var type = GetString(profile, "type");
            if (string.Equals(type, "sampled", StringComparison.OrdinalIgnoreCase))
            {
                AnalyzeSampledProfile(profile, aggregate);
            }
            else if (string.Equals(type, "evented", StringComparison.OrdinalIgnoreCase))
            {
                AnalyzeEventedProfile(profile, aggregate);
            }
            else if (!string.IsNullOrWhiteSpace(type))
            {
                aggregate.Notes.Add($"Skipped unsupported speedscope profile type '{type}'.");
            }
        }

        if (aggregate.ProfileCount == 0)
        {
            return null;
        }

        var hotspots = aggregate.InclusiveWeights
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => frames[pair.Key].Name, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(pair =>
            {
                var frame = frames[pair.Key];
                var percent = aggregate.TotalReferenceWeight <= 0
                    ? 0
                    : Math.Round((pair.Value / aggregate.TotalReferenceWeight) * 100, 1);
                var location = frame.File is null
                    ? null
                    : frame.Line.HasValue
                        ? $"{frame.File}:{frame.Line.Value}"
                        : frame.File;

                return new ProfilingAnalysisHotspot(
                    frame.Name,
                    FormatReferenceValue(pair.Value, aggregate),
                    percent,
                    location);
            })
            .ToArray();

        var metrics = CreateBaseMetrics(metadata);
        metrics.Add(new ProfilingAnalysisMetric("profileCount", "Profiles", aggregate.ProfileCount.ToString(CultureInfo.InvariantCulture), aggregate.ProfileCount));
        metrics.Add(new ProfilingAnalysisMetric(
            "samples",
            aggregate.EventedProfileCount > 0 && aggregate.SampledProfileCount == 0 ? "Events" : "Samples",
            aggregate.ReferenceEventCount.ToString(CultureInfo.InvariantCulture),
            aggregate.ReferenceEventCount));
        metrics.Add(new ProfilingAnalysisMetric("frames", "Observed frames", aggregate.ObservedFrames.Count.ToString(CultureInfo.InvariantCulture), aggregate.ObservedFrames.Count));

        if (aggregate.TotalDurationMs > 0)
        {
            metrics.Add(new ProfilingAnalysisMetric(
                "durationMs",
                "Duration",
                $"{aggregate.TotalDurationMs:0.##} ms",
                aggregate.TotalDurationMs,
                "ms"));
        }

        if (hotspots.Length > 0)
        {
            metrics.Add(new ProfilingAnalysisMetric(
                "topHotspotPercent",
                "Top hotspot share",
                $"{hotspots[0].PercentOfTrace:0.#}%",
                hotspots[0].PercentOfTrace,
                "%"));
        }

        var insights = new List<ProfilingAnalysisInsight>();
        if (hotspots.Length > 0 && hotspots[0].PercentOfTrace >= 45)
        {
            insights.Add(new ProfilingAnalysisInsight(
                ProfilingAnalysisInsightSeverity.Warning,
                "Single hotspot dominates the trace",
                $"{hotspots[0].Name} accounts for {hotspots[0].PercentOfTrace:0.#}% of the analyzed trace."));
        }

        if (aggregate.SampledProfileCount > 0 && aggregate.ReferenceEventCount < 25)
        {
            insights.Add(new ProfilingAnalysisInsight(
                ProfilingAnalysisInsightSeverity.Info,
                "Low sample count",
                "The sampled trace has limited coverage, so hotspot rankings may shift with a longer capture."));
        }

        if (aggregate.ProfileCount > 1)
        {
            insights.Add(new ProfilingAnalysisInsight(
                ProfilingAnalysisInsightSeverity.Info,
                "Multiple profiles analyzed",
                $"The artifact contains {aggregate.ProfileCount} profiles. Hotspots are aggregated across them."));
        }

        if (aggregate.UsesSampleWeights)
        {
            aggregate.Notes.Add("Hotspot percentages are based on inclusive weighted samples exported in the speedscope trace.");
        }
        else if (aggregate.SampledProfileCount > 0)
        {
            aggregate.Notes.Add("Hotspot percentages are based on how often a frame appears in sampled stacks, not exact CPU time.");
        }

        var summary = hotspots.Length > 0
            ? $"Trace capture spans about {FormatDurationSummary(aggregate.TotalDurationMs)} with {hotspots[0].Name} as the top hotspot at {hotspots[0].PercentOfTrace:0.#}% of the analyzed trace."
            : $"Trace capture includes {aggregate.ProfileCount} profile(s) and {aggregate.ObservedFrames.Count} observed frame(s).";

        return new ProfilingArtifactAnalysis(
            metadata,
            artifactPath,
            ArtifactExists: true,
            Kind: ProfilingAnalysisKind.Speedscope,
            Summary: summary,
            Metrics: metrics,
            Hotspots: hotspots,
            Insights: insights,
            Notes: aggregate.Notes);
    }

    private static ProfilingArtifactAnalysis AnalyzeLogArtifact(
        ProfilingArtifactMetadata metadata,
        string artifactPath)
    {
        var lineCount = 0;
        var warningCount = 0;
        var errorCount = 0;
        var exceptionCount = 0;
        DateTimeOffset? firstTimestamp = null;
        DateTimeOffset? lastTimestamp = null;
        var frequentLines = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in File.ReadLines(artifactPath))
        {
            lineCount++;
            var normalized = NormalizeLogMessage(line);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                frequentLines[normalized] = frequentLines.TryGetValue(normalized, out var count) ? count + 1 : 1;
            }

            var lowerLine = line.ToLowerInvariant();
            if (lowerLine.Contains("exception", StringComparison.Ordinal))
            {
                exceptionCount++;
            }

            if (lowerLine.Contains("error", StringComparison.Ordinal) ||
                lowerLine.Contains("fatal", StringComparison.Ordinal))
            {
                errorCount++;
            }
            else if (lowerLine.Contains("warn", StringComparison.Ordinal))
            {
                warningCount++;
            }

            var timestamp = TryParseLogTimestamp(line);
            if (timestamp is not null)
            {
                firstTimestamp ??= timestamp;
                lastTimestamp = timestamp;
            }
        }

        var hotspots = frequentLines
            .Where(pair => pair.Value > 1)
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(pair => new ProfilingAnalysisHotspot(
                pair.Key,
                $"{pair.Value} occurrence{(pair.Value == 1 ? string.Empty : "s")}",
                lineCount == 0 ? 0 : Math.Round((pair.Value / (double)lineCount) * 100, 1)))
            .ToArray();

        var metrics = CreateBaseMetrics(metadata);
        metrics.Add(new ProfilingAnalysisMetric("lineCount", "Log lines", lineCount.ToString(CultureInfo.InvariantCulture), lineCount));
        metrics.Add(new ProfilingAnalysisMetric("warningCount", "Warnings", warningCount.ToString(CultureInfo.InvariantCulture), warningCount));
        metrics.Add(new ProfilingAnalysisMetric("errorCount", "Errors", errorCount.ToString(CultureInfo.InvariantCulture), errorCount));
        metrics.Add(new ProfilingAnalysisMetric("exceptionCount", "Exception mentions", exceptionCount.ToString(CultureInfo.InvariantCulture), exceptionCount));

        var notes = new List<string>();
        if (firstTimestamp is not null && lastTimestamp is not null && lastTimestamp >= firstTimestamp)
        {
            var duration = lastTimestamp.Value - firstTimestamp.Value;
            metrics.Add(new ProfilingAnalysisMetric(
                "durationMs",
                "Duration",
                $"{duration.TotalMilliseconds:0.##} ms",
                duration.TotalMilliseconds,
                "ms"));
        }
        else
        {
            notes.Add("No parseable timestamps were found, so Sherpa could not estimate log duration.");
        }

        var insights = new List<ProfilingAnalysisInsight>();
        if (errorCount > 0)
        {
            insights.Add(new ProfilingAnalysisInsight(
                exceptionCount > 0 ? ProfilingAnalysisInsightSeverity.Critical : ProfilingAnalysisInsightSeverity.Warning,
                "Errors detected in captured logs",
                $"The artifact contains {errorCount} error-level line(s) and {exceptionCount} exception mention(s)."));
        }
        else if (warningCount > 0)
        {
            insights.Add(new ProfilingAnalysisInsight(
                ProfilingAnalysisInsightSeverity.Info,
                "Warnings detected in captured logs",
                $"The artifact contains {warningCount} warning-level line(s)."));
        }

        if (hotspots.Length > 0)
        {
            insights.Add(new ProfilingAnalysisInsight(
                ProfilingAnalysisInsightSeverity.Info,
                "Recurring log lines found",
                $"The most frequent recurring line appears {hotspots[0].Value} in the captured logs."));
        }

        var summary = $"Parsed {lineCount} log line(s) with {warningCount} warning(s), {errorCount} error(s), and {exceptionCount} exception mention(s).";

        return new ProfilingArtifactAnalysis(
            metadata,
            artifactPath,
            ArtifactExists: true,
            Kind: ProfilingAnalysisKind.Logs,
            Summary: summary,
            Metrics: metrics,
            Hotspots: hotspots,
            Insights: insights,
            Notes: notes);
    }

    private static ProfilingArtifactAnalysis AnalyzeJsonArtifact(
        ProfilingArtifactMetadata metadata,
        string artifactPath,
        JsonElement root)
    {
        var metrics = CreateBaseMetrics(metadata);
        var hotspots = new List<ProfilingAnalysisHotspot>();
        var insights = new List<ProfilingAnalysisInsight>();
        var notes = new List<string>();
        string summary;

        switch (root.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var properties = root.EnumerateObject().ToArray();
                metrics.Add(new ProfilingAnalysisMetric(
                    "propertyCount",
                    "Top-level properties",
                    properties.Length.ToString(CultureInfo.InvariantCulture),
                    properties.Length));

                var arrayCount = 0;
                foreach (var property in properties)
                {
                    switch (property.Value.ValueKind)
                    {
                        case JsonValueKind.Array:
                            arrayCount++;
                            hotspots.Add(new ProfilingAnalysisHotspot(
                                ToDisplayLabel(property.Name),
                                $"{property.Value.GetArrayLength()} item(s)",
                                0));
                            break;
                        case JsonValueKind.Number when property.Value.TryGetDouble(out var numericValue):
                            metrics.Add(new ProfilingAnalysisMetric(
                                property.Name,
                                ToDisplayLabel(property.Name),
                                numericValue.ToString("0.##", CultureInfo.InvariantCulture),
                                numericValue));
                            if (IsAlertMetric(property.Name, numericValue))
                            {
                                insights.Add(new ProfilingAnalysisInsight(
                                    ProfilingAnalysisInsightSeverity.Warning,
                                    $"{ToDisplayLabel(property.Name)} is elevated",
                                    $"The JSON artifact reports {numericValue:0.##} for {ToDisplayLabel(property.Name)}."));
                            }
                            break;
                        case JsonValueKind.True:
                        case JsonValueKind.False:
                            metrics.Add(new ProfilingAnalysisMetric(
                                property.Name,
                                ToDisplayLabel(property.Name),
                                property.Value.GetBoolean() ? "True" : "False"));
                            break;
                    }
                }

                summary = arrayCount > 0
                    ? $"Parsed a structured JSON artifact with {properties.Length} top-level properties and {arrayCount} collection(s)."
                    : $"Parsed a structured JSON artifact with {properties.Length} top-level properties.";
                break;
            }
            case JsonValueKind.Array:
            {
                var count = root.GetArrayLength();
                metrics.Add(new ProfilingAnalysisMetric("itemCount", "Items", count.ToString(CultureInfo.InvariantCulture), count));
                summary = $"Parsed a JSON artifact containing {count} top-level item(s).";
                break;
            }
            default:
                summary = "Parsed a JSON artifact, but it did not contain an object or array that Sherpa could summarize in a structured way.";
                notes.Add("Consider exporting the artifact in a richer report format for deeper analysis.");
                break;
        }

        if (hotspots.Count > 5)
        {
            hotspots = hotspots.Take(5).ToList();
        }

        return new ProfilingArtifactAnalysis(
            metadata,
            artifactPath,
            ArtifactExists: true,
            Kind: ProfilingAnalysisKind.Json,
            Summary: summary,
            Metrics: metrics,
            Hotspots: hotspots,
            Insights: insights,
            Notes: notes);
    }

    private static void AnalyzeSampledProfile(JsonElement profile, SpeedscopeAggregate aggregate)
    {
        aggregate.ProfileCount++;
        aggregate.SampledProfileCount++;

        var unit = GetString(profile, "unit");
        var startValue = TryGetDouble(profile, "startValue");
        var endValue = TryGetDouble(profile, "endValue");
        if (startValue.HasValue && endValue.HasValue && endValue.Value >= startValue.Value)
        {
            aggregate.TotalDurationMs += ConvertToMilliseconds(endValue.Value - startValue.Value, unit);
        }

        if (!TryGetProperty(profile, "samples", out var samplesElement) || samplesElement.ValueKind != JsonValueKind.Array)
        {
            aggregate.Notes.Add("A sampled profile was missing the samples array.");
            return;
        }

        var weights = Array.Empty<double>();
        if (TryGetProperty(profile, "weights", out var weightsElement) && weightsElement.ValueKind == JsonValueKind.Array)
        {
            weights = weightsElement.EnumerateArray()
                .Select(weight => weight.ValueKind == JsonValueKind.Number && weight.TryGetDouble(out var value) ? value : 1d)
                .ToArray();
            aggregate.UsesSampleWeights = true;
        }

        var referenceMode = weights.Length > 0 && CanConvertToMilliseconds(unit)
            ? SpeedscopeReferenceMode.DurationMs
            : SpeedscopeReferenceMode.SampleWeight;
        aggregate.SetReferenceMode(referenceMode);

        var index = 0;
        foreach (var sample in samplesElement.EnumerateArray())
        {
            var weight = index < weights.Length ? weights[index] : 1d;
            var normalizedWeight = referenceMode == SpeedscopeReferenceMode.DurationMs
                ? ConvertToMilliseconds(weight, unit)
                : weight;

            aggregate.TotalReferenceWeight += normalizedWeight;
            aggregate.ReferenceEventCount++;

            var uniqueFrames = new HashSet<int>();
            foreach (var frameValue in sample.EnumerateArray())
            {
                var frameIndex = TryGetInt32(frameValue);
                if (!frameIndex.HasValue)
                {
                    continue;
                }

                aggregate.ObservedFrames.Add(frameIndex.Value);
                uniqueFrames.Add(frameIndex.Value);
            }

            foreach (var frameIndex in uniqueFrames)
            {
                aggregate.InclusiveWeights[frameIndex] = aggregate.InclusiveWeights.TryGetValue(frameIndex, out var existing)
                    ? existing + normalizedWeight
                    : normalizedWeight;
            }

            index++;
        }
    }

    private static void AnalyzeEventedProfile(JsonElement profile, SpeedscopeAggregate aggregate)
    {
        aggregate.ProfileCount++;
        aggregate.EventedProfileCount++;

        var unit = GetString(profile, "unit");
        var startValue = TryGetDouble(profile, "startValue");
        var endValue = TryGetDouble(profile, "endValue");
        var durationMs = startValue.HasValue && endValue.HasValue && endValue.Value >= startValue.Value
            ? ConvertToMilliseconds(endValue.Value - startValue.Value, unit)
            : 0d;
        if (durationMs > 0)
        {
            aggregate.TotalDurationMs += durationMs;
        }

        aggregate.SetReferenceMode(SpeedscopeReferenceMode.DurationMs);

        if (!TryGetProperty(profile, "events", out var eventsElement) || eventsElement.ValueKind != JsonValueKind.Array)
        {
            aggregate.Notes.Add("An evented profile was missing the events array.");
            return;
        }

        var stack = new Stack<(int FrameIndex, double StartedAt)>();
        var minTimestamp = double.MaxValue;
        var maxTimestamp = double.MinValue;

        foreach (var @event in eventsElement.EnumerateArray())
        {
            aggregate.ReferenceEventCount++;

            var eventType = GetString(@event, "type");
            var eventAt = TryGetDouble(@event, "at");
            var frameIndex = TryGetInt32(@event, "frame");
            if (eventAt is null || frameIndex is null)
            {
                continue;
            }

            minTimestamp = Math.Min(minTimestamp, eventAt.Value);
            maxTimestamp = Math.Max(maxTimestamp, eventAt.Value);
            aggregate.ObservedFrames.Add(frameIndex.Value);

            if (string.Equals(eventType, "O", StringComparison.OrdinalIgnoreCase))
            {
                stack.Push((frameIndex.Value, eventAt.Value));
                continue;
            }

            if (!string.Equals(eventType, "C", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryPopMatchingFrame(stack, frameIndex.Value, out var openedFrame))
            {
                continue;
            }

            var duration = Math.Max(0, eventAt.Value - openedFrame.StartedAt);
            var normalizedDuration = ConvertToMilliseconds(duration, unit);
            aggregate.InclusiveWeights[frameIndex.Value] = aggregate.InclusiveWeights.TryGetValue(frameIndex.Value, out var existing)
                ? existing + normalizedDuration
                : normalizedDuration;
        }

        if (durationMs <= 0 && minTimestamp != double.MaxValue && maxTimestamp >= minTimestamp)
        {
            aggregate.TotalDurationMs += ConvertToMilliseconds(maxTimestamp - minTimestamp, unit);
        }

        aggregate.TotalReferenceWeight += durationMs > 0
            ? durationMs
            : minTimestamp != double.MaxValue && maxTimestamp >= minTimestamp
                ? ConvertToMilliseconds(maxTimestamp - minTimestamp, unit)
                : 0;
    }

    private static ProfilingArtifactAnalysis CreateMetadataAnalysis(
        ProfilingArtifactMetadata metadata,
        string? artifactPath,
        bool artifactExists,
        string summary,
        IReadOnlyList<string>? notes = null,
        IReadOnlyList<ProfilingAnalysisInsight>? insights = null)
    {
        return new ProfilingArtifactAnalysis(
            metadata,
            artifactPath,
            artifactExists,
            ProfilingAnalysisKind.Metadata,
            summary,
            CreateBaseMetrics(metadata),
            Array.Empty<ProfilingAnalysisHotspot>(),
            insights ?? Array.Empty<ProfilingAnalysisInsight>(),
            notes ?? Array.Empty<string>());
    }

    private static List<ProfilingAnalysisMetric> CreateBaseMetrics(ProfilingArtifactMetadata metadata)
    {
        var metrics = new List<ProfilingAnalysisMetric>();

        if (metadata.SizeBytes.HasValue)
        {
            metrics.Add(new ProfilingAnalysisMetric(
                "sizeBytes",
                "Size",
                FormatBytes(metadata.SizeBytes.Value),
                metadata.SizeBytes.Value,
                "bytes"));
        }

        if (metadata.Properties?.TryGetValue("targetPlatform", out var targetPlatform) == true)
        {
            metrics.Add(new ProfilingAnalysisMetric("targetPlatform", "Platform", targetPlatform));
        }

        if (metadata.Properties?.TryGetValue("scenario", out var scenario) == true)
        {
            metrics.Add(new ProfilingAnalysisMetric("scenario", "Scenario", scenario));
        }

        if (metadata.Properties?.TryGetValue("category", out var category) == true)
        {
            metrics.Add(new ProfilingAnalysisMetric("category", "Category", category));
        }

        return metrics;
    }

    private static bool LooksLikeSpeedscope(JsonElement root, ProfilingArtifactMetadata metadata)
    {
        if (metadata.Kind == ProfilingArtifactKind.Trace)
        {
            return true;
        }

        return TryGetProperty(root, "$schema", out var schemaElement) &&
               schemaElement.ValueKind == JsonValueKind.String &&
               schemaElement.GetString()?.Contains("speedscope", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsJsonArtifact(ProfilingArtifactMetadata metadata, string artifactPath)
        => metadata.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
           artifactPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    private static bool IsLogArtifact(ProfilingArtifactMetadata metadata, string artifactPath)
        => metadata.Kind == ProfilingArtifactKind.Logs ||
           metadata.ContentType.Contains("text", StringComparison.OrdinalIgnoreCase) ||
           artifactPath.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
           artifactPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string? GetString(JsonElement element, string propertyName)
        => TryGetProperty(element, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? TryGetDouble(JsonElement element, string propertyName)
        => TryGetProperty(element, propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var result)
            ? result
            : null;

    private static int? TryGetInt32(JsonElement element, string propertyName)
        => TryGetProperty(element, propertyName, out var value) ? TryGetInt32(value) : null;

    private static int? TryGetInt32(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value))
        {
            return value;
        }

        return null;
    }

    private static ParsedSpeedscopeFrame ParseFrame(JsonElement frame)
    {
        var name = GetString(frame, "name");
        return new ParsedSpeedscopeFrame(
            string.IsNullOrWhiteSpace(name) ? "Unknown frame" : name,
            GetString(frame, "file"),
            TryGetInt32(frame, "line"));
    }

    private static string FormatReferenceValue(double value, SpeedscopeAggregate aggregate)
    {
        return aggregate.ReferenceMode switch
        {
            SpeedscopeReferenceMode.DurationMs => $"{value:0.##} ms",
            SpeedscopeReferenceMode.SampleWeight => $"{value:0.#} weighted samples",
            _ => $"{value:0.##}"
        };
    }

    private static string FormatDurationSummary(double durationMs)
        => durationMs > 0 ? $"{durationMs:0.##} ms" : "an unknown duration";

    private static bool CanConvertToMilliseconds(string? unit)
        => NormalizeUnit(unit) is "nanoseconds" or "microseconds" or "milliseconds" or "seconds";

    private static double ConvertToMilliseconds(double value, string? unit)
    {
        return NormalizeUnit(unit) switch
        {
            "nanoseconds" => value / 1_000_000d,
            "microseconds" => value / 1_000d,
            "milliseconds" => value,
            "seconds" => value * 1_000d,
            _ => value
        };
    }

    private static string NormalizeUnit(string? unit)
    {
        return unit?.Trim().ToLowerInvariant() switch
        {
            "ns" => "nanoseconds",
            "nanosecond" => "nanoseconds",
            "nanoseconds" => "nanoseconds",
            "us" => "microseconds",
            "microsecond" => "microseconds",
            "microseconds" => "microseconds",
            "ms" => "milliseconds",
            "millisecond" => "milliseconds",
            "milliseconds" => "milliseconds",
            "s" => "seconds",
            "sec" => "seconds",
            "second" => "seconds",
            "seconds" => "seconds",
            _ => unit?.Trim().ToLowerInvariant() ?? string.Empty
        };
    }

    private static bool TryPopMatchingFrame(
        Stack<(int FrameIndex, double StartedAt)> stack,
        int expectedFrameIndex,
        out (int FrameIndex, double StartedAt) openedFrame)
    {
        if (stack.Count == 0)
        {
            openedFrame = default;
            return false;
        }

        if (stack.Peek().FrameIndex == expectedFrameIndex)
        {
            openedFrame = stack.Pop();
            return true;
        }

        var buffer = new Stack<(int FrameIndex, double StartedAt)>();
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current.FrameIndex == expectedFrameIndex)
            {
                while (buffer.Count > 0)
                {
                    stack.Push(buffer.Pop());
                }

                openedFrame = current;
                return true;
            }

            buffer.Push(current);
        }

        while (buffer.Count > 0)
        {
            stack.Push(buffer.Pop());
        }

        openedFrame = default;
        return false;
    }

    private static string NormalizeLogMessage(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return string.Empty;
        }

        var withoutTimestamp = TimestampPrefixRegex.Replace(line, string.Empty);
        var withoutHexValues = HexValueRegex.Replace(withoutTimestamp, "0x*");
        var withoutIntegers = IntegerRegex.Replace(withoutHexValues, "#");
        return MultiWhitespaceRegex.Replace(withoutIntegers.Trim(), " ");
    }

    private static DateTimeOffset? TryParseLogTimestamp(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var match = TimestampPrefixRegex.Match(line);
        if (!match.Success)
        {
            return null;
        }

        var stamp = match.Groups["stamp"].Value;
        return DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed
            : null;
    }

    private static bool IsAlertMetric(string key, double value)
    {
        var normalized = key.Trim().ToLowerInvariant();
        return value > 0 && (normalized.Contains("error", StringComparison.Ordinal) ||
                             normalized.Contains("exception", StringComparison.Ordinal) ||
                             normalized.Contains("warning", StringComparison.Ordinal) ||
                             normalized.Contains("failure", StringComparison.Ordinal));
    }

    private static string ToDisplayLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new List<char>(value.Length + 4);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (i > 0 && char.IsUpper(current) && char.IsLower(value[i - 1]))
            {
                builder.Add(' ');
            }

            builder.Add(current == '_' || current == '-' ? ' ' : current);
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(new string(builder.ToArray()));
    }

    private static string CreateMetadataSummary(ProfilingArtifactMetadata metadata)
    {
        return metadata.Kind switch
        {
            ProfilingArtifactKind.Export => $"{metadata.DisplayName} is available in the artifact library, but Sherpa currently provides metadata-only analysis for binary exports such as GC dumps.",
            ProfilingArtifactKind.Screenshot => $"{metadata.DisplayName} is stored in the artifact library and can be used as supporting evidence for a profiling session.",
            _ => $"{metadata.DisplayName} is stored in the artifact library and has a portable metadata summary ready for later UI or Copilot analysis."
        };
    }

    private static string CreateMetadataNote(ProfilingArtifactMetadata metadata)
    {
        return metadata.Kind switch
        {
            ProfilingArtifactKind.Export => "Open the export in a specialized tool for heap or object graph analysis.",
            ProfilingArtifactKind.Screenshot => "Screenshots complement trace and log artifacts but do not currently receive deeper profiling analysis.",
            _ => "Portable analysis for this artifact kind can be expanded incrementally without changing the artifact library contract."
        };
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var index = 0;

        while (value >= 1024 && index < suffixes.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.##} {suffixes[index]}";
    }

    private sealed record ParsedSpeedscopeFrame(string Name, string? File, int? Line);

    private sealed class SpeedscopeAggregate
    {
        public int ProfileCount { get; set; }
        public int SampledProfileCount { get; set; }
        public int EventedProfileCount { get; set; }
        public int ReferenceEventCount { get; set; }
        public bool UsesSampleWeights { get; set; }
        public double TotalReferenceWeight { get; set; }
        public double TotalDurationMs { get; set; }
        public SpeedscopeReferenceMode ReferenceMode { get; private set; }
        public Dictionary<int, double> InclusiveWeights { get; } = new();
        public HashSet<int> ObservedFrames { get; } = new();
        public List<string> Notes { get; } = new();

        public void SetReferenceMode(SpeedscopeReferenceMode mode)
        {
            if (ReferenceMode == SpeedscopeReferenceMode.None)
            {
                ReferenceMode = mode;
                return;
            }

            if (ReferenceMode != mode)
            {
                ReferenceMode = SpeedscopeReferenceMode.Mixed;
            }
        }
    }

    private enum SpeedscopeReferenceMode
    {
        None,
        DurationMs,
        SampleWeight,
        Mixed
    }
}
