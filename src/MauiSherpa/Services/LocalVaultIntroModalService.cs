using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;
using MauiSherpa.Pages.Modals;

namespace MauiSherpa.Services;

public sealed class LocalVaultIntroModalService
{
    private readonly HybridFormBridgeHolder _bridgeHolder;
    private readonly IFormModalService _formModalService;
    private readonly ILocalVaultIntroductionService _introductionService;
    private readonly SemaphoreSlim _modalLock = new(1, 1);

    public LocalVaultIntroModalService(
        HybridFormBridgeHolder bridgeHolder,
        IFormModalService formModalService,
        ILocalVaultIntroductionService introductionService)
    {
        _bridgeHolder = bridgeHolder;
        _formModalService = formModalService;
        _introductionService = introductionService;
    }

    public async Task<LocalVaultIntroductionResult?> ShowIfNeededAsync()
    {
        if (_introductionService.GetState().HasSeenCurrentVersion)
            return null;

        return await ShowAsync(previewOnly: false);
    }

    public Task<LocalVaultIntroductionResult?> ShowPreviewAsync() =>
        ShowAsync(previewOnly: true);

    private async Task<LocalVaultIntroductionResult?> ShowAsync(bool previewOnly)
    {
        await _modalLock.WaitAsync();
        try
        {
            LocalVaultIntroPage? page = null;
            await NativeUiDispatcher.RunAsync(() =>
            {
                page = new LocalVaultIntroPage(_bridgeHolder, previewOnly);
                return Task.CompletedTask;
            });
            return await _formModalService.ShowAsync(page!);
        }
        finally
        {
            _modalLock.Release();
        }
    }
}
