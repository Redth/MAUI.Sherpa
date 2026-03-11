using System.Globalization;
using System.Text.RegularExpressions;
using MauiSherpa.Core.Models.Profiling;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Parses the text output from <c>dotnet-gcdump report</c> into structured data.
/// </summary>
public static partial class GcDumpReportParser
{
    /// <summary>
    /// Parse heapstat output from <c>dotnet-gcdump report</c>.
    /// Expected format:
    /// <code>
    ///       MT      Count    TotalSize  Class Name
    /// 00007ffa...    12345     6789012  System.String
    /// </code>
    /// </summary>
    public static GcDumpReport? ParseHeapStatOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        var types = new List<GcDumpTypeEntry>();
        var lines = output.Split('\n', StringSplitOptions.TrimEntries);
        var inTable = false;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Detect header line — formats vary by dotnet-gcdump version:
            // "MT      Count    TotalSize  Class Name"
            // "Object Bytes     Count  Type"
            if ((line.Contains("Count") && (line.Contains("TotalSize") || line.Contains("Object Bytes"))
                && (line.Contains("Class Name") || line.Contains("Type"))))
            {
                inTable = true;
                continue;
            }

            // Detect summary/total lines
            if (inTable && line.StartsWith("Total", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!inTable)
                continue;

            // Try the modern format first: "  46,376         1  System.String (Bytes > 1K)  [Module(...)]"
            var match = ModernHeapStatLineRegex().Match(line);
            if (match.Success)
            {
                var sizeStr = match.Groups["size"].Value.Replace(",", "");
                var countStr = match.Groups["count"].Value.Replace(",", "");
                var size = long.Parse(sizeStr, CultureInfo.InvariantCulture);
                var count = long.Parse(countStr, CultureInfo.InvariantCulture);
                var typeName = match.Groups["name"].Value.Trim();
                // Strip trailing annotations like "(Bytes > 1K)  [Module(...)]"
                var annoIdx = typeName.IndexOf("  [Module(", StringComparison.Ordinal);
                if (annoIdx > 0) typeName = typeName[..annoIdx];
                annoIdx = typeName.IndexOf(" (Bytes ", StringComparison.Ordinal);
                if (annoIdx > 0) typeName = typeName[..annoIdx];

                if (!string.IsNullOrWhiteSpace(typeName))
                    types.Add(new GcDumpTypeEntry(typeName, count, size));
                continue;
            }

            // Legacy format: "00007ffa12345678    12345     6789012  System.String"
            var legacyMatch = LegacyHeapStatLineRegex().Match(line);
            if (legacyMatch.Success)
            {
                var count = long.Parse(legacyMatch.Groups["count"].Value, CultureInfo.InvariantCulture);
                var size = long.Parse(legacyMatch.Groups["size"].Value, CultureInfo.InvariantCulture);
                var typeName = legacyMatch.Groups["name"].Value.Trim();

                if (!string.IsNullOrWhiteSpace(typeName))
                    types.Add(new GcDumpTypeEntry(typeName, count, size));
            }
        }

        if (types.Count == 0)
            return null;

        // Sort by size descending (largest allocations first)
        types.Sort((a, b) => b.Size.CompareTo(a.Size));

        return new GcDumpReport(
            Types: types,
            TotalSize: types.Sum(t => t.Size),
            TotalCount: types.Sum(t => t.Count),
            RawOutput: output);
    }

    // Modern format: "  46,376         1  System.String (Bytes > 1K)  [Module(...)]"
    // Size and count may have commas as thousand separators
    [GeneratedRegex(@"^\s*(?<size>[\d,]+)\s+(?<count>[\d,]+)\s+(?<name>.+)$")]
    private static partial Regex ModernHeapStatLineRegex();

    // Legacy format: "00007ffa12345678    12345     6789012  System.String"
    [GeneratedRegex(@"^[0-9a-fA-F]+\s+(?<count>\d+)\s+(?<size>\d+)\s+(?<name>.+)$")]
    private static partial Regex LegacyHeapStatLineRegex();
}
