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

            // Detect header line (contains "Count" and "TotalSize" and "Class Name")
            if (line.Contains("Count") && line.Contains("TotalSize") && line.Contains("Class Name"))
            {
                inTable = true;
                continue;
            }

            // Detect "Total" summary line
            if (inTable && line.StartsWith("Total", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!inTable)
                continue;

            // Parse data line: MT(hex)  Count  TotalSize  ClassName
            var match = HeapStatLineRegex().Match(line);
            if (match.Success)
            {
                var count = long.Parse(match.Groups["count"].Value, CultureInfo.InvariantCulture);
                var size = long.Parse(match.Groups["size"].Value, CultureInfo.InvariantCulture);
                var typeName = match.Groups["name"].Value.Trim();

                if (!string.IsNullOrWhiteSpace(typeName))
                {
                    types.Add(new GcDumpTypeEntry(typeName, count, size));
                }
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

    // Matches lines like: 00007ffa12345678    12345     6789012  System.String
    [GeneratedRegex(@"^[0-9a-fA-F]+\s+(?<count>\d+)\s+(?<size>\d+)\s+(?<name>.+)$")]
    private static partial Regex HeapStatLineRegex();
}
