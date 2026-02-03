using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Service for managing the global Copilot overlay state and context
/// </summary>
public class CopilotContextService : ICopilotContextService
{
    public bool IsOverlayOpen { get; private set; }
    
    public event Action? OnOpenRequested;
    public event Action? OnCloseRequested;
    public event Action<string>? OnMessageRequested;
    public event Action<CopilotContext>? OnContextRequested;
    
    public void OpenOverlay()
    {
        OnOpenRequested?.Invoke();
    }
    
    public void CloseOverlay()
    {
        OnCloseRequested?.Invoke();
    }
    
    public void ToggleOverlay()
    {
        if (IsOverlayOpen)
            CloseOverlay();
        else
            OpenOverlay();
    }
    
    public void OpenWithMessage(string message)
    {
        OnOpenRequested?.Invoke();
        OnMessageRequested?.Invoke(message);
    }
    
    public void OpenWithContext(CopilotContext context)
    {
        OnOpenRequested?.Invoke();
        OnContextRequested?.Invoke(context);
    }
    
    public void NotifyOverlayStateChanged(bool isOpen)
    {
        IsOverlayOpen = isOpen;
    }
    
    /// <summary>
    /// Helper to build a context message for environment fix requests
    /// </summary>
    public static CopilotContext BuildEnvironmentFixContext(
        IEnumerable<(string Category, string Message, bool IsError)> issues)
    {
        var errors = issues.Where(i => i.IsError).ToList();
        var warnings = issues.Where(i => !i.IsError).ToList();
        
        var messageBuilder = new System.Text.StringBuilder();
        messageBuilder.AppendLine("Please help me evaluate and fix my development environment.");
        messageBuilder.AppendLine();
        messageBuilder.AppendLine("Current Status:");
        
        foreach (var error in errors)
        {
            messageBuilder.AppendLine($"❌ {error.Category}: {error.Message}");
        }
        
        foreach (var warning in warnings)
        {
            messageBuilder.AppendLine($"⚠️ {warning.Category}: {warning.Message}");
        }
        
        messageBuilder.AppendLine();
        messageBuilder.AppendLine("Please diagnose these issues and help me resolve them step by step.");
        
        return new CopilotContext(
            Title: "Environment Fix",
            Message: messageBuilder.ToString(),
            Type: CopilotContextType.EnvironmentFix
        );
    }
    
    /// <summary>
    /// Helper to build a context message for operation failures
    /// </summary>
    public static CopilotContext BuildOperationFailureContext(
        string operationName,
        string errorMessage,
        string? details = null)
    {
        var messageBuilder = new System.Text.StringBuilder();
        messageBuilder.AppendLine("An operation failed and I need help troubleshooting.");
        messageBuilder.AppendLine();
        messageBuilder.AppendLine($"**Operation:** {operationName}");
        messageBuilder.AppendLine($"**Error:** {errorMessage}");
        
        if (!string.IsNullOrEmpty(details))
        {
            messageBuilder.AppendLine();
            messageBuilder.AppendLine("**Details:**");
            messageBuilder.AppendLine(details);
        }
        
        messageBuilder.AppendLine();
        messageBuilder.AppendLine("Please help me understand what went wrong and how to fix it.");
        
        return new CopilotContext(
            Title: "Operation Failure",
            Message: messageBuilder.ToString(),
            Type: CopilotContextType.OperationFailure,
            OperationName: operationName,
            ErrorMessage: errorMessage,
            Details: details
        );
    }
    
    /// <summary>
    /// Helper to build a context message for process failures
    /// </summary>
    public static CopilotContext BuildProcessFailureContext(
        string command,
        int exitCode,
        string? output = null)
    {
        var messageBuilder = new System.Text.StringBuilder();
        messageBuilder.AppendLine("A command/process failed and I need help.");
        messageBuilder.AppendLine();
        messageBuilder.AppendLine($"**Command:** `{command}`");
        messageBuilder.AppendLine($"**Exit Code:** {exitCode}");
        
        if (!string.IsNullOrEmpty(output))
        {
            // Truncate very long output
            var truncatedOutput = output.Length > 2000 
                ? output.Substring(0, 2000) + "\n... (truncated)" 
                : output;
            
            messageBuilder.AppendLine();
            messageBuilder.AppendLine("**Output:**");
            messageBuilder.AppendLine("```");
            messageBuilder.AppendLine(truncatedOutput);
            messageBuilder.AppendLine("```");
        }
        
        messageBuilder.AppendLine();
        messageBuilder.AppendLine("Please analyze this failure and suggest a fix.");
        
        return new CopilotContext(
            Title: "Process Failure",
            Message: messageBuilder.ToString(),
            Type: CopilotContextType.ProcessFailure,
            OperationName: command,
            ExitCode: exitCode,
            Details: output
        );
    }
}
