namespace MauiSherpa.Core.Services;

internal static class CertificateSecretKeys
{
    public const string Prefix = "CERT_";
    public const string P12Suffix = "_P12";
    public const string PasswordSuffix = "_PWD";
    public const string MetadataSuffix = "_META";

    public static bool TryExtractSerial(string key, string suffix, out string serial)
    {
        if (SecretItemAdapterHelper.TryExtractAffixedId(key, Prefix, suffix, out serial) &&
            (key[Prefix.Length - 1] is '_' or '-') &&
            (key[key.Length - suffix.Length] is '_' or '-') &&
            serial.All(char.IsLetterOrDigit) &&
            SecretItemAdapterHelper.SanitizeSerialNumber(serial).Length > 0)
        {
            return true;
        }

        serial = string.Empty;
        return false;
    }
}
