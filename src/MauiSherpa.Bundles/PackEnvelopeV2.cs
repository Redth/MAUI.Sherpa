using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace MauiSherpa.Bundles;

internal static class PackEnvelopeV2
{
    private const byte BrotliFlag = 0x01;
    private const byte Pbkdf2Sha256 = 0x01;
    private const byte Aes256Gcm = 0x01;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 600_000;
    private const int MaxUncompressedSize = 128 * 1024 * 1024;
    private const int AuthenticatedHeaderSize = 8 + 4 + sizeof(int) + sizeof(long) + SaltSize + NonceSize;
    private const int EnvelopeOverhead = AuthenticatedHeaderSize + TagSize;

    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> magicHeader, string password)
    {
        ValidateMagic(magicHeader);
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (plaintext.Length > MaxUncompressedSize)
            throw new InvalidDataException($"Expedition Pack payload exceeds {MaxUncompressedSize} bytes.");

        var compressed = Compress(plaintext);
        var useCompression = compressed.Length < plaintext.Length;
        var content = useCompression ? compressed : plaintext.ToArray();
        if (!useCompression)
            CryptographicOperations.ZeroMemory(compressed);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var header = new byte[AuthenticatedHeaderSize];
        magicHeader.CopyTo(header);
        var offset = magicHeader.Length;
        header[offset++] = useCompression ? BrotliFlag : (byte)0;
        header[offset++] = Pbkdf2Sha256;
        header[offset++] = Aes256Gcm;
        header[offset++] = 0;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(offset, sizeof(int)), Iterations);
        offset += sizeof(int);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(offset, sizeof(long)), plaintext.Length);
        offset += sizeof(long);
        salt.CopyTo(header.AsSpan(offset, SaltSize));
        offset += SaltSize;
        nonce.CopyTo(header.AsSpan(offset, NonceSize));

        var ciphertext = new byte[content.Length];
        var tag = new byte[TagSize];
        var key = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, content, ciphertext, tag, header);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(content);
        }

        var result = new byte[EnvelopeOverhead + ciphertext.Length];
        header.CopyTo(result, 0);
        tag.CopyTo(result, AuthenticatedHeaderSize);
        ciphertext.CopyTo(result, EnvelopeOverhead);
        return result;
    }

    public static byte[] Decrypt(ReadOnlySpan<byte> encryptedData, ReadOnlySpan<byte> magicHeader, string password)
    {
        ValidateMagic(magicHeader);
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (encryptedData.Length < EnvelopeOverhead ||
            encryptedData.Length > EnvelopeOverhead + MaxUncompressedSize ||
            !encryptedData[..magicHeader.Length].SequenceEqual(magicHeader))
        {
            throw new InvalidDataException("Invalid Expedition Pack v2 envelope.");
        }

        var header = encryptedData[..AuthenticatedHeaderSize];
        var offset = magicHeader.Length;
        var flags = header[offset++];
        var kdf = header[offset++];
        var cipher = header[offset++];
        var reserved = header[offset++];
        var iterations = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(offset, sizeof(int)));
        offset += sizeof(int);
        var uncompressedLength = BinaryPrimitives.ReadInt64LittleEndian(header.Slice(offset, sizeof(long)));
        offset += sizeof(long);

        if ((flags & ~BrotliFlag) != 0 ||
            kdf != Pbkdf2Sha256 ||
            cipher != Aes256Gcm ||
            reserved != 0 ||
            iterations is < 100_000 or > 2_000_000 ||
            uncompressedLength is < 0 or > MaxUncompressedSize)
        {
            throw new InvalidDataException("Unsupported Expedition Pack v2 envelope.");
        }

        var salt = header.Slice(offset, SaltSize).ToArray();
        offset += SaltSize;
        var nonce = header.Slice(offset, NonceSize).ToArray();
        var tag = encryptedData.Slice(AuthenticatedHeaderSize, TagSize);
        var ciphertext = encryptedData[EnvelopeOverhead..];
        var decrypted = new byte[ciphertext.Length];
        var key = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, HashAlgorithmName.SHA256, KeySize);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, decrypted, header);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        if ((flags & BrotliFlag) == 0)
        {
            if (decrypted.Length != uncompressedLength)
            {
                CryptographicOperations.ZeroMemory(decrypted);
                throw new InvalidDataException("Expedition Pack payload length does not match its envelope.");
            }
            return decrypted;
        }

        try
        {
            return Decompress(decrypted, checked((int)uncompressedLength));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decrypted);
        }
    }

    private static byte[] Compress(ReadOnlySpan<byte> plaintext)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            brotli.Write(plaintext);
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] compressed, int expectedLength)
    {
        var result = new byte[expectedLength];
        using var input = new MemoryStream(compressed, writable: false);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        var read = 0;
        while (read < result.Length)
        {
            var count = brotli.Read(result, read, result.Length - read);
            if (count == 0)
                break;
            read += count;
        }

        if (read != result.Length || brotli.ReadByte() != -1)
        {
            CryptographicOperations.ZeroMemory(result);
            throw new InvalidDataException("Invalid compressed Expedition Pack payload.");
        }
        return result;
    }

    private static void ValidateMagic(ReadOnlySpan<byte> magicHeader)
    {
        if (magicHeader.Length != 8)
            throw new ArgumentException("The Expedition Pack v2 magic header must be eight bytes.", nameof(magicHeader));
    }
}
