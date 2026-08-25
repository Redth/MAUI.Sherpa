using System.CommandLine;
using MauiSherpa.Bundles;
using MauiSherpa.Cli.Helpers;

namespace MauiSherpa.Cli.Commands;

public static class BundleCommand
{
    private const string PasswordEnvironmentVariable = "SHERPA_PACK_PASSWORD";
    private const string LegacyPasswordEnvironmentVariable = "SHERPA_BUNDLE_PASSWORD";

    public static Command Create()
    {
        var command = new Command(
            "pack",
            "Validate and run encrypted Expedition Packs in CI.");
        command.Aliases.Add("bundle");
        command.Add(CreateValidateCommand());
        command.Add(CreateSplitCommand());
        command.Add(CreateExecutionCommand("install", [BundlePhase.Install]));
        command.Add(CreateExecutionCommand("build", [BundlePhase.Build]));
        command.Add(CreateExecutionCommand("deploy", [BundlePhase.Deploy]));
        command.Add(CreateExecutionCommand("run", []));
        return command;
    }

    private static Command CreateValidateCommand()
    {
        var command = new Command("validate", "Decrypt and validate a .sherpapack without executing it.");
        var bundleArgument = CreateBundleArgument();
        var passwordStdin = CreatePasswordStdinOption();
        var fromEnvironment = CreateFromEnvironmentOption();
        var environmentPrefix = CreateEnvironmentPrefixOption();
        command.Add(bundleArgument);
        command.Add(passwordStdin);
        command.Add(fromEnvironment);
        command.Add(environmentPrefix);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var structured = UsesStructuredOutput(parseResult);
            try
            {
                var bundle = await ReadBundleAsync(
                    parseResult.GetValue(bundleArgument)!,
                    parseResult.GetValue(passwordStdin),
                    parseResult.GetValue(fromEnvironment),
                    parseResult.GetValue(environmentPrefix)!,
                    cancellationToken);
                if (structured)
                {
                    WriteStructuredSuccess("pack.validate", new
                    {
                        valid = true,
                        bundle.Name,
                        bundle.Version,
                        environments = bundle.Environments.Keys,
                        platforms = bundle.Environments.ToDictionary(
                            pair => pair.Key,
                            pair => pair.Value.Platforms.Keys)
                    });
                }
                else
                {
                    Output.WriteSuccess(
                        $"Expedition Pack '{bundle.Name}' is ready ({bundle.Environments.Count} environment(s)).");
                }
                return 0;
            }
            catch (OperationCanceledException)
            {
                WriteFailure(
                    structured,
                    "pack.validate",
                    130,
                    "Expedition Pack inspection was cancelled.");
                return 130;
            }
            catch (Exception ex)
            {
                WriteFailure(structured, "pack.validate", 2, ex.Message);
                return 2;
            }
        });
        return command;
    }

    private static Command CreateExecutionCommand(
        string name,
        IReadOnlyList<BundlePhase> fixedPhases)
    {
        var command = new Command(name, name == "run"
            ? "Execute selected bundle phases in install/build/deploy order."
            : $"Execute the Expedition Pack {name} phase.");
        var bundleArgument = CreateBundleArgument();
        var environmentOption = new Option<string[]>("--environment", "-e")
        {
            Description = "Expedition Pack environment to execute. May be repeated, but all values must match.",
            AllowMultipleArgumentsPerToken = true,
            Required = true
        };
        var platformOption = new Option<string[]>("--platform", "-p")
        {
            Description = "Platform(s) to execute. Repeat for multiple.",
            AllowMultipleArgumentsPerToken = true
        };
        var phaseOption = new Option<string[]>("--phase")
        {
            Description = "Phase(s) for 'run'. Defaults to install, build, and deploy.",
            AllowMultipleArgumentsPerToken = true
        };
        var projectOption = new Option<string?>("--project")
        {
            Description = "Project path relative to the source directory."
        };
        var artifactOption = new Option<string?>("--artifact")
        {
            Description = "Existing artifact path for a deploy-only invocation."
        };
        var sourceOption = new Option<string>("--source")
        {
            Description = "Project source directory.",
            DefaultValueFactory = _ => Directory.GetCurrentDirectory()
        };
        var outputOption = new Option<string?>("--output")
        {
            Description = "Persistent artifact output directory."
        };
        var variableOption = new Option<string[]>("--variable")
        {
            Description = "Variable override in NAME=VALUE form. Repeat for multiple.",
            AllowMultipleArgumentsPerToken = true
        };
        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Validate and print the execution plan without invoking external tools."
        };
        var parallelOption = new Option<bool>("--parallel")
        {
            Description = "Execute platforms concurrently in isolated staging workspaces."
        };
        var passwordStdin = CreatePasswordStdinOption();
        var fromEnvironment = CreateFromEnvironmentOption();
        var environmentPrefix = CreateEnvironmentPrefixOption();

        command.Add(bundleArgument);
        command.Add(environmentOption);
        command.Add(platformOption);
        command.Add(projectOption);
        command.Add(artifactOption);
        command.Add(sourceOption);
        command.Add(outputOption);
        command.Add(variableOption);
        command.Add(dryRunOption);
        command.Add(parallelOption);
        command.Add(passwordStdin);
        command.Add(fromEnvironment);
        command.Add(environmentPrefix);
        if (fixedPhases.Count == 0)
            command.Add(phaseOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var structured = UsesStructuredOutput(parseResult);
            try
            {
                var bundle = await ReadBundleAsync(
                    parseResult.GetValue(bundleArgument)!,
                    parseResult.GetValue(passwordStdin),
                    parseResult.GetValue(fromEnvironment),
                    parseResult.GetValue(environmentPrefix)!,
                    cancellationToken);
                var environment = ParseSingleValue(
                    parseResult.GetValue(environmentOption) ?? [],
                    "environment");
                var platforms = ParseEnums<BundlePlatform>(
                    parseResult.GetValue(platformOption) ?? [],
                    "platform");
                var phases = fixedPhases.Count > 0
                    ? fixedPhases
                    : ParseEnums<BundlePhase>(
                        parseResult.GetValue(phaseOption) ?? [],
                        "phase",
                        [BundlePhase.Install, BundlePhase.Build, BundlePhase.Deploy]);
                var variables = ParseVariables(parseResult.GetValue(variableOption) ?? []);
                var processRunner = new BundleProcessRunner();
                var providers = BundleDeploymentProviderFactory.CreateAll(processRunner);
                var runner = new SherpaBundleRunner(
                    new BundleToolchainInstaller(processRunner),
                    new BundleBuildService(processRunner),
                    new BundleDeploymentRegistry(providers));
                var progress = structured
                    ? null
                    : new Progress<string>(Output.WriteInfo);
                var result = await runner.RunAsync(
                    bundle,
                    new BundleRunRequest
                    {
                        Environment = environment,
                        Platforms = platforms,
                        Phases = phases,
                        SourceDirectory = parseResult.GetValue(sourceOption)!,
                        OutputDirectory = parseResult.GetValue(outputOption),
                        Project = parseResult.GetValue(projectOption),
                        ArtifactPath = parseResult.GetValue(artifactOption),
                        VariableOverrides = variables,
                        DryRun = parseResult.GetValue(dryRunOption),
                        Parallel = parseResult.GetValue(parallelOption)
                    },
                    progress,
                    cancellationToken);

                WriteResult($"pack.{name}", result, structured);
                return result.Succeeded ? 0 : 1;
            }
            catch (OperationCanceledException)
            {
                WriteFailure(
                    structured,
                    $"pack.{name}",
                    130,
                    "Expedition Pack execution was cancelled.");
                return 130;
            }
            catch (Exception ex)
            {
                WriteFailure(structured, $"pack.{name}", 2, ex.Message);
                return 2;
            }
        });
        return command;
    }

    private static Argument<string?> CreateBundleArgument() => new("pack")
    {
        Description = "Path to the encrypted .sherpapack file.",
        Arity = ArgumentArity.ZeroOrOne
    };

    private static Option<bool> CreatePasswordStdinOption() => new("--password-stdin")
    {
        Description = $"Read the Expedition Pack password from standard input instead of {PasswordEnvironmentVariable}."
    };

    private static Option<bool> CreateFromEnvironmentOption() => new("--from-env")
    {
        Description = "Read the encrypted pack from SHERPA_PACK or validated SHERPA_PACK_1..N chunks."
    };

    private static Option<string> CreateEnvironmentPrefixOption() => new("--pack-env-prefix")
    {
        Description = "Environment-variable prefix used with --from-env.",
        DefaultValueFactory = _ => SherpaPackText.DefaultPrefix
    };

    private static async Task<SherpaBundle> ReadBundleAsync(
        string? path,
        bool passwordStdin,
        bool fromEnvironment,
        string environmentPrefix,
        CancellationToken cancellationToken)
    {
        var password = passwordStdin
            ? await Console.In.ReadLineAsync(cancellationToken)
            : Environment.GetEnvironmentVariable(PasswordEnvironmentVariable)
              ?? Environment.GetEnvironmentVariable(LegacyPasswordEnvironmentVariable);
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException(
                $"Set {PasswordEnvironmentVariable} or pass --password-stdin.");
        }

        if (fromEnvironment && !string.IsNullOrWhiteSpace(path))
            throw new BundleValidationException(["Specify either a pack path or --from-env, not both."]);
        if (!fromEnvironment && string.IsNullOrWhiteSpace(path))
            throw new BundleValidationException(["Specify a pack path or pass --from-env."]);

        var bytes = fromEnvironment
            ? SherpaPackText.AssembleFromEnvironment(
                Environment.GetEnvironmentVariable,
                environmentPrefix)
            : await File.ReadAllBytesAsync(Path.GetFullPath(path!), cancellationToken);
        return SherpaBundleFile.Decrypt(bytes, password);
    }

    private static Command CreateSplitCommand()
    {
        var command = new Command(
            "split",
            "Encode an encrypted Expedition Pack as one GitHub secret or validated numbered chunks.");
        var packArgument = new Argument<string>("pack")
        {
            Description = "Path to the encrypted .sherpapack file."
        };
        var prefixOption = new Option<string>("--prefix")
        {
            Description = "Secret/environment variable prefix.",
            DefaultValueFactory = _ => SherpaPackText.DefaultPrefix
        };
        var maximumLengthOption = new Option<int>("--max-value-length")
        {
            Description = "Maximum characters per secret value.",
            DefaultValueFactory = _ => SherpaPackText.DefaultMaximumValueLength
        };
        var outputOption = new Option<string?>("--output")
        {
            Description = "Write one file per secret plus a GitHub Actions env snippet instead of printing values."
        };
        command.Add(packArgument);
        command.Add(prefixOption);
        command.Add(maximumLengthOption);
        command.Add(outputOption);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var structured = UsesStructuredOutput(parseResult);
            try
            {
                var path = Path.GetFullPath(parseResult.GetValue(packArgument)!);
                var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                if (!SherpaBundleFile.HasValidHeader(bytes))
                    throw new InvalidDataException("The input is not an encrypted Expedition Pack.");

                var prefix = parseResult.GetValue(prefixOption)!;
                var parts = SherpaPackText.Split(
                    bytes,
                    prefix,
                    parseResult.GetValue(maximumLengthOption));
                var output = parseResult.GetValue(outputOption);
                if (!string.IsNullOrWhiteSpace(output))
                    await WriteSplitFilesAsync(parts, output, cancellationToken);

                if (structured)
                {
                    WriteStructuredSuccess("pack.split", new
                    {
                        partCount = parts.Count,
                        prefix,
                        outputDirectory = string.IsNullOrWhiteSpace(output)
                            ? null
                            : Path.GetFullPath(output),
                        parts = parts.Select(part => new
                        {
                            part.Name,
                            value = string.IsNullOrWhiteSpace(output) ? part.Value : null,
                            file = string.IsNullOrWhiteSpace(output)
                                ? null
                                : Path.Combine(Path.GetFullPath(output), $"{part.Name}.txt")
                        })
                    });
                }
                else if (string.IsNullOrWhiteSpace(output))
                {
                    foreach (var part in parts)
                        Console.WriteLine($"{part.Name}={part.Value}");
                }
                else
                {
                    Output.WriteSuccess(
                        $"Wrote {parts.Count} Expedition Pack secret file(s) to {Path.GetFullPath(output)}.");
                }
                return 0;
            }
            catch (OperationCanceledException)
            {
                WriteFailure(structured, "pack.split", 130, "Expedition Pack splitting was cancelled.");
                return 130;
            }
            catch (Exception ex)
            {
                WriteFailure(structured, "pack.split", 2, ex.Message);
                return 2;
            }
        });
        return command;
    }

    private static async Task WriteSplitFilesAsync(
        IReadOnlyList<SherpaPackTextPart> parts,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        foreach (var part in parts)
        {
            var path = Path.Combine(directory, $"{part.Name}.txt");
            await File.WriteAllTextAsync(
                path,
                part.Value,
                cancellationToken);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        var lines = new List<string>
        {
            "env:"
        };
        lines.AddRange(parts.Select(part =>
            $"  {part.Name}: ${{{{ secrets.{part.Name} }}}}"));
        lines.Add("  SHERPA_PACK_PASSWORD: ${{ secrets.SHERPA_PACK_PASSWORD }}");
        await File.WriteAllLinesAsync(
            Path.Combine(directory, "github-actions-env.yml"),
            lines,
            cancellationToken);
    }

    private static IReadOnlyList<T> ParseEnums<T>(
        IReadOnlyList<string> values,
        string label,
        IReadOnlyList<T>? defaults = null)
        where T : struct, Enum
    {
        if (values.Count == 0)
            return defaults ?? [];
        var parsed = new List<T>();
        foreach (var value in values)
        {
            if (!Enum.TryParse<T>(value, ignoreCase: true, out var item))
                throw new BundleValidationException([$"Unknown {label} '{value}'."]);
            parsed.Add(item);
        }
        return parsed.Distinct().ToArray();
    }

    private static string ParseSingleValue(
        IReadOnlyList<string> values,
        string label)
    {
        if (values.Count == 0)
            throw new BundleValidationException([$"Specify --{label}."]);

        var distinct = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return distinct.Length switch
        {
            1 => distinct[0],
            0 => throw new BundleValidationException([$"Specify --{label}."]),
            _ => throw new BundleValidationException([$"Specify only one unique {label}."])
        };
    }

    private static IReadOnlyDictionary<string, string> ParseVariables(IEnumerable<string> values)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            var separator = value.IndexOf('=');
            if (separator <= 0)
                throw new BundleValidationException([$"Variable override '{value}' must use NAME=VALUE."]);
            result[value[..separator]] = value[(separator + 1)..];
        }
        return result;
    }

    private static void WriteResult(string commandName, BundleRunResult result, bool structured)
    {
        if (structured)
        {
            if (result.Succeeded)
            {
                WriteStructuredSuccess(commandName, result);
            }
            else
            {
                WriteStructuredError(
                    commandName,
                    1,
                    "One or more Expedition Pack phases failed.",
                    result);
            }
            return;
        }

        foreach (var platform in result.Platforms)
        {
            foreach (var phase in platform.Phases)
            {
                var message = $"{platform.Platform} {phase.Phase}: {phase.Message}";
                if (phase.Succeeded)
                    Output.WriteSuccess(message);
                else
                    Output.WriteError(message);
            }
            foreach (var artifact in platform.Artifacts)
                Output.WriteInfo($"Artifact: {artifact.Path}");
        }
    }

    private static bool UsesStructuredOutput(ParseResult parseResult) =>
        parseResult.GetValue(CliOptions.Json) || parseResult.GetValue(CliOptions.Agent);

    private static void WriteFailure(bool structured, string commandName, int exitCode, string message)
    {
        if (structured)
        {
            WriteStructuredError(commandName, exitCode, message);
            return;
        }

        Output.WriteError(message);
    }

    private static void WriteStructuredSuccess(string commandName, object result) =>
        Output.WriteJson(new
        {
            status = "ok",
            command = commandName,
            exitCode = 0,
            result
        });

    private static void WriteStructuredError(
        string commandName,
        int exitCode,
        string message,
        object? result = null) =>
        Output.WriteJson(new
        {
            status = "error",
            command = commandName,
            exitCode,
            error = new
            {
                message
            },
            result
        });
}
