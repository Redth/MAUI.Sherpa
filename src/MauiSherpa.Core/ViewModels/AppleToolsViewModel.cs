namespace MauiSherpa.Core.ViewModels;

public class AppleToolsViewModel : ViewModelBase
{
    public string Title => "Apple Development Tools";

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private string _statusMessage = "Ready";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public AppleToolsViewModel(Interfaces.IAlertService? alertService = null, Interfaces.ILoggingService? loggingService = null)
        : base(alertService ?? new StubAlertService(), loggingService ?? new StubLoggingService())
    {
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading Xcode and tools information...";
        await Task.Delay(1000);
        StatusMessage = "Tools information loaded";
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
