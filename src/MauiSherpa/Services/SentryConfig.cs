using System.Reflection;

namespace MauiSherpa.Services;

/// <summary>
/// Resolves the Sentry DSN without hardcoding it in source.
/// Resolution order:
///   1. SENTRY_DSN environment variable (useful for local development)
///   2. Assembly metadata "SentryDsn" (baked in by CI via -p:SentryDsn=... in Directory.Build.props)
/// Returns null when neither is set, in which case Sentry should not be initialized.
/// </summary>
internal static class SentryConfig
{
    public static string? GetDsn()
    {
        var fromEnv = Environment.GetEnvironmentVariable("SENTRY_DSN");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv.Trim();

        var fromAssembly = typeof(SentryConfig).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(a.Key, "SentryDsn", StringComparison.Ordinal))
            ?.Value;

        return string.IsNullOrWhiteSpace(fromAssembly) ? null : fromAssembly.Trim();
    }
}
