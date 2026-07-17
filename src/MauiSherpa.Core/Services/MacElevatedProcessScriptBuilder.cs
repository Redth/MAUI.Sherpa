using System.Text;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public static class MacElevatedProcessScriptBuilder
{
    public static string Build(ProcessRequest request, string outputFile)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFile);

        var command = string.Join(
            " ",
            new[] { request.Command }
                .Concat(request.Arguments)
                .Select(EscapeForShell));
        if (request.UsePseudoTerminal)
            command = $"/usr/bin/script -q /dev/null {command}";

        var script = new StringBuilder();
        script.AppendLine("#!/bin/bash");
        script.AppendLine("set -o pipefail");
        script.AppendLine($"exec > >(tee -a {EscapeForShell(outputFile)}) 2>&1");

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
            script.AppendLine($"cd {EscapeForShell(request.WorkingDirectory)} || exit 1");

        if (request.Environment != null)
        {
            foreach (var (name, value) in request.Environment.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                if (!IsValidEnvironmentName(name))
                    throw new ArgumentException($"'{name}' is not a valid environment variable name.");
                script.AppendLine($"export {name}={EscapeForShell(value)}");
            }
        }

        script.AppendLine(command);
        script.AppendLine("EXIT_CODE=$?");
        script.AppendLine($"printf '\\n__EXIT_CODE__:%s\\n' \"$EXIT_CODE\" >> {EscapeForShell(outputFile)}");
        script.AppendLine("exit $EXIT_CODE");
        return script.ToString();
    }

    private static bool IsValidEnvironmentName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        (char.IsLetter(name[0]) || name[0] == '_') &&
        name.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');

    private static string EscapeForShell(string value) =>
        $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";
}
