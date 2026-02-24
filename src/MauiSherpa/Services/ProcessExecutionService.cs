using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Services;

public class ProcessExecutionService : IProcessExecutionService
{
    private readonly ILoggingService _logger;
    private readonly IPlatformService _platform;
    private Process? _currentProcess;
    private readonly StringBuilder _outputBuilder = new();
    private readonly StringBuilder _errorBuilder = new();
    private ProcessState _currentState = ProcessState.Pending;
    private readonly object _stateLock = new();
    private CancellationTokenSource? _linkedCts;
    private DateTime _startTime;
    private string? _tempOutputFile;
    private CancellationTokenSource? _tailCts;

    public ProcessState CurrentState
    {
        get { lock (_stateLock) return _currentState; }
        private set
        {
            ProcessState oldState;
            lock (_stateLock)
            {
                oldState = _currentState;
                _currentState = value;
            }
            if (oldState != value)
            {
                StateChanged?.Invoke(this, new ProcessStateChangedEventArgs(oldState, value));
            }
        }
    }

    public int? ProcessId => _currentProcess?.Id;

    public event EventHandler<ProcessOutputEventArgs>? OutputReceived;
    public event EventHandler<ProcessStateChangedEventArgs>? StateChanged;

    public ProcessExecutionService(ILoggingService logger, IPlatformService platform)
    {
        _logger = logger;
        _platform = platform;
    }

    public async Task<ProcessResult> ExecuteAsync(ProcessRequest request, CancellationToken cancellationToken = default)
    {
        _outputBuilder.Clear();
        _errorBuilder.Clear();
        CurrentState = ProcessState.Running;
        _startTime = DateTime.Now;

        _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            // Echo the command to output
            var commandLine = $"$ {request.CommandLine}";
            OnOutput(commandLine, isError: false);
            
            if (request.RequiresElevation && (_platform.IsMacCatalyst || _platform.IsMacOS))
            {
                return await ExecuteElevatedMacAsync(request, _linkedCts.Token);
            }
            
            return await ExecuteNormalAsync(request, _linkedCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Process execution failed: {ex.Message}", ex);
            CurrentState = ProcessState.Failed;
            var duration = DateTime.Now - _startTime;
            OnOutput($"\n❌ Error: {ex.Message}", isError: true);
            return new ProcessResult(-1, _outputBuilder.ToString(), ex.Message, duration, ProcessState.Failed);
        }
        finally
        {
            CleanupTempFiles();
            _linkedCts?.Dispose();
            _linkedCts = null;
            _currentProcess?.Dispose();
            _currentProcess = null;
            _tailCts?.Cancel();
            _tailCts?.Dispose();
            _tailCts = null;
        }
    }
    
    private async Task<ProcessResult> ExecuteNormalAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.Command,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            WorkingDirectory = request.WorkingDirectory ?? Environment.CurrentDirectory
        };

        foreach (var arg in request.Arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        if (request.Environment != null)
        {
            foreach (var kvp in request.Environment)
            {
                startInfo.Environment[kvp.Key] = kvp.Value;
            }
        }

        _currentProcess = new Process { StartInfo = startInfo };
        _currentProcess.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                OnOutput(e.Data, isError: false);
            }
        };
        _currentProcess.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                OnOutput(e.Data, isError: true);
            }
        };

        _currentProcess.Start();
        _currentProcess.BeginOutputReadLine();
        _currentProcess.BeginErrorReadLine();

        try
        {
            await _currentProcess.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Handled by state
        }

        return CreateResult();
    }
    
    private async Task<ProcessResult> ExecuteElevatedMacAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        // Strategy: Create a temp script that runs the command and tees output to a file.
        // We tail the file in real-time while osascript runs the script with elevation.
        
        _tempOutputFile = Path.Combine(Path.GetTempPath(), $"mauisherpa_output_{Guid.NewGuid():N}.log");
        var tempScript = Path.Combine(Path.GetTempPath(), $"mauisherpa_cmd_{Guid.NewGuid():N}.sh");
        
        try
        {
            // Build the command with proper escaping
            var cmdArgs = string.Join(" ", request.Arguments.Select(EscapeForShell));
            var fullCommand = $"{request.Command} {cmdArgs}";
            
            // Create script that runs command and streams to output file
            // Using unbuffered output with script command for real-time streaming
            var scriptContent = $@"#!/bin/bash
exec > >(tee -a ""{_tempOutputFile}"") 2>&1
{fullCommand}
EXIT_CODE=$?
echo ""__EXIT_CODE__:$EXIT_CODE"" >> ""{_tempOutputFile}""
exit $EXIT_CODE
";
            await File.WriteAllTextAsync(tempScript, scriptContent, cancellationToken);
            
            // Make script executable
            var chmod = Process.Start(new ProcessStartInfo
            {
                FileName = "chmod",
                ArgumentList = { "+x", tempScript },
                UseShellExecute = false,
                CreateNoWindow = true
            });
            chmod?.WaitForExit();
            
            // Create output file
            await File.WriteAllTextAsync(_tempOutputFile, "", cancellationToken);
            
            // Start tailing the output file
            _tailCts = new CancellationTokenSource();
            var tailTask = TailOutputFileAsync(_tempOutputFile, _tailCts.Token);
            
            // Build osascript command
            var escapedScript = tempScript.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var osascriptCmd = $"do shell script \"\\\"{escapedScript}\\\"\" with administrator privileges";
            
            _logger.LogInformation($"Running elevated: {fullCommand}");
            OnOutput("🔐 Requesting administrator privileges...", isError: false);
            
            var startInfo = new ProcessStartInfo
            {
                FileName = "osascript",
                ArgumentList = { "-e", osascriptCmd },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = request.WorkingDirectory ?? Environment.CurrentDirectory
            };
            
            _currentProcess = new Process { StartInfo = startInfo };
            _currentProcess.Start();
            
            // osascript output is captured but we get streaming from the tail
            var osascriptOutput = await _currentProcess.StandardOutput.ReadToEndAsync(cancellationToken);
            var osascriptError = await _currentProcess.StandardError.ReadToEndAsync(cancellationToken);
            
            try
            {
                await _currentProcess.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Handled by state
            }
            
            // Stop tailing
            _tailCts.Cancel();
            try { await tailTask; } catch (OperationCanceledException) { }
            
            // Check for osascript errors (like user cancelled auth)
            if (!string.IsNullOrWhiteSpace(osascriptError) && _currentProcess.ExitCode != 0)
            {
                OnOutput(osascriptError, isError: true);
            }
            
            // Parse exit code from our marker if present
            var exitCode = _currentProcess.ExitCode;
            if (File.Exists(_tempOutputFile))
            {
                var content = await File.ReadAllTextAsync(_tempOutputFile, cancellationToken);
                var exitMarker = "__EXIT_CODE__:";
                var markerIndex = content.LastIndexOf(exitMarker);
                if (markerIndex >= 0)
                {
                    var exitCodeStr = content.Substring(markerIndex + exitMarker.Length).Trim();
                    if (int.TryParse(exitCodeStr, out var parsedCode))
                    {
                        exitCode = parsedCode;
                    }
                }
            }
            
            // Determine final state
            ProcessState finalState;
            lock (_stateLock)
            {
                finalState = _currentState;
                if (finalState == ProcessState.Running)
                {
                    finalState = exitCode == 0 ? ProcessState.Completed : ProcessState.Failed;
                }
            }
            CurrentState = finalState;
            
            return new ProcessResult(
                exitCode,
                _outputBuilder.ToString(),
                _errorBuilder.ToString(),
                DateTime.Now - _startTime,
                finalState
            );
        }
        finally
        {
            // Clean up temp script
            try { if (File.Exists(tempScript)) File.Delete(tempScript); } catch { }
        }
    }
    
    private async Task TailOutputFileAsync(string filePath, CancellationToken cancellationToken)
    {
        long lastPosition = 0;
        var lastContent = "";
        
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    if (fs.Length > lastPosition)
                    {
                        fs.Seek(lastPosition, SeekOrigin.Begin);
                        using var reader = new StreamReader(fs);
                        var newContent = await reader.ReadToEndAsync(cancellationToken);
                        lastPosition = fs.Position;
                        
                        if (!string.IsNullOrEmpty(newContent))
                        {
                            // Split by lines and output each (skip exit code marker)
                            var lines = newContent.Split('\n');
                            foreach (var line in lines)
                            {
                                if (!string.IsNullOrEmpty(line) && !line.StartsWith("__EXIT_CODE__:"))
                                {
                                    OnOutput(line.TrimEnd('\r'), isError: false);
                                }
                            }
                        }
                    }
                }
                
                await Task.Delay(100, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Tail error: {ex.Message}");
                await Task.Delay(200, cancellationToken);
            }
        }
    }
    
    private ProcessResult CreateResult()
    {
        var duration = DateTime.Now - _startTime;
        var exitCode = _currentProcess?.HasExited == true ? _currentProcess.ExitCode : -1;

        ProcessState finalState;
        lock (_stateLock)
        {
            finalState = _currentState;
            if (finalState == ProcessState.Running)
            {
                finalState = exitCode == 0 ? ProcessState.Completed : ProcessState.Failed;
            }
        }
        CurrentState = finalState;

        return new ProcessResult(
            exitCode,
            _outputBuilder.ToString(),
            _errorBuilder.ToString(),
            duration,
            finalState
        );
    }
    
    private void CleanupTempFiles()
    {
        if (_tempOutputFile != null)
        {
            try { if (File.Exists(_tempOutputFile)) File.Delete(_tempOutputFile); } catch { }
            _tempOutputFile = null;
        }
    }

    private string EscapeForShell(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return "''";
        if (!arg.Contains(' ') && !arg.Contains('\'') && !arg.Contains('"'))
            return arg;
        return $"'{arg.Replace("'", "'\\''")}'";
    }

    private string EscapeForAppleScript(string command)
    {
        return command.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    public void Cancel()
    {
        if (_currentProcess == null || _currentProcess.HasExited) return;

        _logger.LogInformation("Sending cancel signal to process");
        OnOutput("\n⚠️ Cancellation requested...", isError: false);
        CurrentState = ProcessState.Cancelled;

        try
        {
            if (_platform.IsMacCatalyst || _platform.IsMacOS)
            {
                // Send SIGINT on Unix
                SendSignal(_currentProcess.Id, 2); // SIGINT
            }
            else
            {
                // On Windows, try to send Ctrl+C
                _currentProcess.StandardInput.WriteLine("\x03");
                _currentProcess.StandardInput.Close();
            }

            // Give it a moment to gracefully stop
            Task.Delay(500).ContinueWith(_ =>
            {
                if (_currentProcess != null && !_currentProcess.HasExited)
                {
                    OnOutput("Process did not stop gracefully. Use Force Kill if needed.", isError: true);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to send cancel signal: {ex.Message}");
        }

        _linkedCts?.Cancel();
    }

    public void Kill()
    {
        if (_currentProcess == null || _currentProcess.HasExited) return;

        _logger.LogInformation("Force killing process");
        OnOutput("\n🛑 Force killing process...", isError: false);
        CurrentState = ProcessState.Killed;

        try
        {
            _currentProcess.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to kill process: {ex.Message}");
            try
            {
                _currentProcess.Kill();
            }
            catch { }
        }

        _linkedCts?.Cancel();
    }

    public string GetFullOutput()
    {
        return _outputBuilder.ToString();
    }

    private void OnOutput(string data, bool isError)
    {
        if (isError)
            _errorBuilder.AppendLine(data);
        else
            _outputBuilder.AppendLine(data);

        OutputReceived?.Invoke(this, new ProcessOutputEventArgs(data, isError));
    }

    // P/Invoke for sending signals on Unix
    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int sig);

    private void SendSignal(int pid, int signal)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || 
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            kill(pid, signal);
        }
    }
}
