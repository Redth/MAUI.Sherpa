using GitHub.Copilot.SDK;
using MauiSherpa.Core.Interfaces;
using System.Text.Json;

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
        _logger.LogDebug($"Permission request: Kind={request.Kind}, ToolCallId={request.ToolCallId}");
        
        // Log all extension data for debugging
        if (request.ExtensionData != null)
        {
            foreach (var kvp in request.ExtensionData)
            {
                _logger.LogDebug($"  Request.ExtensionData[{kvp.Key}]: {kvp.Value}");
            }
        }
        
        // Log invocation properties using reflection
        _logger.LogDebug($"  Invocation type: {invocation.GetType().FullName}");
        foreach (var prop in invocation.GetType().GetProperties())
        {
            try
            {
                var value = prop.GetValue(invocation);
                if (value != null)
                {
                    _logger.LogDebug($"  Invocation.{prop.Name}: {value}");
                }
            }
            catch { }
        }
        
        // Check for ExtensionData on invocation too
        var invocationExtData = invocation.GetType().GetProperty("ExtensionData")?.GetValue(invocation) as IDictionary<string, object>;
        if (invocationExtData != null)
        {
            foreach (var kvp in invocationExtData)
            {
                _logger.LogDebug($"  Invocation.ExtensionData[{kvp.Key}]: {kvp.Value}");
            }
        }
        
        // Try to get tool name from our tracked mapping first (most reliable)
        var toolCallId = request.ToolCallId ?? "";
        var toolName = "unknown";
        
        if (!string.IsNullOrEmpty(toolCallId) && _toolCallIdToName.TryGetValue(toolCallId, out var mappedName))
        {
            toolName = mappedName;
        }
        else
        {
            // Fall back to other sources
            toolName = request.ExtensionData?.GetValueOrDefault("toolName")?.ToString()
                ?? request.ExtensionData?.GetValueOrDefault("name")?.ToString()
                ?? invocation.GetType().GetProperty("ToolName")?.GetValue(invocation)?.ToString()
                ?? invocation.GetType().GetProperty("Name")?.GetValue(invocation)?.ToString()
                ?? toolCallId
                ?? "unknown";
        }
        
        // Get intention/description from extension data (much more useful than generic description)
        var intention = request.ExtensionData?.GetValueOrDefault("intention")?.ToString();
        var path = request.ExtensionData?.GetValueOrDefault("path")?.ToString();
        
        // Try to get command for bash/shell tools from multiple sources
        // The SDK uses "fullCommandText" for the actual command
        var command = request.ExtensionData?.GetValueOrDefault("fullCommandText")?.ToString()
            ?? request.ExtensionData?.GetValueOrDefault("command")?.ToString()
            ?? request.ExtensionData?.GetValueOrDefault("cmd")?.ToString()
            ?? request.ExtensionData?.GetValueOrDefault("script")?.ToString()
            ?? request.ExtensionData?.GetValueOrDefault("code")?.ToString();
        
        // Also check invocation extension data
        if (string.IsNullOrEmpty(command) && invocationExtData != null)
        {
            invocationExtData.TryGetValue("fullCommandText", out var fullCmd);
            invocationExtData.TryGetValue("command", out var cmd);
            invocationExtData.TryGetValue("code", out var code);
            command = fullCmd?.ToString() ?? cmd?.ToString() ?? code?.ToString();
        }
        
        _logger.LogDebug($"  Resolved toolName: {toolName}, intention: {intention}, path: {path}, command: {command}");
        
        var tool = _toolsService.GetTool(toolName);
        var isReadOnly = tool?.IsReadOnly ?? (request.Kind == "read");
        
        // Build a better description using intention or path
        var toolDescription = intention 
            ?? (path != null ? $"Access: {path}" : null)
            ?? tool?.Description 
            ?? "";
        
        // Default result
        var defaultResult = new ToolPermissionResult(true);
        
        // If we have a permission handler delegate, call it
        if (PermissionHandler != null)
        {
            var permRequest = new ToolPermissionRequest(toolName, toolDescription, isReadOnly, defaultResult, command, path);
            _logger.LogDebug($"Calling PermissionHandler for tool: {toolName}");
            var result = await PermissionHandler(permRequest);
            _logger.LogDebug($"PermissionHandler returned: IsAllowed={result.IsAllowed}");
            
            if (result.IsAllowed)
            {
                _logger.LogDebug($"Returning 'approved' for tool: {toolName}");
                return new PermissionRequestResult { Kind = "approved" };
            }
            else
            {
                _logger.LogDebug($"Returning 'denied' for tool: {toolName}");
                return new PermissionRequestResult { Kind = "denied" };
            }
        }
        
        // Default: allow read-only tools, deny destructive tools
        if (isReadOnly)
        {
            return new PermissionRequestResult { Kind = "approved" };
        }
        
        // Default deny for destructive tools if no handler
        return new PermissionRequestResult { Kind = "denied" };
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
