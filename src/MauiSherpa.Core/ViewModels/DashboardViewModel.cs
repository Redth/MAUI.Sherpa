namespace MauiSherpa.Core.ViewModels;

public class DashboardViewModel : ViewModelBase
{
    public string Title => "Dashboard";
    public string WelcomeMessage => "Let .NET MAUI Sherpa guide your development environment needs!";

    public DashboardViewModel(Interfaces.IAlertService? alertService = null, Interfaces.ILoggingService? loggingService = null) 
        : base(alertService ?? new StubAlertService(), loggingService ?? new StubLoggingService())
    {
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
