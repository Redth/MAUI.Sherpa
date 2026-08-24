using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace MauiSherpa.Bundles;

public static class PasswordEncryption
{
    private const int SaltSize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int DefaultIterations = 600_000;
    private const int MaxIterations = 2_000_000;
    private const int MaxHeaderLength = 16 * 1024;

    private sealed record EnvelopeHeader(
        int Version,
        string Kdf,
        int Iterations,
        string Salt,
        string Cipher,
        string Nonce,
        string Tag);

    public static byte[] Encrypt(
        ReadOnlySpan<byte> plaintext,
        string password,
        ReadOnlySpan<byte> magicHeader,
        int iterations = DefaultIterations)
    {
        ValidateInputs(password, magicHeader, iterations);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = DeriveKey(password, salt, iterations);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, magicHeader);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        var header = new EnvelopeHeader(
            Version: 1,
            Kdf: "pbkdf2-sha256",
            Iterations: iterations,
            Salt: Convert.ToBase64String(salt),
            Cipher: "aes-256-gcm",
            Nonce: Convert.ToBase64String(nonce),
            Tag: Convert.ToBase64String(tag));
        var headerBytes = JsonSerializer.SerializeToUtf8Bytes(header, BundleJson.Options);

        var result = new byte[magicHeader.Length + sizeof(int) + headerBytes.Length + ciphertext.Length];
        magicHeader.CopyTo(result);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(magicHeader.Length, sizeof(int)), headerBytes.Length);
        headerBytes.CopyTo(result.AsSpan(magicHeader.Length + sizeof(int)));
        ciphertext.CopyTo(result.AsSpan(magicHeader.Length + sizeof(int) + headerBytes.Length));
        return result;
    }

    public static byte[] EncryptLegacy(
        ReadOnlySpan<byte> plaintext,
        string password,
        ReadOnlySpan<byte> magicHeader,
        int iterations)
    {
        ValidateInputs(password, magicHeader, iterations);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = DeriveKey(password, salt, iterations);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        var result = new byte[magicHeader.Length + SaltSize + NonceSize + TagSize + ciphertext.Length];
        var offset = 0;
        magicHeader.CopyTo(result);
        offset += magicHeader.Length;
        salt.CopyTo(result.AsSpan(offset));
        offset += SaltSize;
        nonce.CopyTo(result.AsSpan(offset));
        offset += NonceSize;
        tag.CopyTo(result.AsSpan(offset));
        offset += TagSize;
        ciphertext.CopyTo(result.AsSpan(offset));
        return result;
    }

    public static byte[] Decrypt(
        ReadOnlySpan<byte> encryptedData,
        string password,
        ReadOnlySpan<byte> magicHeader)
    {
        ValidateInputs(password, magicHeader, DefaultIterations);

        if (encryptedData.Length < magicHeader.Length + sizeof(int) ||
            !encryptedData[..magicHeader.Length].SequenceEqual(magicHeader))
        {
            throw new InvalidDataException("Invalid encrypted file header.");
        }

        var headerLength = BinaryPrimitives.ReadInt32LittleEndian(
            encryptedData.Slice(magicHeader.Length, sizeof(int)));
        if (headerLength <= 0 || headerLength > MaxHeaderLength ||
            encryptedData.Length < magicHeader.Length + sizeof(int) + headerLength)
        {
            throw new InvalidDataException("Invalid encrypted file envelope.");
        }

        var headerStart = magicHeader.Length + sizeof(int);
        var header = JsonSerializer.Deserialize<EnvelopeHeader>(
            encryptedData.Slice(headerStart, headerLength),
            BundleJson.Options) ?? throw new InvalidDataException("Missing encrypted file envelope.");
        ValidateHeader(header);

        var salt = Convert.FromBase64String(header.Salt);
        var nonce = Convert.FromBase64String(header.Nonce);
        var tag = Convert.FromBase64String(header.Tag);
        if (salt.Length != SaltSize || nonce.Length != NonceSize || tag.Length != TagSize)
            throw new InvalidDataException("Invalid encrypted file parameters.");

        var ciphertext = encryptedData[(headerStart + headerLength)..];
        var plaintext = new byte[ciphertext.Length];
        var key = DeriveKey(password, salt, header.Iterations);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, magicHeader);
            return plaintext;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static byte[] DecryptLegacy(
        ReadOnlySpan<byte> encryptedData,
        string password,
        ReadOnlySpan<byte> magicHeader,
        int iterations)
    {
        ValidateInputs(password, magicHeader, iterations);
        var minimumLength = magicHeader.Length + SaltSize + NonceSize + TagSize;
        if (encryptedData.Length < minimumLength ||
            !encryptedData[..magicHeader.Length].SequenceEqual(magicHeader))
        {
            throw new InvalidDataException("Invalid encrypted file format.");
        }

        var offset = magicHeader.Length;
        var salt = encryptedData.Slice(offset, SaltSize).ToArray();
        offset += SaltSize;
        var nonce = encryptedData.Slice(offset, NonceSize).ToArray();
        offset += NonceSize;
        var tag = encryptedData.Slice(offset, TagSize).ToArray();
        offset += TagSize;
        var ciphertext = encryptedData[offset..];
        var plaintext = new byte[ciphertext.Length];
        var key = DeriveKey(password, salt, iterations);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return plaintext;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static void ValidateInputs(string password, ReadOnlySpan<byte> magicHeader, int iterations)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (magicHeader.Length < 4)
            throw new ArgumentException("The magic header must contain at least four bytes.", nameof(magicHeader));
        if (iterations < 100_000)
            throw new ArgumentOutOfRangeException(nameof(iterations), "At least 100,000 KDF iterations are required.");
        if (iterations > MaxIterations)
            throw new ArgumentOutOfRangeException(nameof(iterations), $"At most {MaxIterations:N0} KDF iterations are allowed.");
    }

    private static void ValidateHeader(EnvelopeHeader header)
    {
        if (header.Version != 1 ||
            header.Kdf != "pbkdf2-sha256" ||
            header.Cipher != "aes-256-gcm" ||
            header.Iterations is < 100_000 or > MaxIterations)
        {
            throw new InvalidDataException("Unsupported encrypted file envelope.");
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, KeySize);
}
