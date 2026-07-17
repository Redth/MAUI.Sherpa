using System.Text;
using System.Text.Json;
using MauiSherpa.Workloads.Models;

namespace MauiSherpa.Workloads.Services;

/// <summary>
/// Performs a surgical JSONC edit of sdk.workloadVersion. Unrelated bytes, including
/// comments, trailing commas, indentation, and newline style, are retained.
/// </summary>
public sealed class GlobalJsonWorkloadPinEditor : IGlobalJsonWorkloadPinEditor
{
    public GlobalJsonWorkloadPinPreview Preview(string projectFolder, string? workloadVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        var folder = Path.GetFullPath(projectFolder);
        var service = new GlobalJsonService();
        var path = service.FindGlobalJson(folder) ?? Path.Combine(folder, "global.json");
        var original = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        var updated = Edit(original, workloadVersion);

        return new GlobalJsonWorkloadPinPreview
        {
            Path = path,
            OriginalContent = original,
            UpdatedContent = updated,
            WorkloadVersion = workloadVersion
        };
    }

    public async Task ApplyAsync(
        GlobalJsonWorkloadPinPreview preview,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (!preview.Changed)
            return;

        var directory = Path.GetDirectoryName(preview.Path)
            ?? throw new InvalidOperationException("The global.json path has no parent directory.");
        Directory.CreateDirectory(directory);

        var current = File.Exists(preview.Path)
            ? await File.ReadAllTextAsync(preview.Path, cancellationToken)
            : string.Empty;
        if (!string.Equals(current, preview.OriginalContent, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"'{preview.Path}' changed after the workload pin preview was created. Review the current file and try again.");
        }

        var temporary = $"{preview.Path}.mauisherpa-{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                preview.UpdatedContent,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            File.Move(temporary, preview.Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    internal static string Edit(string original, string? workloadVersion)
    {
        if (string.IsNullOrWhiteSpace(original))
        {
            return workloadVersion == null
                ? original
                : $"{{{Environment.NewLine}  \"sdk\": {{{Environment.NewLine}    \"workloadVersion\": {JsonSerializer.Serialize(workloadVersion)}{Environment.NewLine}  }}{Environment.NewLine}}}{Environment.NewLine}";
        }

        var bytes = Encoding.UTF8.GetBytes(original);
        var location = Locate(bytes);
        var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        if (location.WorkloadValueStart >= 0)
        {
            if (workloadVersion != null)
            {
                var serialized = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(workloadVersion));
                return Encoding.UTF8.GetString(Replace(
                    bytes,
                    location.WorkloadValueStart,
                    location.WorkloadValueEnd,
                    serialized));
            }

            var updated = RemoveProperty(
                bytes,
                location.SdkObjectStart,
                location.SdkObjectEnd,
                location.WorkloadPropertyStart,
                location.WorkloadValueEnd);
            var relocated = Locate(updated);
            if (relocated.LegacyWorkloadValueStart >= 0)
            {
                updated = RemoveProperty(
                    updated,
                    relocated.LegacyWorkloadObjectStart,
                    relocated.LegacyWorkloadObjectEnd,
                    relocated.LegacyWorkloadPropertyStart,
                    relocated.LegacyWorkloadValueEnd);
            }
            return Encoding.UTF8.GetString(updated);
        }

        if (workloadVersion == null)
        {
            if (location.LegacyWorkloadValueStart < 0)
                return original;
            return Encoding.UTF8.GetString(RemoveProperty(
                bytes,
                location.LegacyWorkloadObjectStart,
                location.LegacyWorkloadObjectEnd,
                location.LegacyWorkloadPropertyStart,
                location.LegacyWorkloadValueEnd));
        }

        var property = $"\"workloadVersion\": {JsonSerializer.Serialize(workloadVersion)}";
        if (location.SdkObjectStart >= 0)
        {
            return Encoding.UTF8.GetString(InsertObjectProperty(
                bytes,
                location.SdkObjectStart,
                location.SdkObjectEnd,
                location.SdkLastValueEnd,
                property,
                newline));
        }

        var sdkProperty = $"\"sdk\": {{{newline}    {property}{newline}  }}";
        return Encoding.UTF8.GetString(InsertObjectProperty(
            bytes,
            location.RootObjectStart,
            location.RootObjectEnd,
            location.RootLastValueEnd,
            sdkProperty,
            newline));
    }

    private static JsonLocation Locate(byte[] bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var result = new JsonLocation();
        string? pendingProperty = null;
        var pendingPropertyStart = -1;
        var sdkDepth = -1;
        var legacyWorkloadDepth = -1;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                pendingProperty = reader.GetString();
                pendingPropertyStart = checked((int)reader.TokenStartIndex);
                continue;
            }

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                if (reader.CurrentDepth == 0)
                    result.RootObjectStart = checked((int)reader.TokenStartIndex);
                if (pendingProperty == "sdk" && reader.CurrentDepth == 1)
                {
                    result.SdkObjectStart = checked((int)reader.TokenStartIndex);
                    sdkDepth = reader.CurrentDepth;
                }
                if (pendingProperty == "workloadSet" && reader.CurrentDepth == 1)
                {
                    result.LegacyWorkloadObjectStart = checked((int)reader.TokenStartIndex);
                    legacyWorkloadDepth = reader.CurrentDepth;
                }
            }

            if (pendingProperty == "workloadVersion" &&
                sdkDepth >= 0 &&
                reader.CurrentDepth == sdkDepth + 1)
            {
                if (reader.TokenType != JsonTokenType.String)
                    throw new InvalidDataException("sdk.workloadVersion must be a JSON string.");
                result.WorkloadPropertyStart = pendingPropertyStart;
                result.WorkloadValueStart = checked((int)reader.TokenStartIndex);
                result.WorkloadValueEnd = checked((int)reader.BytesConsumed);
            }
            if (pendingProperty == "version" &&
                legacyWorkloadDepth >= 0 &&
                reader.CurrentDepth == legacyWorkloadDepth + 1)
            {
                if (reader.TokenType != JsonTokenType.String)
                    throw new InvalidDataException("workloadSet.version must be a JSON string.");
                result.LegacyWorkloadPropertyStart = pendingPropertyStart;
                result.LegacyWorkloadValueStart = checked((int)reader.TokenStartIndex);
                result.LegacyWorkloadValueEnd = checked((int)reader.BytesConsumed);
            }

            if (reader.TokenType == JsonTokenType.EndObject)
            {
                if (reader.CurrentDepth == sdkDepth)
                {
                    result.SdkObjectEnd = checked((int)reader.TokenStartIndex);
                    sdkDepth = -1;
                }
                if (reader.CurrentDepth == legacyWorkloadDepth)
                {
                    result.LegacyWorkloadObjectEnd = checked((int)reader.TokenStartIndex);
                    legacyWorkloadDepth = -1;
                }
                if (reader.CurrentDepth == 0)
                    result.RootObjectEnd = checked((int)reader.TokenStartIndex);
            }

            if (IsCompletedValue(reader.TokenType))
            {
                if (reader.CurrentDepth == 1)
                    result.RootLastValueEnd = checked((int)reader.BytesConsumed);
                if (sdkDepth >= 0 && reader.CurrentDepth == sdkDepth + 1)
                    result.SdkLastValueEnd = checked((int)reader.BytesConsumed);
            }

            pendingProperty = null;
            pendingPropertyStart = -1;
        }

        if (result.RootObjectStart < 0 || result.RootObjectEnd < 0)
            throw new InvalidDataException("global.json must contain a JSON object.");
        return result;
    }

    private static byte[] InsertObjectProperty(
        byte[] bytes,
        int objectStart,
        int objectEnd,
        int lastValueEnd,
        string property,
        string newline)
    {
        if (lastValueEnd >= 0 && FindNextSiblingComma(bytes, lastValueEnd, objectEnd) < 0)
        {
            bytes = Replace(bytes, lastValueEnd, lastValueEnd, [(byte)',']);
            objectEnd++;
        }

        var contentEnd = objectEnd;
        while (contentEnd > objectStart + 1 && IsWhitespace(bytes[contentEnd - 1]))
            contentEnd--;

        var hasContent = contentEnd > objectStart + 1;
        var closingIndent = GetLineIndent(bytes, objectEnd);
        var indentUnit = DetectIndentUnit(bytes);
        var childIndent = closingIndent + indentUnit;
        var trailing = Encoding.UTF8.GetString(bytes, contentEnd, objectEnd - contentEnd);
        var trailingHasNewline = trailing.Contains('\n');

        var insertion = new StringBuilder();
        insertion.Append(newline).Append(childIndent).Append(property);
        if (!trailingHasNewline)
            insertion.Append(newline).Append(closingIndent);

        return Replace(
            bytes,
            contentEnd,
            contentEnd,
            Encoding.UTF8.GetBytes(insertion.ToString()));
    }

    private static byte[] RemoveProperty(
        byte[] bytes,
        int objectStart,
        int objectEnd,
        int propertyStart,
        int valueEnd)
    {
        var start = propertyStart;
        var end = valueEnd;

        var lineStart = start;
        while (lineStart > objectStart + 1 &&
               bytes[lineStart - 1] != (byte)'\n' &&
               IsWhitespace(bytes[lineStart - 1]))
            lineStart--;

        var next = end;
        next = SkipTriviaForward(bytes, next, objectEnd);
        if (next < objectEnd && bytes[next] == (byte)',')
            return Replace(bytes, lineStart, next + 1, []);

        var previousComma = FindPreviousSiblingComma(bytes, objectStart, lineStart);
        if (previousComma >= 0)
        {
            var trailingEnd = SkipTriviaForward(bytes, end, objectEnd);
            var withoutProperty = Replace(bytes, lineStart, trailingEnd, []);
            return Replace(withoutProperty, previousComma, previousComma + 1, []);
        }

        return Replace(bytes, lineStart, end, []);
    }

    private static int FindNextSiblingComma(byte[] bytes, int start, int objectEnd)
    {
        var next = SkipTriviaForward(bytes, start, objectEnd);
        return next < objectEnd && bytes[next] == (byte)',' ? next : -1;
    }

    private static int SkipTriviaForward(byte[] bytes, int start, int end)
    {
        var index = start;
        while (index < end)
        {
            if (IsWhitespace(bytes[index]))
            {
                index++;
                continue;
            }

            if (index + 1 < end && bytes[index] == (byte)'/' && bytes[index + 1] == (byte)'/')
            {
                index += 2;
                while (index < end && bytes[index] != (byte)'\n')
                    index++;
                continue;
            }

            if (index + 1 < end && bytes[index] == (byte)'/' && bytes[index + 1] == (byte)'*')
            {
                index += 2;
                while (index + 1 < end &&
                       !(bytes[index] == (byte)'*' && bytes[index + 1] == (byte)'/'))
                    index++;
                index = Math.Min(index + 2, end);
                continue;
            }

            break;
        }
        return index;
    }

    private static int FindPreviousSiblingComma(byte[] bytes, int objectStart, int end)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;
        var previousComma = -1;

        for (var index = objectStart + 1; index < end; index++)
        {
            var value = bytes[index];
            if (inString)
            {
                if (escaped)
                    escaped = false;
                else if (value == (byte)'\\')
                    escaped = true;
                else if (value == (byte)'"')
                    inString = false;
                continue;
            }

            if (value == (byte)'"')
            {
                inString = true;
                continue;
            }

            if (index + 1 < end && value == (byte)'/' && bytes[index + 1] == (byte)'/')
            {
                index += 2;
                while (index < end && bytes[index] != (byte)'\n')
                    index++;
                continue;
            }

            if (index + 1 < end && value == (byte)'/' && bytes[index + 1] == (byte)'*')
            {
                index += 2;
                while (index + 1 < end &&
                       !(bytes[index] == (byte)'*' && bytes[index + 1] == (byte)'/'))
                    index++;
                index++;
                continue;
            }

            if (value is (byte)'{' or (byte)'[')
                depth++;
            else if (value is (byte)'}' or (byte)']')
                depth--;
            else if (value == (byte)',' && depth == 0)
                previousComma = index;
        }

        return previousComma;
    }

    private static byte[] Replace(byte[] source, int start, int end, byte[] replacement)
    {
        var result = new byte[source.Length - (end - start) + replacement.Length];
        Buffer.BlockCopy(source, 0, result, 0, start);
        Buffer.BlockCopy(replacement, 0, result, start, replacement.Length);
        Buffer.BlockCopy(source, end, result, start + replacement.Length, source.Length - end);
        return result;
    }

    private static string GetLineIndent(byte[] bytes, int position)
    {
        var lineStart = position;
        while (lineStart > 0 && bytes[lineStart - 1] != (byte)'\n')
            lineStart--;
        return Encoding.UTF8.GetString(bytes, lineStart, position - lineStart);
    }

    private static string DetectIndentUnit(byte[] bytes)
    {
        for (var index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] != (byte)'\n')
                continue;
            var start = ++index;
            while (index < bytes.Length && (bytes[index] == (byte)' ' || bytes[index] == (byte)'\t'))
                index++;
            if (index > start && index < bytes.Length && bytes[index] == (byte)'"')
                return Encoding.UTF8.GetString(bytes, start, index - start);
        }
        return "  ";
    }

    private static bool IsWhitespace(byte value) =>
        value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';

    private static bool IsCompletedValue(JsonTokenType tokenType) =>
        tokenType is JsonTokenType.String or
            JsonTokenType.Number or
            JsonTokenType.True or
            JsonTokenType.False or
            JsonTokenType.Null or
            JsonTokenType.EndObject or
            JsonTokenType.EndArray;

    private sealed class JsonLocation
    {
        public int RootObjectStart { get; set; } = -1;
        public int RootObjectEnd { get; set; } = -1;
        public int RootLastValueEnd { get; set; } = -1;
        public int SdkObjectStart { get; set; } = -1;
        public int SdkObjectEnd { get; set; } = -1;
        public int SdkLastValueEnd { get; set; } = -1;
        public int WorkloadPropertyStart { get; set; } = -1;
        public int WorkloadValueStart { get; set; } = -1;
        public int WorkloadValueEnd { get; set; } = -1;
        public int LegacyWorkloadObjectStart { get; set; } = -1;
        public int LegacyWorkloadObjectEnd { get; set; } = -1;
        public int LegacyWorkloadPropertyStart { get; set; } = -1;
        public int LegacyWorkloadValueStart { get; set; } = -1;
        public int LegacyWorkloadValueEnd { get; set; } = -1;
    }
}
