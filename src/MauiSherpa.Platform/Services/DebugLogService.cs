using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MauiSherpa.Platform.Services;

public class DebugLogService
{
    private readonly ConcurrentQueue<string> _logs = new();
    private const int MaxLogs = 100;
    
    public event Action? OnLogAdded;
    
    public void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        _logs.Enqueue($"[{timestamp}] {message}");
        
        // Keep only last MaxLogs entries
        while (_logs.Count > MaxLogs && _logs.TryDequeue(out _)) { }
        
        OnLogAdded?.Invoke();
    }
    
    public IEnumerable<string> GetLogs() => _logs.ToArray();
    
    public void Clear()
    {
        while (_logs.TryDequeue(out _)) { }
        OnLogAdded?.Invoke();
    }
}

/// <summary>
/// Custom logger that writes to the DebugLogService overlay
/// </summary>
public class DebugOverlayLogger : ILogger
{
    private readonly string _categoryName;
    private readonly DebugLogService _logService;
    
    // Only log from these categories
    private static readonly HashSet<string> AllowedCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "LocalSdkService",
        "DoctorService", 
        "Doctor",
        "LoggingService",
        "AndroidSdkService",
        "AndroidSdkViewModel",
        "AppleConnectService",
        "OperationModalService",
        "MultiOperationModalService"
    };

    public DebugOverlayLogger(string categoryName, DebugLogService logService)
    {
        _categoryName = categoryName;
        _logService = logService;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel)
    {
        if (logLevel < LogLevel.Debug) return false;
        
        var shortCategory = _categoryName.Contains('.') 
            ? _categoryName.Substring(_categoryName.LastIndexOf('.') + 1) 
            : _categoryName;
        
        return AllowedCategories.Contains(shortCategory);
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        
        var levelStr = logLevel switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???"
        };
        
        var shortCategory = _categoryName.Contains('.') 
            ? _categoryName.Substring(_categoryName.LastIndexOf('.') + 1) 
            : _categoryName;
        
        var message = formatter(state, exception);
        _logService.Log($"[{levelStr}] {shortCategory}: {message}");
        
        if (exception != null)
            _logService.Log($"  Exception: {exception.Message}");
    }
}

/// <summary>
/// Logger provider that creates DebugOverlayLoggers
/// </summary>
public class DebugOverlayLoggerProvider : ILoggerProvider
{
    private readonly DebugLogService _logService;
    private readonly ConcurrentDictionary<string, DebugOverlayLogger> _loggers = new();

    public DebugOverlayLoggerProvider(DebugLogService logService)
    {
        _logService = logService;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new DebugOverlayLogger(name, _logService));
    }

    public void Dispose()
    {
        _loggers.Clear();
    }
}
