using System.Security.Cryptography.X509Certificates;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

internal static class CertificateBundleUtilities
{
    public static byte[] ExportSelectedIdentities(
        byte[] sourceP12,
        string sourcePassword,
        IReadOnlyCollection<LocalSigningIdentity> selectedIdentities,
        string exportPassword)
    {
        ArgumentNullException.ThrowIfNull(sourceP12);
        ArgumentNullException.ThrowIfNull(selectedIdentities);

        if (sourceP12.Length == 0)
            throw new ArgumentException("P12 data cannot be empty.", nameof(sourceP12));
        if (selectedIdentities.Count == 0)
            throw new ArgumentException("At least one signing identity must be selected.", nameof(selectedIdentities));

        var selectedHashes = selectedIdentities
            .Select(identity => NormalizeHex(identity.Hash))
            .Where(hash => hash.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedSerials = selectedIdentities
            .Select(identity => NormalizeHex(identity.SerialNumber).TrimStart('0'))
            .Where(serial => serial.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var imported = X509CertificateLoader.LoadPkcs12Collection(
            sourceP12,
            sourcePassword,
            X509KeyStorageFlags.Exportable);

        try
        {
            var selected = new X509Certificate2Collection();
            foreach (var certificate in imported.Cast<X509Certificate2>())
            {
                var hash = NormalizeHex(certificate.Thumbprint);
                var serial = NormalizeHex(certificate.SerialNumber).TrimStart('0');
                if (!selectedHashes.Contains(hash) && !selectedSerials.Contains(serial))
                    continue;

                if (!certificate.HasPrivateKey)
                {
                    throw new InvalidOperationException(
                        $"Certificate '{certificate.GetNameInfo(X509NameType.SimpleName, false)}' has no private key.");
                }

                selected.Add(certificate);
            }

            if (selected.Count != selectedIdentities.Count)
            {
                throw new InvalidOperationException(
                    $"Only {selected.Count} of {selectedIdentities.Count} selected signing identities were found in the exported keychain bundle.");
            }

            return selected.Export(X509ContentType.Pfx, exportPassword)
                ?? throw new InvalidOperationException("Failed to export the selected certificate bundle.");
        }
        finally
        {
            foreach (var certificate in imported)
                certificate.Dispose();
        }
    }

    private static string NormalizeHex(string? value) =>
        new((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
}
