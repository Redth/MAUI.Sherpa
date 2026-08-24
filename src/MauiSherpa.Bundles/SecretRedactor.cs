namespace MauiSherpa.Bundles;

public sealed class SecretRedactor(IEnumerable<string> secretValues)
{
    private readonly string[] _secretValues = secretValues
        .Where(value => !string.IsNullOrEmpty(value))
        .Distinct(StringComparer.Ordinal)
        .OrderByDescending(value => value.Length)
        .ToArray();

    public string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value ?? string.Empty;

        var redacted = value;
        foreach (var secret in _secretValues)
            redacted = redacted.Replace(secret, "***", StringComparison.Ordinal);
        return redacted;
    }
}
