using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Services;

/// <summary>
/// Presents the Copilot chat as a modal MAUI page backed by its own BlazorWebView.
/// Listens to ICopilotContextService open/close events and pushes/pops the CopilotPage.
/// 
/// On Mac Catalyst/Windows, the singleton CopilotPage is reused across open/close cycles
/// (HandlerDisconnectPolicy.Manual keeps the WebView alive).
/// On macOS AppKit, a fresh CopilotPage is created each time because the MacOSBlazorWebView
/// gets disposed when removed from the visual tree.
/// </summary>
public class CopilotModalService : ICopilotModalService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICopilotContextService _contextService;
    private readonly IToolbarService _toolbarService;
    private CopilotPage? _currentPage;

    public bool IsOpen { get; private set; }

    public CopilotModalService(IServiceProvider serviceProvider, ICopilotContextService contextService, IToolbarService toolbarService)
    {
        _serviceProvider = serviceProvider;
        _contextService = contextService;
        _toolbarService = toolbarService;

        _contextService.OnOpenRequested += HandleOpenRequested;
        _contextService.OnCloseRequested += HandleCloseRequested;
    }

    private CopilotPage GetOrCreatePage()
    {
#if MACOSAPP
        // macOS AppKit: always create fresh — WebView is disposed on PopModalAsync
        return ActivatorUtilities.CreateInstance<CopilotPage>(_serviceProvider);
#else
        // Mac Catalyst/Windows: reuse singleton (HandlerDisconnectPolicy.Manual)
        return _currentPage ??= _serviceProvider.GetRequiredService<CopilotPage>();
#endif
    }

    public async Task OpenAsync()
    {
        if (IsOpen) return;

        var nav = Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;
        if (nav is null) return;

        IsOpen = true;
        _contextService.NotifyOverlayStateChanged(true);
        _toolbarService.SetToolbarSuppressed(true);

        _currentPage = GetOrCreatePage();
        await nav.PushModalAsync(_currentPage, animated: true);
    }

    public async Task CloseAsync()
    {
        if (!IsOpen) return;

        var nav = Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;
        if (nav is null) return;

        IsOpen = false;
        _contextService.NotifyOverlayStateChanged(false);

        if (_currentPage != null && nav.ModalStack.Contains(_currentPage))
        {
            await nav.PopModalAsync(animated: true);
        }

#if MACOSAPP
        _currentPage = null; // let GC collect the disposed page
#endif

        // Restore toolbar AFTER the modal is fully dismissed so the
        // native toolbar items re-appear on the underlying page.
        _toolbarService.SetToolbarSuppressed(false);
    }

    private void HandleOpenRequested()
    {
        Application.Current?.Dispatcher.Dispatch(async () =>
        {
            try { await OpenAsync(); }
            catch { /* Navigation may fail if already in progress */ }
        });
    }

    private void HandleCloseRequested()
    {
        Application.Current?.Dispatcher.Dispatch(async () =>
        {
            try { await CloseAsync(); }
            catch { /* Navigation may fail if already in progress */ }
        });
    }

    public void Dispose()
    {
        _contextService.OnOpenRequested -= HandleOpenRequested;
        _contextService.OnCloseRequested -= HandleCloseRequested;
    }
}
