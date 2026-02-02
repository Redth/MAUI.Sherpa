using MauiSherpa.Core.Interfaces;
using Microsoft.AspNetCore.Components;

namespace MauiSherpa.Platform.Services;

public class NavigationService : INavigationService
{
    private readonly NavigationManager _navigationManager;

    public NavigationService(NavigationManager navigationManager)
    {
        _navigationManager = navigationManager;
    }

    public Task NavigateToAsync(string route)
    {
        _navigationManager.NavigateTo(route);
        return Task.CompletedTask;
    }

    public Task NavigateBackAsync()
    {
        // Blazor doesn't have built-in back navigation, use JS interop if needed
        return Task.CompletedTask;
    }

    public Task<string?> GetCurrentRouteAsync()
    {
        return Task.FromResult<string?>(_navigationManager.Uri);
    }
}
