using System.Text;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public static class WindowsElevatedProcessScriptBuilder
{
    public static string Build(ProcessRequest request, string outputFile)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFile);

        var script = new StringBuilder();
        script.AppendLine("$ErrorActionPreference = 'Stop'");
        script.AppendLine($"Set-Location -LiteralPath {Quote(request.WorkingDirectory ?? Environment.CurrentDirectory)}");

        if (request.Environment != null)
        {
            foreach (var (key, value) in request.Environment)
                script.AppendLine($"Set-Item -LiteralPath {Quote($"Env:{key}")} -Value {Quote(value)}");
        }

        script.AppendLine("$exitCode = 1");
        script.AppendLine("try {");
        script.Append("  & ").Append(Quote(request.Command));
        foreach (var argument in request.Arguments)
            script.Append(' ').Append(Quote(argument));
        script.Append(" 2>&1 | Tee-Object -FilePath ").AppendLine(Quote(outputFile));
        script.AppendLine("  $exitCode = if ($null -eq $LASTEXITCODE) { if ($?) { 0 } else { 1 } } else { $LASTEXITCODE }");
        script.AppendLine("}");
        script.AppendLine("catch {");
        script.AppendLine($"  $_ | Out-String | Add-Content -LiteralPath {Quote(outputFile)}");
        script.AppendLine("}");
        script.AppendLine("finally {");
        script.AppendLine($"  Add-Content -LiteralPath {Quote(outputFile)} -Value \"__EXIT_CODE__:$exitCode\"");
        script.AppendLine("}");
        script.AppendLine("exit $exitCode");
        return script.ToString();
    }

    private static string Quote(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
}
