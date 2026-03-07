using System.Text.Json;
using System.Text.Json.Serialization;

namespace MauiSherpa.Cli.Helpers;

public static class Output
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void WriteJson<T>(T value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
    }

    public static void WriteTable(string[] headers, IEnumerable<string[]> rows)
    {
        var allRows = rows.ToList();
        var widths = new int[headers.Length];

        for (int i = 0; i < headers.Length; i++)
            widths[i] = headers[i].Length;

        foreach (var row in allRows)
            for (int i = 0; i < Math.Min(row.Length, widths.Length); i++)
                widths[i] = Math.Max(widths[i], row[i].Length);

        WriteRow(headers, widths);
        Console.WriteLine(string.Join("  ", widths.Select(w => new string('─', w))));
        foreach (var row in allRows)
            WriteRow(row, widths);
    }

    private static void WriteRow(string[] cells, int[] widths)
    {
        var parts = new string[widths.Length];
        for (int i = 0; i < widths.Length; i++)
            parts[i] = (i < cells.Length ? cells[i] : "").PadRight(widths[i]);
        Console.WriteLine(string.Join("  ", parts));
    }

    public static void WriteSuccess(string message) => Console.WriteLine($"✓ {message}");
    public static void WriteWarning(string message) => Console.WriteLine($"⚠ {message}");
    public static void WriteError(string message) => Console.Error.WriteLine($"✗ {message}");
    public static void WriteInfo(string message) => Console.WriteLine($"  {message}");
}
