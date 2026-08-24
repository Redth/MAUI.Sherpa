using System.Text.Json;
using System.Security.Cryptography;

namespace MauiSherpa.Bundles;

public static class SherpaBundleFile
{
    private static readonly byte[] V1MagicHeader = "SHRPB001"u8.ToArray();
    private static readonly byte[] V2MagicHeader = "SHRPB002"u8.ToArray();

    public static byte[] Encrypt(SherpaBundle bundle, string password)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        BundleValidator.ValidateAndThrow(bundle);
        var payload = JsonSerializer.SerializeToUtf8Bytes(bundle, BundleJson.Options);
        try
        {
            return PackEnvelopeV2.Encrypt(payload, V2MagicHeader, password);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public static SherpaBundle Decrypt(ReadOnlySpan<byte> encryptedData, string password)
    {
        var payload = encryptedData.StartsWith(V2MagicHeader)
            ? PackEnvelopeV2.Decrypt(encryptedData, V2MagicHeader, password)
            : encryptedData.StartsWith(V1MagicHeader)
                ? PasswordEncryption.Decrypt(encryptedData, password, V1MagicHeader)
                : throw new InvalidDataException("Invalid Expedition Pack header.");
        try
        {
            var bundle = JsonSerializer.Deserialize<SherpaBundle>(payload, BundleJson.Options)
                ?? throw new InvalidDataException("The bundle payload is empty.");
            BundleValidator.ValidateAndThrow(bundle);
            return bundle;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public static bool HasValidHeader(ReadOnlySpan<byte> data) =>
        data.StartsWith(V2MagicHeader) || data.StartsWith(V1MagicHeader);

    internal static byte[] EncryptV1ForCompatibilityTests(SherpaBundle bundle, string password)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(bundle, BundleJson.Options);
        try
        {
            return PasswordEncryption.Encrypt(payload, password, V1MagicHeader);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }
}
