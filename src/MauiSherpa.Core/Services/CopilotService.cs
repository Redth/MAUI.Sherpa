using GitHub.Copilot.SDK;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Service for interacting with GitHub Copilot CLI via the SDK
/// </summary>
public class CopilotService : ICopilotService, IAsyncDisposable
{
    private readonly ILoggingService _logger;
    private readonly ICopilotToolsService _toolsService;
    private readonly string _skillsPath;
    private readonly List<CopilotChatMessage> _messages = new();
    
    private CopilotClient? _client;
    private CopilotSession? _session;
    private IDisposable? _eventSubscription;
    private CopilotAvailability? _cachedAvailability;

    public bool IsConnected => _client?.State == ConnectionState.Connected;
    public string? CurrentSessionId => _session?.SessionId;
    public IReadOnlyList<CopilotChatMessage> Messages => _messages.AsReadOnly();
    public CopilotAvailability? CachedAvailability => _cachedAvailability;
    
    /// <summary>
    /// Optional handler for permission requests. If not set, uses default behavior.
    /// </summary>
    public Func<ToolPermissionRequest, Task<ToolPermissionResult>>? PermissionHandler { get; set; }

    public event Action<string>? OnAssistantMessage;
    public event Action<string>? OnAssistantDelta;
    public event Action? OnSessionIdle;
    public event Action<string>? OnError;
    public event Action<string, string>? OnToolStart;
    public event Action<string, string>? OnToolComplete;

    public CopilotService(ILoggingService logger, ICopilotToolsService toolsService)
    {
        _logger = logger;
        _toolsService = toolsService;
        _skillsPath = GetSkillsPath();
        _logger.LogInformation($"Copilot skills path: {_skillsPath}");
    }

    private static string GetSkillsPath()
    {
        // Get the directory where the app is running from
        var baseDir = AppContext.BaseDirectory;
        
        // For MAUI apps, Raw assets are placed in different locations:
        // - Mac Catalyst: MauiSherpa.app/Contents/Resources/Skills
        // - Windows: alongside the executable
        
        // First check Resources folder (Mac Catalyst bundle structure)
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

        // Fallback to base directory
        return skillsPath;
    }

    public async Task<CopilotAvailability> CheckAvailabilityAsync(bool forceRefresh = false)
    {
        // Return cached result if available and not forcing refresh
        if (!forceRefresh && _cachedAvailability != null)
        {
            _logger.LogInformation("Returning cached Copilot availability");
            return _cachedAvailability;
        }

        CopilotClient? tempClient = null;
        try
        {
            _logger.LogInformation("Checking Copilot availability via SDK...");
            
            // Create a temporary client to check status
            var options = new CopilotClientOptions
            {
                AutoStart = true,
                AutoRestart = false
            };
            
            tempClient = new CopilotClient(options);
            await tempClient.StartAsync();
            
            // Get version/status info
            var statusResponse = await tempClient.GetStatusAsync();
            var version = statusResponse?.Version;
            _logger.LogInformation($"Copilot CLI version: {version}");
            
            // Check authentication status using SDK
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
            
            // If we can't start the client, assume CLI is not installed
            var isNotInstalled = ex.Message.Contains("not found") || 
                                 ex.Message.Contains("No such file") ||
                                 ex.Message.Contains("cannot find") ||
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
                await tempClient.DisposeAsync();
            }
        }
    }

    public async Task ConnectAsync()
    {
        if (_client != null)
        {
            _logger.LogWarning("Already connected to Copilot");
            return;
        }

        try
        {
            _logger.LogInformation("Connecting to Copilot CLI...");
            
            var options = new CopilotClientOptions
            {
                AutoStart = true,
                AutoRestart = true,
                UseStdio = true,
                Cwd = _skillsPath, // Set working directory to skills folder
                LogLevel = "info"
            };

            _client = new CopilotClient(options);
            await _client.StartAsync();
            
            _logger.LogInformation("Connected to Copilot CLI");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to connect to Copilot: {ex.Message}", ex);
            _client = null;
            throw;
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

    public async Task StartSessionAsync(string? model = null)
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
            var copilotTools = _toolsService.GetTools();
            var readOnlyTools = _toolsService.ReadOnlyToolNames;
            _logger.LogInformation($"Registering {copilotTools.Count} tools with Copilot session ({readOnlyTools.Count} read-only)");
            
            var config = new SessionConfig
            {
                Model = model ?? "anthropic/claude-opus-4.5",
                Streaming = true,
                Tools = copilotTools.Select(t => t.Function).ToList(),
                OnPermissionRequest = async (request, invocation) =>
                {
                    // Extract tool name from request
                    string? toolName = null;
                    if (request.ExtensionData != null && 
                        request.ExtensionData.TryGetValue("toolName", out var toolNameObj))
                    {
                        toolName = toolNameObj as string;
                    }
                    
                    // Get tool metadata
                    var tool = toolName != null ? _toolsService.GetTool(toolName) : null;
                    var isReadOnly = tool?.IsReadOnly ?? false;
                    var description = tool?.Description ?? "";
                    
                    // Determine default result based on whether tool is read-only
                    var defaultResult = new ToolPermissionResult(IsAllowed: true);
                    
                    // If we have a permission handler, let it decide
                    if (PermissionHandler != null && toolName != null)
                    {
                        var permissionRequest = new ToolPermissionRequest(
                            ToolName: toolName,
                            ToolDescription: description,
                            IsReadOnly: isReadOnly,
                            DefaultResult: defaultResult
                        );
                        
                        try
                        {
                            var result = await PermissionHandler(permissionRequest);
                            _logger.LogDebug($"Permission handler returned: {(result.IsAllowed ? "allow" : "deny")} for tool: {toolName}");
                            
                            if (result.IsAllowed)
                            {
                                return new PermissionRequestResult { Kind = "allow" };
                            }
                            else
                            {
                                return new PermissionRequestResult { Kind = "deny" };
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Permission handler threw exception: {ex.Message}", ex);
                            // Fall through to default behavior on error
                        }
                    }
                    
                    // Default behavior: auto-approve read-only tools, allow others
                    if (isReadOnly)
                    {
                        _logger.LogDebug($"Auto-approving read-only tool: {toolName}");
                    }
                    else
                    {
                        _logger.LogDebug($"Allowing tool execution for: {toolName ?? request.Kind}");
                    }
                    return new PermissionRequestResult { Kind = "allow" };
                }
            };

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
            switch (evt)
            {
                case AssistantMessageDeltaEvent delta:
                    OnAssistantDelta?.Invoke(delta.Data.DeltaContent ?? "");
                    break;
                    
                case AssistantMessageEvent msg:
                    OnAssistantMessage?.Invoke(msg.Data.Content ?? "");
                    break;
                    
                case SessionIdleEvent:
                    OnSessionIdle?.Invoke();
                    break;
                    
                case SessionErrorEvent error:
                    _logger.LogError($"Session error: {error.Data.Message}");
                    OnError?.Invoke(error.Data.Message ?? "Unknown error");
                    break;
                    
                case ToolExecutionStartEvent toolStart:
                    _logger.LogDebug($"Tool started: {toolStart.Data.ToolName}");
                    OnToolStart?.Invoke(
                        toolStart.Data.ToolName ?? "unknown",
                        "");
                    break;
                    
                case ToolExecutionCompleteEvent toolComplete:
                    _logger.LogDebug($"Tool completed");
                    OnToolComplete?.Invoke(
                        "tool",
                        toolComplete.Data.Result?.ToString() ?? "");
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
        _messages.Add(new CopilotChatMessage(content, true));
    }

    public void AddAssistantMessage(string content)
    {
        _messages.Add(new CopilotChatMessage(content, false));
    }

    public void ClearMessages()
    {
        _messages.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
}
