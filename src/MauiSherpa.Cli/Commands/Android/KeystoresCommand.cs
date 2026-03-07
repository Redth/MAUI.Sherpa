using System.CommandLine;
using MauiSherpa.Cli.Helpers;

namespace MauiSherpa.Cli.Commands.Android;

public static class KeystoresCommand
{
    public static Command Create()
    {
        var cmd = new Command("keystores", "Android keystore management — create keystores and view certificate fingerprints.");
        cmd.Add(CreateCreateCommand());
        cmd.Add(CreateSignaturesCommand());
        return cmd;
    }

    private static async Task<string?> FindKeytoolAsync()
    {
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
        {
            var keytoolPath = Path.Combine(javaHome, "bin", OperatingSystem.IsWindows() ? "keytool.exe" : "keytool");
            if (File.Exists(keytoolPath)) return keytoolPath;
        }
        return await ProcessRunner.WhichAsync("keytool");
    }

    private static Command CreateCreateCommand()
    {
        var cmd = new Command("create", "Create a new Android keystore for app signing.\n\nExample:\n  maui-sherpa android keystores create --alias mykey --path ./release.keystore");

        var pathOpt = new Option<string>("--path") { Description = "Output keystore file path", Required = true };
        var aliasOpt = new Option<string>("--alias") { Description = "Key alias name", DefaultValueFactory = (_) => "key0" };
        var cnOpt = new Option<string>("--cn") { Description = "Common Name (e.g., your name)", Required = true };
        var orgOpt = new Option<string?>("--org") { Description = "Organization name" };
        var validityOpt = new Option<int>("--validity") { Description = "Validity in days", DefaultValueFactory = (_) => 10000 };
        var storepassOpt = new Option<string>("--storepass") { Description = "Keystore password", Required = true };
        var keypassOpt = new Option<string?>("--keypass") { Description = "Key password (defaults to storepass)" };

        cmd.Add(pathOpt);
        cmd.Add(aliasOpt);
        cmd.Add(cnOpt);
        cmd.Add(orgOpt);
        cmd.Add(validityOpt);
        cmd.Add(storepassOpt);
        cmd.Add(keypassOpt);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var path = parseResult.GetValue(pathOpt);
            var alias = parseResult.GetValue(aliasOpt);
            var cn = parseResult.GetValue(cnOpt);
            var org = parseResult.GetValue(orgOpt);
            var validity = parseResult.GetValue(validityOpt);
            var storepass = parseResult.GetValue(storepassOpt);
            var keypass = parseResult.GetValue(keypassOpt);
            var keytool = await FindKeytoolAsync();
            if (keytool is null) { Output.WriteError("keytool not found. Set JAVA_HOME or install JDK."); return; }

            var dname = $"CN={cn}";
            if (!string.IsNullOrEmpty(org)) dname += $", O={org}";

            var args = $"-genkeypair -v -keystore \"{path}\" -alias {alias} -keyalg RSA -keysize 2048 " +
                       $"-validity {validity} -storepass \"{storepass}\" -keypass \"{keypass ?? storepass}\" -dname \"{dname}\"";

            var result = await ProcessRunner.RunAsync(keytool, args);
            if (result.ExitCode == 0)
                Output.WriteSuccess($"Keystore created: {path}");
            else
                Output.WriteError($"Failed: {result.Error}");
        });

        return cmd;
    }

    private static Command CreateSignaturesCommand()
    {
        var cmd = new Command("signatures", "Display certificate fingerprints (SHA-1, SHA-256, MD5) for a keystore.\n\nExample:\n  maui-sherpa android keystores signatures ./release.keystore --storepass mypassword");

        var pathArg = new Argument<string>("path") { Description = "Keystore file path" };
        var storepassOpt = new Option<string>("--storepass") { Description = "Keystore password", Required = true };
        var aliasOpt = new Option<string?>("--alias") { Description = "Specific key alias (shows all if omitted)" };

        cmd.Add(pathArg);
        cmd.Add(storepassOpt);
        cmd.Add(aliasOpt);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var path = parseResult.GetValue(pathArg);
            var storepass = parseResult.GetValue(storepassOpt);
            var alias = parseResult.GetValue(aliasOpt);
            var json = parseResult.GetValue(CliOptions.Json);
            var keytool = await FindKeytoolAsync();
            if (keytool is null) { Output.WriteError("keytool not found."); return; }

            var args = $"-list -v -keystore \"{path}\" -storepass \"{storepass}\"";
            if (!string.IsNullOrEmpty(alias))
                args += $" -alias {alias}";

            var result = await ProcessRunner.RunAsync(keytool, args);
            if (result.ExitCode != 0)
            {
                Output.WriteError($"Failed: {result.Error}");
                return;
            }

            if (json)
            {
                var fingerprints = ParseFingerprints(result.Output);
                Output.WriteJson(new { fingerprints });
            }
            else
            {
                Console.WriteLine(result.Output);
            }
        });

        return cmd;
    }

    private static List<object> ParseFingerprints(string output)
    {
        var fingerprints = new List<object>();
        string? currentAlias = null;
        string? sha1 = null, sha256 = null, md5 = null;

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Alias name:"))
                currentAlias = trimmed["Alias name:".Length..].Trim();
            else if (trimmed.StartsWith("SHA1:"))
                sha1 = trimmed["SHA1:".Length..].Trim();
            else if (trimmed.StartsWith("SHA256:"))
                sha256 = trimmed["SHA256:".Length..].Trim();
            else if (trimmed.StartsWith("MD5:"))
                md5 = trimmed["MD5:".Length..].Trim();

            if (currentAlias is not null && (sha1 is not null || sha256 is not null))
            {
                fingerprints.Add(new { alias = currentAlias, sha1, sha256, md5 });
                currentAlias = null; sha1 = null; sha256 = null; md5 = null;
            }
        }

        return fingerprints;
    }
}
