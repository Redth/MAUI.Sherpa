namespace MauiSherpa.Bundle.Cli;

internal static class HelpText
{
    public static void Print(TextWriter w)
    {
        w.WriteLine("""
        sherpacli — drive the setup → build → deploy pipeline for a .NET MAUI project
                    from a .sherpabundle file.

        USAGE:
          sherpacli <bundle>.sherpabundle -environment:<name> [options]

        REQUIRED:
          <bundle>                 Path to the .sherpabundle file (positional).
          -environment:<name>      Environment block to apply (case-insensitive).

        OPTIONS:
          -platform:<list>         Comma-separated: android,ios,macos,maccatalyst,windows.
                                   Defaults to every platform defined in the environment.
          -step:<list>             Comma-separated: setup,build,deploy,all (default: all).
                                   Always run in order: setup → build → deploy.
          -project:<path>          Target .csproj. Inferred from the CWD if omitted.
          -variable:"name=value"   Set a ${name} substitution variable. Repeatable.
          -replacetoken:"name=val" Override a ReplaceTokens entry. Repeatable.
          -msbuild:"prop=value"    Override an MSBuildProperties entry. Repeatable.
          -j, --json               Emit only the JSON result on stdout (quiet logs).
          -h, --help               Show this help.
          --version                Show version.

        EXAMPLE:
          sherpacli app.sherpabundle -environment:production -platform:android,ios \
            -variable:"buildNumber=1234" -replacetoken:"Hello=Android World 2"
        """);
    }
}
