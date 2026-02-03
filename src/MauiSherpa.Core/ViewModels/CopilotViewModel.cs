namespace MauiSherpa.Core.ViewModels;

public class CopilotViewModel : ViewModelBase
{
    public string Title => "GitHub Copilot";

    private string _userQuery = string.Empty;
    public string UserQuery
    {
        get => _userQuery;
        set => SetProperty(ref _userQuery, value);
    }

    private string _copilotResponse = string.Empty;
    public string CopilotResponse
    {
        get => _copilotResponse;
        set => SetProperty(ref _copilotResponse, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set => SetProperty(ref _isConnected, value);
    }

    public CopilotViewModel(Interfaces.IAlertService? alertService = null, Interfaces.ILoggingService? loggingService = null)
        : base(alertService ?? new StubAlertService(), loggingService ?? new StubLoggingService())
    {
        IsConnected = false;
    }

    public async Task ConnectAsync()
    {
        IsLoading = true;
        await Task.Delay(1000);
        IsConnected = true;
        IsLoading = false;
    }

    public async Task SendQueryAsync()
    {
        if (string.IsNullOrWhiteSpace(UserQuery))
            return;

        IsLoading = true;
        await Task.Delay(1500);
        CopilotResponse = $"This is a simulated response to: {UserQuery}\n\nCopilot integration coming soon!";
        IsLoading = false;
    }

    private class StubAlertService : Interfaces.IAlertService
    {
        public Task ShowAlertAsync(string title, string message, string? cancel = null) => Task.CompletedTask;
        public Task<bool> ShowConfirmAsync(string title, string message, string? confirm = null, string? cancel = null) => Task.FromResult(true);
        public Task ShowToastAsync(string message) => Task.CompletedTask;
    }

    private class StubLoggingService : Interfaces.ILoggingService
    {
        public void LogInformation(string message) { }
        public void LogWarning(string message) { }
        public void LogError(string message, Exception? exception = null) { }
        public void LogDebug(string message) { }
        public IReadOnlyList<Interfaces.LogEntry> GetRecentLogs(int maxCount = 500) => [];
        public void ClearLogs() { }
        public event Action? OnLogAdded;
    }
}
