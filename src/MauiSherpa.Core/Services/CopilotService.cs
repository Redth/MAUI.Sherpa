using GitHub.Copilot.SDK;
using MauiSherpa.Core.Interfaces;
using System.Text.Json;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Service for interacting with GitHub Copilot CLI via the SDK
/// </summary>
public class CopilotService : ICopilotService, IAsyncDisposable
{
    private sealed record ResolvedPermissionRequest(
        string ToolCallId,
        string ToolName,
        string ToolDescription,
        bool IsReadOnly,
        string? Command,
        string? Path);

    private readonly ILoggingService _logger;
    private readonly ICopilotToolsService _toolsService;
    private readonly string _skillsPath;
    private readonly string _copilotWorkingDirectory;
    private readonly SemaphoreSlim _clientGate = new(1, 1);
    private readonly List<CopilotChatMessage> _messages = new();
    private readonly Dictionary<string, string> _toolCallIdToName = new(); // Track callId -> toolName mapping
    
    private CopilotClient? _client;
    private CopilotSession? _session;
    private IDisposable? _eventSubscription;
    private CopilotAvailability? _cachedAvailability;

    public bool IsConnected => _client?.State == ConnectionState.Connected;
    public string? CurrentSessionId => _session?.SessionId;
    public IReadOnlyList<CopilotChatMessage> Messages => _messages.AsReadOnly();
    public CopilotAvailability? CachedAvailability => _cachedAvailability;

    public event Action<string>? OnAssistantMessage;
    public event Action<string>? OnAssistantDelta;
    public event Action? OnSessionIdle;
    public event Action<string>? OnError;
    public event Action<string, string>? OnToolStart;
    public event Action<string, string>? OnToolComplete;
    public event Action<string>? OnReasoningStart;
    public event Action<string, string>? OnReasoningDelta;
    public event Action? OnTurnStart;
    public event Action? OnTurnEnd;
    public event Action<string>? OnIntentChanged;
    public event Action<CopilotUsageInfo>? OnUsageInfoChanged;
    public event Action<CopilotSessionError>? OnSessionError;
    
    public Func<ToolPermissionRequest, Task<ToolPermissionResult>>? PermissionHandler { get; set; }

    public CopilotService(ILoggingService logger, ICopilotToolsService toolsService)
    {
        _logger = logger;
        _toolsService = toolsService;
        _skillsPath = GetSkillsPath();
        _copilotWorkingDirectory = GetCopilotWorkingDirectory();
        _logger.LogInformation($"Copilot skills path: {_skillsPath}");
        _logger.LogInformation($"Copilot working directory: {_copilotWorkingDirectory}");
    }

    private static string GetSkillsPath()
    {
        // Get the directory where the app is running from
        var baseDir = AppContext.BaseDirectory;
        
        // For MAUI apps, Raw assets are placed in different locations:
        // - Mac Catalyst: MauiSherpa.app/Contents/Resources/Skills
        // - macOS AppKit: MauiSherpa.app/Contents/Resources/Skills
        // - Windows: alongside the executable
        
        // First check Resources folder (Mac Catalyst / macOS bundle structure)
        var resourcesPath = Path.Combine(baseDir, "..", "Resources", "Skills");
        if (Directory.Exists(resourcesPath))
        {
            return Path.GetFullPath(resourcesPath);
        }
        
        // Check directly in base dir (Windows or development)
        var skillsPath = Path.Combine(baseDir, "Skills");
        if (Directory.Exists(skillsPath))
        {
            return skillsPath;
        }

        // Try parent directories (for development scenarios)
        var parent = Path.GetDirectoryName(baseDir);
        while (parent != null)
        {
            var testPath = Path.Combine(parent, "Skills");
            if (Directory.Exists(testPath))
            {
                return testPath;
            }
            parent = Path.GetDirectoryName(parent);
        }

        // No Skills directory found — fall back to base directory so the process
        // can still start (Skills are optional, Copilot works without them)
        return baseDir;
    }

    private static string GetCopilotWorkingDirectory()
    {
        var path = Path.Combine(AppDataPath.GetAppDataDirectory(), "copilot");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string GetUserHomeDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(home) ? Environment.CurrentDirectory : home;
    }

    internal static string BuildLaunchPath(string? currentPath = null, IEnumerable<string>? extraDirectories = null)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var orderedPaths = new List<string>();

        static string NormalizeDirectory(string path)
        {
            try
            {
                return Path.GetFullPath(path.Trim());
            }
            catch
            {
                return path.Trim();
            }
        }

        void AddDirectory(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return;

            var normalized = NormalizeDirectory(candidate);
            if (!Directory.Exists(normalized) || !seen.Add(normalized))
                return;

            orderedPaths.Add(normalized);
        }

        foreach (var dir in (currentPath ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            AddDirectory(dir);
        }

        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsLinux())
        {
            var home = GetUserHomeDirectory();

            AddDirectory("/opt/homebrew/bin");
            AddDirectory("/usr/local/bin");
            AddDirectory("/opt/local/bin");
            AddDirectory("/usr/bin");
            AddDirectory("/bin");
            AddDirectory("/usr/sbin");
            AddDirectory("/sbin");

            AddDirectory(Path.Combine(home, ".local", "bin"));
            AddDirectory(Path.Combine(home, ".npm-global", "bin"));
            AddDirectory(Path.Combine(home, ".yarn", "bin"));
            AddDirectory(Path.Combine(home, ".volta", "bin"));
            AddDirectory(Path.Combine(home, ".asdf", "shims"));
            AddDirectory(Path.Combine(home, ".local", "share", "fnm", "aliases", "default", "bin"));
            AddDirectory(Path.Combine(home, ".fnm", "aliases", "default", "bin"));
            AddDirectory(Path.Combine(home, "bin"));
        }

        if (extraDirectories != null)
        {
            foreach (var dir in extraDirectories)
                AddDirectory(dir);
        }

        return string.Join(Path.PathSeparator, orderedPaths);
    }

    private static Dictionary<string, string> BuildCliEnvironment(string? cliPath = null)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var home = GetUserHomeDirectory();

        if (!string.IsNullOrWhiteSpace(home))
        {
            environment["HOME"] = home;
            environment["USERPROFILE"] = home;

            if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsLinux())
            {
                var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                if (string.IsNullOrWhiteSpace(xdgConfigHome))
                    xdgConfigHome = Path.Combine(home, ".config");

                environment["XDG_CONFIG_HOME"] = xdgConfigHome;

                var ghConfigDir = Environment.GetEnvironmentVariable("GH_CONFIG_DIR");
                if (string.IsNullOrWhiteSpace(ghConfigDir))
                    ghConfigDir = Path.Combine(xdgConfigHome, "gh");

                environment["GH_CONFIG_DIR"] = ghConfigDir;
            }
        }

        var extraDirs = !string.IsNullOrWhiteSpace(cliPath)
            ? new[] { Path.GetDirectoryName(cliPath)! }
            : Array.Empty<string>();

        var launchPath = BuildLaunchPath(extraDirectories: extraDirs);
        if (!string.IsNullOrWhiteSpace(launchPath))
            environment["PATH"] = launchPath;

        foreach (var variable in new[] { "TMPDIR", "TMP", "TEMP", "LANG", "LC_ALL", "SHELL", "USER", "LOGNAME" })
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
                environment[variable] = value;
        }

        return environment;
    }

    /// <summary>
    /// Resolves the Copilot CLI binary path. Prefer the user's installed CLI when available
    /// so release builds can reuse the normal auth state and avoid bundle-signing edge cases,
    /// then fall back to the bundled runtimes copy.
    /// </summary>
    internal static string? ResolveCopilotCliPath(string? pathEnv = null, string? baseDirectory = null, IEnumerable<string>? additionalSearchPaths = null)
    {
        var rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        var binaryName = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows) ? "copilot.exe" : "copilot";

        foreach (var candidate in additionalSearchPaths ?? Enumerable.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                return candidate;
        }

        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            foreach (var candidate in new[]
            {
                Path.Combine(programFiles, "GitHub Copilot", binaryName),
                Path.Combine(localAppData, "Microsoft", "WinGet", "Links", binaryName)
            })
            {
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        else
        {
            var home = GetUserHomeDirectory();
            foreach (var candidate in new[]
            {
                "/opt/homebrew/bin/copilot",
                "/usr/local/bin/copilot",
                "/opt/local/bin/copilot",
                Path.Combine(home, ".local", "bin", "copilot"),
                Path.Combine(home, ".npm-global", "bin", "copilot"),
                Path.Combine(home, ".yarn", "bin", "copilot"),
                Path.Combine(home, ".volta", "bin", "copilot"),
                Path.Combine(home, ".asdf", "shims", "copilot"),
                Path.Combine(home, "bin", "copilot")
            })
            {
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        var pathDirs = BuildLaunchPath(pathEnv)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var dir in pathDirs)
        {
            var candidate = Path.Combine(dir, binaryName);
            if (File.Exists(candidate))
                return candidate;
        }

        var candidateBaseDirs = new[]
        {
            baseDirectory ?? AppContext.BaseDirectory,
            Path.GetFullPath(Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "..")),
            Path.GetFullPath(Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "..", "MonoBundle"))
        }
        .Distinct(StringComparer.Ordinal)
        .ToArray();

        foreach (var root in candidateBaseDirs)
        {
            var bundledPath = Path.Combine(root, "runtimes", rid, "native", binaryName);
            if (File.Exists(bundledPath))
                return bundledPath;

            if (rid.StartsWith("maccatalyst-", StringComparison.OrdinalIgnoreCase))
            {
                var osxRid = rid.Replace("maccatalyst-", "osx-");
                var osxPath = Path.Combine(root, "runtimes", osxRid, "native", binaryName);
                if (File.Exists(osxPath))
                    return osxPath;
            }
        }

        return null;
    }

    public async Task<CopilotAvailability> CheckAvailabilityAsync(bool forceRefresh = false)
    {
        // Return cached result if available and not forcing refresh
        if (!forceRefresh && _cachedAvailability != null)
        {
            _logger.LogInformation("Returning cached Copilot availability");
            return _cachedAvailability;
        }

        await _clientGate.WaitAsync();
        try
        {
            if (!forceRefresh && _cachedAvailability != null)
            {
                _logger.LogInformation("Returning cached Copilot availability after synchronization");
                return _cachedAvailability;
            }

            CopilotClient? tempClient = null;
            var tempClientStarted = false;
            try
            {
                _logger.LogInformation("Checking Copilot availability via SDK...");

                var cliPath = ResolveCopilotCliPath();
                var cliEnvironment = BuildCliEnvironment(cliPath);

                if (cliPath != null)
                    _logger.LogInformation($"Resolved Copilot CLI path: {cliPath}");
                else
                    _logger.LogWarning("Could not resolve Copilot CLI path from bundle, well-known locations, or PATH");

                var options = new CopilotClientOptions
                {
                    AutoStart = true,
                    CliPath = cliPath,
                    Cwd = _copilotWorkingDirectory,
                    Environment = cliEnvironment
                };

                tempClient = new CopilotClient(options);
                await tempClient.StartAsync();
                tempClientStarted = true;

                var statusResponse = await tempClient.GetStatusAsync();
                var version = statusResponse?.Version;
                _logger.LogInformation($"Copilot CLI version: {version}");

                var authResponse = await tempClient.GetAuthStatusAsync();

                if (authResponse == null || !authResponse.IsAuthenticated)
                {
                    var statusMsg = authResponse?.StatusMessage ?? "Not logged in to GitHub Copilot";
                    _logger.LogWarning($"Copilot not authenticated: {statusMsg}");
                    _cachedAvailability = new CopilotAvailability(
                        IsInstalled: true,
                        IsAuthenticated: false,
                        Version: version,
                        Login: authResponse?.Login,
                        ErrorMessage: statusMsg
                    );
                    return _cachedAvailability;
                }

                _logger.LogInformation($"Copilot authenticated as {authResponse.Login}");

                if (_client == null)
                {
                    _client = tempClient;
                    tempClient = null;
                    tempClientStarted = false;
                    _logger.LogInformation("Reusing availability-check Copilot client for the next session");
                }

                _cachedAvailability = new CopilotAvailability(
                    IsInstalled: true,
                    IsAuthenticated: true,
                    Version: version,
                    Login: authResponse.Login,
                    ErrorMessage: null
                );
                return _cachedAvailability;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error checking Copilot availability: {ex.Message}", ex);

                var isNotInstalled = ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                                     ex.Message.Contains("No such file", StringComparison.OrdinalIgnoreCase) ||
                                     ex.Message.Contains("cannot find", StringComparison.OrdinalIgnoreCase) ||
                                     ex is System.ComponentModel.Win32Exception;

                _cachedAvailability = new CopilotAvailability(
                    IsInstalled: !isNotInstalled,
                    IsAuthenticated: false,
                    Version: null,
                    Login: null,
                    ErrorMessage: isNotInstalled
                        ? "GitHub Copilot CLI is not installed"
                        : ex.Message
                );
                return _cachedAvailability;
            }
            finally
            {
                if (tempClient != null)
                {
                    if (tempClientStarted)
                    {
                        try
                        {
                            await tempClient.StopAsync();
                        }
                        catch (Exception stopEx)
                        {
                            _logger.LogWarning($"Failed to stop temporary Copilot client cleanly: {stopEx.Message}");

                            try
                            {
                                await tempClient.ForceStopAsync();
                            }
                            catch (Exception forceStopEx)
                            {
                                _logger.LogWarning($"Failed to force-stop temporary Copilot client: {forceStopEx.Message}");
                            }
                        }
                    }

                    await tempClient.DisposeAsync();
                }
            }
        }
        finally
        {
            _clientGate.Release();
        }
    }

    public async Task ConnectAsync()
    {
        await _clientGate.WaitAsync();
        try
        {
            if (_client?.State == ConnectionState.Connected)
            {
                _logger.LogInformation("Already connected to Copilot");
                return;
            }

            if (_client != null)
            {
                _logger.LogWarning("Discarding stale Copilot client before reconnecting");

                try
                {
                    await _client.StopAsync();
                }
                catch
                {
                    try
                    {
                        await _client.ForceStopAsync();
                    }
                    catch
                    {
                    }
                }

                await _client.DisposeAsync();
                _client = null;
            }

            _logger.LogInformation("Connecting to Copilot CLI...");

            var cliPath = ResolveCopilotCliPath();
            var cliEnvironment = BuildCliEnvironment(cliPath);
            if (cliPath != null)
                _logger.LogInformation($"Resolved Copilot CLI path: {cliPath}");

            var options = new CopilotClientOptions
            {
                AutoStart = true,
                UseStdio = true,
                Cwd = _copilotWorkingDirectory,
                LogLevel = "info",
                CliPath = cliPath,
                Environment = cliEnvironment
            };

            _client = new CopilotClient(options);
            await _client.StartAsync();

            _cachedAvailability = _cachedAvailability is null
                ? new CopilotAvailability(true, true, null, null, null)
                : _cachedAvailability with { IsInstalled = true, IsAuthenticated = true, ErrorMessage = null };

            _logger.LogInformation("Connected to Copilot CLI");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to connect to Copilot: {ex.Message}", ex);
            _client = null;
            throw;
        }
        finally
        {
            _clientGate.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        if (_client == null)
        {
            return;
        }

        try
        {
            _logger.LogInformation("Disconnecting from Copilot...");
            
            if (_session != null)
            {
                await EndSessionAsync();
            }

            await _client.StopAsync();
            _client = null;
            
            _logger.LogInformation("Disconnected from Copilot");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error disconnecting from Copilot: {ex.Message}", ex);
            // Force stop if graceful stop fails
            try
            {
                if (_client != null)
                {
                    await _client.ForceStopAsync();
                }
            }
            catch { }
            _client = null;
        }
    }

    public async Task StartSessionAsync(string? model = null, string? systemPrompt = null)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Not connected to Copilot. Call ConnectAsync first.");
        }

        if (_session != null)
        {
            await EndSessionAsync();
        }

        try
        {
            _logger.LogInformation($"Starting Copilot session with model: {model ?? "default"}");
            
            // Get tools from tools service
            var tools = _toolsService.GetTools();
            _logger.LogInformation($"Registering {tools.Count} tools with Copilot session");
            
            // Build system prompt - use provided prompt or fall back to default
            var promptContent = CopilotSystemPromptBuilder.Build(systemPrompt);
            
            var config = new SessionConfig
            {
                Model = model ?? "claude-opus-4.5", // Use Claude Opus 4.5 as default
                Streaming = true,
                Tools = tools.ToList(),
                OnPermissionRequest = HandleSdkPermissionRequest,
                SystemMessage = new SystemMessageConfig
                {
                    Content = promptContent
                }
            };
            
            _logger.LogInformation("System prompt configured for session");
            
            // Add skills directory (the parent folder containing skill folders)
            // Temporarily disabled for debugging - skills may be causing API errors
            var skillsPath = _skillsPath;
            var enableSkills = false; // Set to true to re-enable skills
            if (enableSkills && Directory.Exists(skillsPath))
            {
                config.SkillDirectories = new List<string> { skillsPath };
                _logger.LogInformation($"Adding skills directory: {skillsPath}");
                
                // Log individual skills found for debugging
                foreach (var skillDir in Directory.GetDirectories(skillsPath))
                {
                    if (File.Exists(Path.Combine(skillDir, "SKILL.md")))
                    {
                        _logger.LogInformation($"  Found skill: {Path.GetFileName(skillDir)}");
                    }
                }
            }
            else
            {
                _logger.LogInformation("Skills disabled for this session");
            }

            _session = await _client.CreateSessionAsync(config);
            
            // Subscribe to session events
            _eventSubscription = _session.On(HandleSessionEvent);
            
            _logger.LogInformation($"Session started: {_session.SessionId}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to start session: {ex.Message}", ex);
            throw;
        }
    }
    
    private async Task<PermissionRequestResult> HandleSdkPermissionRequest(PermissionRequest request, PermissionInvocation invocation)
    {
        var resolved = ResolvePermissionRequest(request);

        _logger.LogDebug(
            $"Permission request: Kind={request.Kind}, ToolCallId={resolved.ToolCallId}, ToolName={resolved.ToolName}, SessionId={invocation.SessionId}");
        _logger.LogDebug(
            $"  Resolved permission context: Description={resolved.ToolDescription}, Path={resolved.Path}, Command={resolved.Command}, IsReadOnly={resolved.IsReadOnly}");
        
        // Build a better description using intention or path
        var defaultResult = new ToolPermissionResult(true);
        
        // If we have a permission handler delegate, call it
        if (PermissionHandler != null)
        {
            var permRequest = new ToolPermissionRequest(
                resolved.ToolName,
                resolved.ToolDescription,
                resolved.IsReadOnly,
                defaultResult,
                resolved.Command,
                resolved.Path,
                resolved.ToolCallId);

            _logger.LogDebug($"Calling PermissionHandler for tool: {resolved.ToolName}");
            var result = await PermissionHandler(permRequest);
            _logger.LogDebug($"PermissionHandler returned: IsAllowed={result.IsAllowed}");
            
            if (result.IsAllowed)
            {
                _logger.LogDebug($"Returning approved for tool: {resolved.ToolName}");
                return new PermissionRequestResult { Kind = PermissionRequestResultKind.Approved };
            }

            _logger.LogDebug($"Returning interactive denial for tool: {resolved.ToolName}");
            return new PermissionRequestResult { Kind = PermissionRequestResultKind.DeniedInteractivelyByUser };
        }
        
        // Default: allow read-only tools, deny destructive tools
        if (resolved.IsReadOnly)
        {
            return new PermissionRequestResult { Kind = PermissionRequestResultKind.Approved };
        }
        
        // Default deny for destructive tools if no handler
        return new PermissionRequestResult { Kind = PermissionRequestResultKind.DeniedCouldNotRequestFromUser };
    }

    private ResolvedPermissionRequest ResolvePermissionRequest(PermissionRequest request)
    {
        return request switch
        {
            PermissionRequestShell shell => ResolveShellPermissionRequest(shell),
            PermissionRequestWrite write => ResolveWritePermissionRequest(write),
            PermissionRequestRead read => ResolveReadPermissionRequest(read),
            PermissionRequestMcp mcp => ResolveMcpPermissionRequest(mcp),
            PermissionRequestUrl url => ResolveUrlPermissionRequest(url),
            PermissionRequestMemory memory => ResolveMemoryPermissionRequest(memory),
            PermissionRequestCustomTool customTool => ResolveCustomToolPermissionRequest(customTool),
            PermissionRequestHook hook => ResolveHookPermissionRequest(hook),
            _ => FinalizePermissionRequest(null, request.Kind, "", false, null, null)
        };
    }

    private ResolvedPermissionRequest ResolveShellPermissionRequest(PermissionRequestShell request)
    {
        var path = request.PossiblePaths.FirstOrDefault();
        var description = request.Intention;
        if (string.IsNullOrWhiteSpace(description) && !string.IsNullOrWhiteSpace(request.Warning))
        {
            description = request.Warning;
        }

        return FinalizePermissionRequest(
            request.ToolCallId,
            "shell",
            description,
            false,
            request.FullCommandText,
            path);
    }

    private ResolvedPermissionRequest ResolveWritePermissionRequest(PermissionRequestWrite request)
    {
        return FinalizePermissionRequest(
            request.ToolCallId,
            "write",
            request.Intention,
            false,
            null,
            request.FileName);
    }

    private ResolvedPermissionRequest ResolveReadPermissionRequest(PermissionRequestRead request)
    {
        return FinalizePermissionRequest(
            request.ToolCallId,
            "read",
            request.Intention,
            true,
            null,
            request.Path);
    }

    private ResolvedPermissionRequest ResolveMcpPermissionRequest(PermissionRequestMcp request)
    {
        var description = request.ToolTitle;
        if (string.IsNullOrWhiteSpace(description))
        {
            description = $"{request.ServerName}: {request.ToolName}";
        }

        return FinalizePermissionRequest(
            request.ToolCallId,
            request.ToolName,
            description,
            request.ReadOnly,
            null,
            null);
    }

    private ResolvedPermissionRequest ResolveUrlPermissionRequest(PermissionRequestUrl request)
    {
        var description = request.Intention;
        if (string.IsNullOrWhiteSpace(description))
        {
            description = $"Fetch URL: {request.Url}";
        }

        return FinalizePermissionRequest(
            request.ToolCallId,
            "url",
            description,
            true,
            null,
            null);
    }

    private ResolvedPermissionRequest ResolveMemoryPermissionRequest(PermissionRequestMemory request)
    {
        var description = string.IsNullOrWhiteSpace(request.Subject)
            ? "Store assistant memory"
            : $"Store memory: {request.Subject}";

        return FinalizePermissionRequest(
            request.ToolCallId,
            "memory",
            description,
            false,
            null,
            null);
    }

    private ResolvedPermissionRequest ResolveCustomToolPermissionRequest(PermissionRequestCustomTool request)
    {
        return FinalizePermissionRequest(
            request.ToolCallId,
            request.ToolName,
            request.ToolDescription,
            false,
            null,
            null);
    }

    private ResolvedPermissionRequest ResolveHookPermissionRequest(PermissionRequestHook request)
    {
        return FinalizePermissionRequest(
            request.ToolCallId,
            request.ToolName,
            request.HookMessage,
            false,
            null,
            null);
    }

    private ResolvedPermissionRequest FinalizePermissionRequest(
        string? toolCallId,
        string fallbackToolName,
        string? description,
        bool defaultIsReadOnly,
        string? command,
        string? path)
    {
        var resolvedToolCallId = toolCallId ?? "";
        var toolName = ResolvePermissionToolName(resolvedToolCallId, fallbackToolName);
        var tool = _toolsService.GetTool(toolName);

        var toolDescription = description;
        if (string.IsNullOrWhiteSpace(toolDescription) && !string.IsNullOrWhiteSpace(path))
        {
            toolDescription = $"Access: {path}";
        }
        if (string.IsNullOrWhiteSpace(toolDescription))
        {
            toolDescription = tool?.Description ?? "";
        }

        var isReadOnly = tool?.IsReadOnly ?? defaultIsReadOnly;
        return new ResolvedPermissionRequest(
            resolvedToolCallId,
            toolName,
            toolDescription,
            isReadOnly,
            command,
            path);
    }

    private string ResolvePermissionToolName(string toolCallId, string fallbackToolName)
    {
        if (!string.IsNullOrWhiteSpace(toolCallId) && _toolCallIdToName.TryGetValue(toolCallId, out var mappedName))
        {
            return mappedName;
        }

        return string.IsNullOrWhiteSpace(fallbackToolName) ? "unknown" : fallbackToolName;
    }

    public async Task EndSessionAsync()
    {
        if (_session == null)
        {
            return;
        }

        try
        {
            _logger.LogInformation($"Ending session: {_session.SessionId}");
            
            _eventSubscription?.Dispose();
            _eventSubscription = null;
            
            await _session.DisposeAsync();
            _session = null;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error ending session: {ex.Message}", ex);
            _session = null;
        }
    }

    public async Task SendMessageAsync(string message)
    {
        if (_session == null)
        {
            throw new InvalidOperationException("No active session. Call StartSessionAsync first.");
        }

        try
        {
            _logger.LogInformation($"Sending message: {message.Substring(0, Math.Min(50, message.Length))}...");
            
            await _session.SendAsync(new MessageOptions
            {
                Prompt = message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to send message: {ex.Message}", ex);
            throw;
        }
    }

    public async Task AbortAsync()
    {
        if (_session == null)
        {
            return;
        }

        try
        {
            _logger.LogInformation("Aborting current message...");
            await _session.AbortAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error aborting: {ex.Message}", ex);
        }
    }

    private void HandleSessionEvent(SessionEvent evt)
    {
        try
        {
            // Debug log all events with full type name
            var eventType = evt.GetType().Name;
            _logger.LogDebug($"SDK Event: {eventType}");
            
            switch (evt)
            {
                case AssistantTurnStartEvent turnStart:
                    _logger.LogDebug($"  TurnStart: TurnId={turnStart.Data?.TurnId}");
                    OnTurnStart?.Invoke();
                    break;
                    
                case AssistantTurnEndEvent turnEnd:
                    _logger.LogDebug($"  TurnEnd: TurnId={turnEnd.Data?.TurnId}");
                    OnTurnEnd?.Invoke();
                    break;
                    
                case AssistantReasoningEvent reasoning:
                    _logger.LogDebug($"  ReasoningStart: Id={reasoning.Data.ReasoningId}, ContentLen={reasoning.Data.Content?.Length ?? 0}");
                    OnReasoningStart?.Invoke(reasoning.Data.ReasoningId ?? "");
                    if (!string.IsNullOrEmpty(reasoning.Data.Content))
                    {
                        OnReasoningDelta?.Invoke(
                            reasoning.Data.ReasoningId ?? "",
                            reasoning.Data.Content);
                    }
                    break;
                    
                case AssistantReasoningDeltaEvent reasoningDelta:
                    // Don't log reasoning delta events - too noisy
                    OnReasoningDelta?.Invoke(
                        reasoningDelta.Data.ReasoningId ?? "",
                        reasoningDelta.Data.DeltaContent ?? "");
                    break;
                    
                case AssistantMessageDeltaEvent delta:
                    // Don't log delta events - too noisy
                    OnAssistantDelta?.Invoke(delta.Data.DeltaContent ?? "");
                    break;
                    
                case AssistantMessageEvent msg:
                    _logger.LogDebug($"  Message: ContentLen={msg.Data.Content?.Length ?? 0}");
                    OnAssistantMessage?.Invoke(msg.Data.Content ?? "");
                    break;
                
                case AssistantIntentEvent intent:
                    var intentText = intent.Data?.Intent ?? "";
                    _logger.LogDebug($"  AssistantIntent: {intentText}");
                    OnIntentChanged?.Invoke(intentText);
                    break;
                    
                case SessionIdleEvent:
                    _logger.LogDebug($"  SessionIdle");
                    OnSessionIdle?.Invoke();
                    break;
                    
                case SessionErrorEvent error:
                    var errorData = error.Data;
                    
                    // Log all available properties from the error
                    if (errorData != null)
                    {
                        _logger.LogError($"Session error data type: {errorData.GetType().FullName}");
                        foreach (var prop in errorData.GetType().GetProperties())
                        {
                            try
                            {
                                var value = prop.GetValue(errorData);
                                _logger.LogError($"  {prop.Name}: {value}");
                            }
                            catch { }
                        }
                    }
                    
                    var errorCode = errorData?.GetType().GetProperty("Code")?.GetValue(errorData)?.ToString();
                    var errorDetails = errorData?.GetType().GetProperty("Details")?.GetValue(errorData)?.ToString();
                    _logger.LogError($"Session error: {errorData?.Message}, Code={errorCode}, Details={errorDetails}");
                    
                    var sessionError = new CopilotSessionError(
                        errorData?.Message ?? "Unknown error",
                        errorCode,
                        errorDetails
                    );
                    OnSessionError?.Invoke(sessionError);
                    OnError?.Invoke(errorData?.Message ?? "Unknown error");
                    break;
                
                case SessionUsageInfoEvent usageInfo:
                    // Try to extract usage info properties using reflection
                    var usageData = usageInfo.Data;
                    var model = usageData?.GetType().GetProperty("Model")?.GetValue(usageData)?.ToString();
                    var currentTokens = usageData?.GetType().GetProperty("CurrentTokens")?.GetValue(usageData) as int?;
                    var tokenLimit = usageData?.GetType().GetProperty("TokenLimit")?.GetValue(usageData) as int?;
                    var inputTokens = usageData?.GetType().GetProperty("InputTokens")?.GetValue(usageData) as int?;
                    var outputTokens = usageData?.GetType().GetProperty("OutputTokens")?.GetValue(usageData) as int?;
                    
                    _logger.LogDebug($"  SessionUsageInfo: Model={model}, Tokens={currentTokens}/{tokenLimit}, In={inputTokens}, Out={outputTokens}");
                    
                    var usage = new CopilotUsageInfo(model, currentTokens, tokenLimit, inputTokens, outputTokens);
                    OnUsageInfoChanged?.Invoke(usage);
                    break;
                    
                case SessionModelChangeEvent modelChange:
                    var modelData = modelChange.Data;
                    var newModel = modelData?.GetType().GetProperty("NewModel")?.GetValue(modelData)?.ToString();
                    var prevModel = modelData?.GetType().GetProperty("PreviousModel")?.GetValue(modelData)?.ToString();
                    _logger.LogInformation($"Model changed: {prevModel} -> {newModel}");
                    break;
                
                case AssistantUsageEvent assistantUsage:
                    // Extract token usage from assistant usage event
                    var aUsageData = assistantUsage.Data;
                    var aInputTokens = aUsageData?.GetType().GetProperty("InputTokens")?.GetValue(aUsageData) as int?;
                    var aOutputTokens = aUsageData?.GetType().GetProperty("OutputTokens")?.GetValue(aUsageData) as int?;
                    var aModel = aUsageData?.GetType().GetProperty("Model")?.GetValue(aUsageData)?.ToString();
                    
                    if (aInputTokens.HasValue || aOutputTokens.HasValue)
                    {
                        _logger.LogDebug($"  AssistantUsage: Model={aModel}, In={aInputTokens}, Out={aOutputTokens}");
                        var aUsage = new CopilotUsageInfo(aModel, null, null, aInputTokens, aOutputTokens);
                        OnUsageInfoChanged?.Invoke(aUsage);
                    }
                    break;
                    
                case ToolExecutionStartEvent toolStart:
                    // Skip report_intent tool - we handle it via AssistantIntentEvent
                    if (toolStart.Data.ToolName == "report_intent")
                    {
                        _logger.LogDebug($"  ToolExecutionStart: Skipping report_intent");
                        break;
                    }
                    
                    // Track the callId -> toolName mapping for permission requests
                    var startToolName = toolStart.Data.ToolName ?? "unknown";
                    var startCallId = toolStart.Data.ToolCallId ?? "";
                    if (!string.IsNullOrEmpty(startCallId))
                    {
                        _toolCallIdToName[startCallId] = startToolName;
                    }
                    
                    _logger.LogDebug($"  ToolExecutionStart: Name={startToolName}, CallId={startCallId}");
                    OnToolStart?.Invoke(startToolName, startCallId);
                    break;

                case ToolUserRequestedEvent toolRequested:
                    var requestedToolName = toolRequested.Data.ToolName ?? "unknown";
                    var requestedToolCallId = toolRequested.Data.ToolCallId ?? "";
                    if (!string.IsNullOrEmpty(requestedToolCallId))
                    {
                        _toolCallIdToName[requestedToolCallId] = requestedToolName;
                    }

                    _logger.LogDebug($"  ToolUserRequested: Name={requestedToolName}, CallId={requestedToolCallId}");
                    break;
                     
                case ToolExecutionCompleteEvent toolComplete:
                    var resultObj = toolComplete.Data.Result;
                    var errorObj = toolComplete.Data.Error;
                    
                    // Debug log the Data object properties first
                    _logger.LogDebug($"  ToolExecutionComplete: Data type: {toolComplete.Data.GetType().FullName}");
                    foreach (var dataProp in toolComplete.Data.GetType().GetProperties())
                    {
                        try
                        {
                            var val = dataProp.GetValue(toolComplete.Data);
                            _logger.LogDebug($"    Data.{dataProp.Name}: {val}");
                        }
                        catch { }
                    }
                    
                    // Debug log the error object if present
                    if (errorObj != null)
                    {
                        _logger.LogDebug($"  ToolExecutionComplete: Error type: {errorObj.GetType().FullName}");
                        foreach (var prop in errorObj.GetType().GetProperties())
                        {
                            try
                            {
                                var val = prop.GetValue(errorObj);
                                _logger.LogDebug($"    Error.{prop.Name}: {val}");
                            }
                            catch { }
                        }
                    }
                    
                    // Debug log the result object details
                    if (resultObj == null)
                    {
                        _logger.LogDebug($"  ToolExecutionComplete: Result is null");
                    }
                    else
                    {
                        _logger.LogDebug($"  ToolExecutionComplete: Result type: {resultObj.GetType().FullName}");
                        // Log all properties
                        foreach (var prop in resultObj.GetType().GetProperties())
                        {
                            try
                            {
                                var val = prop.GetValue(resultObj);
                                _logger.LogDebug($"    Result.{prop.Name}: {val}");
                            }
                            catch { }
                        }
                    }
                    
                    var resultStr = FormatToolResult(resultObj);
                    var completeCallId = toolComplete.Data.ToolCallId;
                    
                    // Try to get tool name from the event data
                    var completeToolName = toolComplete.Data?.GetType().GetProperty("ToolName")?.GetValue(toolComplete.Data)?.ToString();
                    
                    // Skip report_intent completions - we handle intent via AssistantIntentEvent
                    if (completeToolName == "report_intent" || resultStr == "Intent logged")
                    {
                        _logger.LogDebug($"  ToolExecutionComplete: Skipping report_intent completion");
                        break;
                    }
                    
                    _logger.LogDebug($"  ToolExecutionComplete: ToolName={completeToolName}, CallId={completeCallId}, ResultType={resultObj?.GetType().Name}, ResultLen={resultStr.Length}, Result={resultStr.Substring(0, Math.Min(200, resultStr.Length))}...");
                    OnToolComplete?.Invoke(
                        completeCallId ?? "",
                        resultStr);
                    break;
                    
                default:
                    // Log unhandled events with their full details for debugging
                    _logger.LogDebug($"  (Unhandled event: {eventType}) - Data: {evt}");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error handling session event: {ex.Message}", ex);
        }
    }

    public void AddUserMessage(string content)
    {
        _logger.LogDebug($"AddUserMessage: {content.Substring(0, Math.Min(50, content.Length))}...");
        _messages.Add(new CopilotChatMessage(content, true));
    }

    public void AddAssistantMessage(string content)
    {
        _logger.LogDebug($"AddAssistantMessage: {content.Substring(0, Math.Min(50, content.Length))}...");
        _messages.Add(new CopilotChatMessage(content, false));
    }
    
    public void AddReasoningMessage(string reasoningId)
    {
        _logger.LogDebug($"AddReasoningMessage: {reasoningId}");
        
        // Check for duplicate - don't add if we already have a reasoning message with this ID
        var existing = _messages.FirstOrDefault(m => 
            m.MessageType == CopilotMessageType.Reasoning && 
            m.ReasoningId == reasoningId);
        if (existing != null)
        {
            _logger.LogDebug($"AddReasoningMessage: Skipping duplicate for reasoningId={reasoningId}");
            return;
        }
        
        _messages.Add(new CopilotChatMessage("", false, CopilotMessageType.Reasoning, reasoningId: reasoningId));
    }
    
    public void UpdateReasoningMessage(string reasoningId, string content)
    {
        // First try to find by exact ID
        var msg = _messages.LastOrDefault(m => m.MessageType == CopilotMessageType.Reasoning && m.ReasoningId == reasoningId);
        
        // If not found, try to find any incomplete reasoning message
        if (msg == null)
        {
            msg = _messages.LastOrDefault(m => m.MessageType == CopilotMessageType.Reasoning && !m.IsComplete);
        }
        
        // If still not found, create a new one with this ID
        if (msg == null)
        {
            _logger.LogDebug($"UpdateReasoningMessage: Creating new reasoning message for id {reasoningId}");
            msg = new CopilotChatMessage("", false, CopilotMessageType.Reasoning, reasoningId: reasoningId);
            _messages.Add(msg);
        }
        
        msg.Content += content;
        _logger.LogDebug($"UpdateReasoningMessage: {reasoningId}, totalLen={msg.Content.Length}");
    }
    
    public void CompleteReasoningMessage(string? reasoningId = null)
    {
        _logger.LogDebug($"CompleteReasoningMessage: {reasoningId ?? "(any)"}");
        var msg = reasoningId != null 
            ? _messages.LastOrDefault(m => m.MessageType == CopilotMessageType.Reasoning && m.ReasoningId == reasoningId)
            : _messages.LastOrDefault(m => m.MessageType == CopilotMessageType.Reasoning && !m.IsComplete);
        
        // Also try incomplete reasoning if specific ID not found
        if (msg == null && reasoningId != null)
        {
            msg = _messages.LastOrDefault(m => m.MessageType == CopilotMessageType.Reasoning && !m.IsComplete);
        }
        
        if (msg != null)
        {
            msg.IsComplete = true;
            msg.IsCollapsed = true;
            _logger.LogDebug($"CompleteReasoningMessage: Completed, contentLen={msg.Content.Length}");
        }
        else
        {
            _logger.LogWarning($"CompleteReasoningMessage: Could not find reasoning message");
        }
    }
    
    public void AddToolMessage(string toolName, string? toolCallId = null)
    {
        _logger.LogDebug($"AddToolMessage: {toolName}, callId={toolCallId}");
        
        // Check for duplicate - don't add if we already have an incomplete tool with this callId or name
        if (!string.IsNullOrEmpty(toolCallId))
        {
            var existing = _messages.FirstOrDefault(m => 
                m.MessageType == CopilotMessageType.ToolCall && 
                m.ToolCallId == toolCallId);
            if (existing != null)
            {
                _logger.LogDebug($"AddToolMessage: Skipping duplicate for callId={toolCallId}");
                return;
            }
        }
        
        // Also check if there's already an incomplete tool with the same name (in case callId varies)
        var existingByName = _messages.FirstOrDefault(m => 
            m.MessageType == CopilotMessageType.ToolCall && 
            m.ToolName == toolName && 
            !m.IsComplete);
        if (existingByName != null && string.IsNullOrEmpty(toolCallId))
        {
            _logger.LogDebug($"AddToolMessage: Skipping duplicate for name={toolName} (already have incomplete)");
            return;
        }
        
        var msg = new CopilotChatMessage("", false, CopilotMessageType.ToolCall, toolName: toolName, toolCallId: toolCallId);
        _messages.Add(msg);
    }

    public void SetToolCommand(string? toolName, string? toolCallId, string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        CopilotChatMessage? msg = null;

        if (!string.IsNullOrEmpty(toolCallId))
        {
            msg = _messages.LastOrDefault(m => m.MessageType == CopilotMessageType.ToolCall && m.ToolCallId == toolCallId);
        }

        if (msg == null && !string.IsNullOrEmpty(toolName))
        {
            msg = _messages.LastOrDefault(m => m.MessageType == CopilotMessageType.ToolCall && m.ToolName == toolName && !m.IsComplete);
        }

        if (msg == null)
        {
            msg = _messages.LastOrDefault(m => m.MessageType == CopilotMessageType.ToolCall && !m.IsComplete);
        }

        if (msg != null)
        {
            msg.ToolCommand = command;
            _logger.LogDebug($"SetToolCommand: Updated command preview for tool {msg.ToolName}, callId={msg.ToolCallId}");
        }
        else
        {
            _logger.LogDebug($"SetToolCommand: Could not find tool message for name={toolName ?? "(any)"}, callId={toolCallId}");
        }
    }
    
    public void CompleteToolMessage(string? toolName, string? toolCallId, bool success, string result)
    {
        _logger.LogDebug($"CompleteToolMessage: name={toolName ?? "(any)"}, callId={toolCallId}, success={success}, resultLen={result?.Length ?? 0}");
        
        CopilotChatMessage? msg = null;
        
        // Try to find by call ID first (most reliable for parallel calls)
        if (!string.IsNullOrEmpty(toolCallId))
        {
            msg = _messages.LastOrDefault(m => m.MessageType == CopilotMessageType.ToolCall && m.ToolCallId == toolCallId && !m.IsComplete);
        }
        
        // Then try by name
        if (msg == null && !string.IsNullOrEmpty(toolName))
        {
            msg = _messages.LastOrDefault(m => m.MessageType == CopilotMessageType.ToolCall && m.ToolName == toolName && !m.IsComplete);
        }
        
        // Finally try any incomplete tool
        if (msg == null)
        {
            msg = _messages.LastOrDefault(m => m.MessageType == CopilotMessageType.ToolCall && !m.IsComplete);
        }
        
        if (msg != null)
        {
            msg.IsComplete = true;
            msg.IsSuccess = success;
            msg.Content = result;
            msg.IsCollapsed = true; // Collapse output by default (command still visible)
            _logger.LogDebug($"CompleteToolMessage: Completed tool {msg.ToolName}");
        }
        else
        {
            _logger.LogWarning($"CompleteToolMessage: Could not find tool message for name={toolName}, callId={toolCallId}");
        }
    }

    public void ClearMessages()
    {
        _messages.Clear();
    }
    
    public void AddErrorMessage(CopilotChatMessage errorMessage)
    {
        _messages.Add(errorMessage);
    }
    
    /// <summary>
    /// Format a tool result object for display
    /// </summary>
    private string FormatToolResult(object? result)
    {
        if (result == null) return "";
        
        var resultType = result.GetType();
        var resultTypeName = resultType.FullName ?? resultType.Name;
        
        // If it's already a string, return it
        if (result is string str) return str;
        
        // Try to get useful properties from the result
        try
        {
            // Check for common property names
            var contentProp = resultType.GetProperty("Content") ?? resultType.GetProperty("content");
            if (contentProp != null)
            {
                var content = contentProp.GetValue(result)?.ToString();
                if (!string.IsNullOrEmpty(content)) return content;
            }
            
            var messageProp = resultType.GetProperty("Message") ?? resultType.GetProperty("message");
            if (messageProp != null)
            {
                var message = messageProp.GetValue(result)?.ToString();
                if (!string.IsNullOrEmpty(message)) return message;
            }
            
            var textProp = resultType.GetProperty("Text") ?? resultType.GetProperty("text");
            if (textProp != null)
            {
                var text = textProp.GetValue(result)?.ToString();
                if (!string.IsNullOrEmpty(text)) return text;
            }
            
            var valueProp = resultType.GetProperty("Value") ?? resultType.GetProperty("value");
            if (valueProp != null)
            {
                var value = valueProp.GetValue(result)?.ToString();
                if (!string.IsNullOrEmpty(value)) return value;
            }
            
            // Try JSON serialization
            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            // If JSON is just the type name or empty object, return type info
            if (json == "{}" || json == "null" || json.Contains("\"$type\""))
            {
                return $"[{resultTypeName}]";
            }
            return json;
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"FormatToolResult: Failed to format {resultTypeName}: {ex.Message}");
            // Fallback to ToString, but if it's just the type name, indicate that
            var toString = result.ToString() ?? "";
            if (toString == resultTypeName || toString.StartsWith("GitHub.Copilot.SDK."))
            {
                return $"[{resultTypeName}]";
            }
            return toString;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
}
