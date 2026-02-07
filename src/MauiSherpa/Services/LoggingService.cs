using Microsoft.Extensions.Logging;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Services;

public class LoggingService : ILoggingService
{
    private readonly ILogger<LoggingService> _logger;
    private readonly List<LogEntry> _logs = new();
    private readonly object _lock = new();
    private const int MaxLogEntries = 1000;
    
    private readonly string _logFilePath;
    private readonly StreamWriter? _logFileWriter;

    public event Action? OnLogAdded;

    public LoggingService(ILogger<LoggingService> logger)
    {
        _logger = logger;
        
        // Set up file logging
        try
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MauiSherpa", "logs");
            Directory.CreateDirectory(logDir);
            
            _logFilePath = Path.Combine(logDir, $"maui-sherpa-{DateTime.Now:yyyy-MM-dd}.log");
            _logFileWriter = new StreamWriter(_logFilePath, append: true) { AutoFlush = true };
            
            _logFileWriter.WriteLine($"\n\n========== Session started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==========\n");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Could not initialize file logging: {ex.Message}");
            _logFileWriter = null;
            _logFilePath = string.Empty;
        }
    }

    public void LogInformation(string message)
    {
        _logger.LogInformation(message);
        AddLog("INF", message);
    }

    public void LogWarning(string message)
    {
        _logger.LogWarning(message);
        AddLog("WRN", message);
    }

    public void LogError(string message, Exception? exception = null)
    {
        if (exception != null)
        {
            _logger.LogError(exception, message);
            AddLog("ERR", $"{message}: {exception.Message}");
        }
        else
        {
            _logger.LogError(message);
            AddLog("ERR", message);
        }
    }

    public void LogDebug(string message)
    {
        _logger.LogDebug(message);
        AddLog("DBG", message);
    }

    public IReadOnlyList<LogEntry> GetRecentLogs(int maxCount = 500)
    {
        lock (_lock)
        {
            return _logs.TakeLast(maxCount).ToList();
        }
    }

    public void ClearLogs()
    {
        lock (_lock)
        {
            _logs.Clear();
        }
    }

    private void AddLog(string level, string message)
    {
        var timestamp = DateTime.Now;
        lock (_lock)
        {
            _logs.Add(new LogEntry(timestamp, level, message));
            if (_logs.Count > MaxLogEntries)
            {
                _logs.RemoveAt(0);
            }
            
            // Write to file
            try
            {
                _logFileWriter?.WriteLine($"{timestamp:HH:mm:ss.fff} [{level}] {message}");
            }
            catch { /* Ignore file write errors */ }
        }
        OnLogAdded?.Invoke();
    }
}
