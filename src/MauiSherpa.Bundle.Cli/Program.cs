using System.Reflection;
using MauiSherpa.Bundle.Cli;
using MauiSherpa.Bundle.Loading;
using MauiSherpa.Bundle.Pipeline;

var parsed = CommandLineParser.Parse(args);

if (parsed.ShowHelp)
{
    HelpText.Print(Console.Out);
    return 0;
}

if (parsed.ShowVersion)
{
    var asm = Assembly.GetExecutingAssembly();
    var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                  ?? asm.GetName().Version?.ToString() ?? "unknown";
    Console.WriteLine($"sherpacli {version}");
    return 0;
}

if (parsed.Error is not null || parsed.Options is null)
{
    Console.Error.WriteLine($"✗ {parsed.Error ?? "Invalid arguments."}");
    Console.Error.WriteLine();
    HelpText.Print(Console.Error);
    return 2;
}

var options = parsed.Options;
var log = new ConsoleLog(quiet: options.JsonOnly);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    var pipeline = new SherpaPipeline();
    var result = await pipeline.RunAsync(options, log, cts.Token);

    // §7 output: JSON to stdout + sidecar + CI env vars.
    Console.Out.WriteLine(SherpaOutputWriter.Serialize(result));
    var sidecar = SherpaOutputWriter.WriteSidecar(result, options.BundlePath);
    log.Info($"Wrote {sidecar}");
    SherpaOutputWriter.EmitEnvironment(result, Console.Out);

    // Fail the run if any deploy failed.
    var failedDeploys = result.Platforms.Values
        .SelectMany(p => p.Deploys)
        .Count(d => string.Equals(d.Status, "Failed", StringComparison.OrdinalIgnoreCase));
    if (failedDeploys > 0)
    {
        log.Error($"{failedDeploys} deploy target(s) failed.");
        return 1;
    }

    log.Success("Done.");
    return 0;
}
catch (SherpaBundleException ex)
{
    log.Error(ex.Message);
    return 1;
}
catch (OperationCanceledException)
{
    log.Error("Cancelled.");
    return 130;
}
catch (Exception ex)
{
    log.Error($"Unexpected error: {ex}");
    return 1;
}
