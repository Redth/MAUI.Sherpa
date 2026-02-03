using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Services;

public class SplashService : ISplashService
{
    public event Action? OnBlazorReady;
    
    public bool IsBlazorReady { get; private set; }
    
    public void NotifyBlazorReady()
    {
        if (IsBlazorReady) return;
        
        IsBlazorReady = true;
        OnBlazorReady?.Invoke();
    }
}
