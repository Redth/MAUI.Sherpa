namespace MauiSherpa.Services;

public enum ToastType
{
    Success,
    Info,
    Warning,
    Error
}

public record ToastMessage(string Message, ToastType Type, string Id)
{
    public bool IsExiting { get; set; }
}

public class BlazorToastService
{
    private readonly List<ToastMessage> _toasts = new();
    private readonly object _lock = new();
    private const int MaxToasts = 3;
    private const int AutoDismissMs = 4000;
    private const int ExitAnimationMs = 300;

    public event Action? OnChange;

    public IReadOnlyList<ToastMessage> Toasts
    {
        get
        {
            lock (_lock)
            {
                return _toasts.ToList();
            }
        }
    }

    public ToastMessage Show(string message, ToastType type = ToastType.Success)
    {
        var toast = new ToastMessage(message, type, Guid.NewGuid().ToString());
        
        lock (_lock)
        {
            // Remove oldest if at max capacity
            while (_toasts.Count >= MaxToasts)
            {
                _toasts.RemoveAt(0);
            }
            _toasts.Add(toast);
        }
        
        OnChange?.Invoke();

        // Schedule auto-dismiss
        _ = Task.Run(async () =>
        {
            await Task.Delay(AutoDismissMs);
            await DismissWithAnimation(toast);
        });
        
        return toast;
    }

    public ToastMessage ShowSuccess(string message) => Show(message, ToastType.Success);
    public ToastMessage ShowInfo(string message) => Show(message, ToastType.Info);
    public ToastMessage ShowWarning(string message) => Show(message, ToastType.Warning);
    public ToastMessage ShowError(string message) => Show(message, ToastType.Error);

    public async Task DismissWithAnimation(ToastMessage toast)
    {
        lock (_lock)
        {
            var existing = _toasts.FirstOrDefault(t => t.Id == toast.Id);
            if (existing == null) return;
            existing.IsExiting = true;
        }
        
        OnChange?.Invoke();
        
        // Wait for exit animation
        await Task.Delay(ExitAnimationMs);
        
        lock (_lock)
        {
            _toasts.RemoveAll(t => t.Id == toast.Id);
        }
        
        OnChange?.Invoke();
    }

    public void Dismiss(ToastMessage toast)
    {
        _ = DismissWithAnimation(toast);
    }
}
