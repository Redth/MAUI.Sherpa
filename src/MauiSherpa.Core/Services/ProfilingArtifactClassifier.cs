using MauiSherpa.Core.Models.Profiling;

namespace MauiSherpa.Core.Services;

public static class ProfilingArtifactClassifier
{
    public static bool IsSupported(string path)
    {
        return Classify(path) != ProfilingArtifactKind.Other;
    }

    public static ProfilingArtifactKind Classify(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.EndsWith(".speedscope.json", StringComparison.OrdinalIgnoreCase))
            return ProfilingArtifactKind.Trace;
        if (fileName.EndsWith(".nettrace", StringComparison.OrdinalIgnoreCase))
            return ProfilingArtifactKind.Trace;
        if (fileName.EndsWith(".mibc", StringComparison.OrdinalIgnoreCase))
            return ProfilingArtifactKind.Mibc;
        if (fileName.EndsWith(".gcdump", StringComparison.OrdinalIgnoreCase))
            return ProfilingArtifactKind.GcDump;
        if (fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return ProfilingArtifactKind.Log;
        }

        return ProfilingArtifactKind.Other;
    }

    public static string GetDisplayName(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.EndsWith(".speedscope.json", StringComparison.OrdinalIgnoreCase))
            return "Speedscope profile";
        if (fileName.EndsWith(".nettrace", StringComparison.OrdinalIgnoreCase))
            return "Raw .NET trace";
        if (fileName.EndsWith(".mibc", StringComparison.OrdinalIgnoreCase))
            return "MIBC startup profile";
        if (fileName.EndsWith(".gcdump", StringComparison.OrdinalIgnoreCase))
            return "GC heap dump";
        if (fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return "Capture log";
        }

        return fileName;
    }

    public static string GetContentType(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.EndsWith(".speedscope.json", StringComparison.OrdinalIgnoreCase))
            return "application/json";
        if (fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return "text/plain";
        }

        return "application/octet-stream";
    }

    public static string GetBaseName(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.EndsWith(".speedscope.json", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^".speedscope.json".Length]
            : Path.GetFileNameWithoutExtension(fileName);
    }
}
