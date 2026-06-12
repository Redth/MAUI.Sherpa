using MauiSherpa.Bundle.Loading;
using MauiSherpa.Bundle.Models;
using MauiSherpa.Bundle.Pipeline;

namespace MauiSherpa.Bundle.Steps;

/// <summary>
/// The <c>setup</c> step (spec §3 Setup blocks): materializes signing assets to
/// disk / the keychain and records the MSBuild signing properties the build step
/// will need.
/// </summary>
public sealed class SetupRunner
{
    public async Task RunAsync(PlatformContext ctx)
    {
        ctx.Log.Step($"[{ctx.Platform.ToDisplayName()}] setup");
        switch (ctx.Platform)
        {
            case SherpaPlatform.Android: SetupAndroid(ctx); break;
            case SherpaPlatform.IOS: await SetupAppleAsync(ctx); break;
            case SherpaPlatform.MacOS: await SetupMacAsync(ctx, ctx.Run.Environment.MacOS, "macos"); break;
            case SherpaPlatform.MacCatalyst: await SetupMacAsync(ctx, ctx.Run.Environment.MacCatalyst, "maccatalyst"); break;
            case SherpaPlatform.Windows: SetupWindows(ctx); break;
        }
    }

    private static void SetupAndroid(PlatformContext ctx)
    {
        var setup = ctx.Run.Environment.Android?.Setup;
        var keystores = setup?.Keystores;
        if (keystores is null || keystores.Count == 0)
        {
            ctx.Log.Info("No Android keystores in bundle; relying on project/MSBuild defaults.");
            return;
        }

        // The first keystore is the primary signing identity.
        var primary = keystores[0];
        var bytes = SigningAssets.DecodeBase64(primary.Content, "Android keystore");
        var path = SigningAssets.WriteAsset(ctx.Run.ScratchDirectory, "android", "release.keystore", bytes);

        ctx.SigningProperties["AndroidKeyStore"] = "true";
        ctx.SigningProperties["AndroidSigningKeyStore"] = path;
        if (primary.KeyAlias is { } alias) ctx.SigningProperties["AndroidSigningKeyAlias"] = alias;
        if (primary.StorePassword is { } sp) ctx.SigningProperties["AndroidSigningStorePass"] = sp;
        if (primary.KeyPassword is { } kp) ctx.SigningProperties["AndroidSigningKeyPass"] = kp;

        ctx.Log.Success($"Android keystore written → {path}");
        if (keystores.Count > 1)
            ctx.Log.Warn($"{keystores.Count} keystores provided; only the first is used for signing.");
    }

    private async Task SetupAppleAsync(PlatformContext ctx)
    {
        var setup = ctx.Run.Environment.IOS?.Setup;
        if (setup is null)
        {
            ctx.Log.Info("No iOS setup block; relying on existing keychain/profiles.");
            return;
        }

        var profiles = setup.Profiles ?? new List<ProfileRef>();
        for (var i = 0; i < profiles.Count; i++)
            InstallProvisioningProfile(ctx, profiles[i].Content, $"iOS profile #{i + 1}");

        var certs = setup.Certificates ?? new List<CertificateRef>();
        for (var i = 0; i < certs.Count; i++)
            await ImportCertificateAsync(ctx, certs[i].Content, certs[i].Password, $"iOS certificate #{i + 1}", "ios");
    }

    private async Task SetupMacAsync(PlatformContext ctx, MacPlatform? mac, string subdir)
    {
        if (mac is null)
        {
            ctx.Log.Info($"No {ctx.Platform.ToDisplayName()} block; relying on existing keychain/profiles.");
            return;
        }

        if (mac.ProvisioningProfile is { } prof)
            InstallProvisioningProfile(ctx, prof, $"{ctx.Platform.ToDisplayName()} profile", ".provisionprofile");
        if (mac.Certificate is { } cert)
            await ImportCertificateAsync(ctx, cert, mac.CertificatePassword, $"{ctx.Platform.ToDisplayName()} certificate", subdir);
    }

    private static void SetupWindows(PlatformContext ctx)
    {
        var win = ctx.Run.Environment.Windows;
        if (win?.Certificate is null)
        {
            ctx.Log.Info("No Windows certificate in bundle; relying on project/MSBuild defaults.");
            return;
        }

        var bytes = SigningAssets.DecodeBase64(win.Certificate, "Windows certificate");
        var path = SigningAssets.WriteAsset(ctx.Run.ScratchDirectory, "windows", "signing.pfx", bytes);

        ctx.SigningProperties["AppxPackageSigningEnabled"] = "true";
        ctx.SigningProperties["PackageCertificateKeyFile"] = path;
        if (win.CertificatePassword is { } pwd)
            ctx.SigningProperties["PackageCertificatePassword"] = pwd;

        ctx.Log.Success($"Windows signing certificate written → {path}");
    }

    private static void InstallProvisioningProfile(PlatformContext ctx, string? content, string what, string extension = ".mobileprovision")
    {
        if (!OperatingSystem.IsMacOS())
        {
            ctx.Log.Warn($"{what}: provisioning profiles can only be installed on macOS; skipping.");
            return;
        }

        var bytes = SigningAssets.DecodeBase64(content, what);
        var uuid = SigningAssets.ReadProfilePlistValue(bytes, "UUID") ?? Guid.NewGuid().ToString();
        var name = SigningAssets.ReadProfilePlistValue(bytes, "Name");

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "MobileDevice", "Provisioning Profiles");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, uuid + extension);
        File.WriteAllBytes(dest, bytes);

        ctx.Log.Success($"{what} installed → {dest}{(name is null ? "" : $" ({name})")}");
    }

    private async Task ImportCertificateAsync(PlatformContext ctx, string? content, string? password, string what, string subdir)
    {
        if (!OperatingSystem.IsMacOS())
        {
            ctx.Log.Warn($"{what}: certificates are imported via the macOS keychain; skipping on this OS.");
            return;
        }

        var bytes = SigningAssets.DecodeBase64(content, what);
        var p12 = SigningAssets.WriteAsset(ctx.Run.ScratchDirectory, subdir, $"cert-{Guid.NewGuid():N}.p12", bytes);

        // Import into the default (login) keychain. -A allows any app to use the
        // key without prompting, which is required for headless CI builds.
        var args = new List<string> { "import", p12, "-f", "pkcs12", "-A" };
        if (!string.IsNullOrEmpty(password))
        {
            args.Add("-P");
            args.Add(password);
        }

        var result = await ctx.Process.RunAsync("security", args, log: ctx.Log, ct: ctx.CancellationToken);
        if (result.Success || result.StdErr.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            ctx.Log.Success($"{what} imported into keychain.");
        else
            throw new SherpaBundleException($"{what}: keychain import failed: {result.StdErr}");
    }
}
