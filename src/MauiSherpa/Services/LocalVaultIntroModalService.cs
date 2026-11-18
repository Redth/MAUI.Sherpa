using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;

namespace MauiSherpa.Services;

public sealed class LocalVaultIntroModalService
{
    private const string PreviewOnlyParameter = "PreviewOnly";
    private readonly ModalParameterService _modalParams;
    private readonly ILocalVaultIntroductionService _introductionService;
    private readonly SemaphoreSlim _modalLock = new(1, 1);

    public bool IsVisible { get; private set; }
    public event Action? VisibilityChanged;

    public LocalVaultIntroModalService(
        ModalParameterService modalParams,
        ILocalVaultIntroductionService introductionService)
    {
        _modalParams = modalParams;
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
            _modalParams.Clear();
            _modalParams.Set(PreviewOnlyParameter, previewOnly);

            var resultTask = _modalParams.WaitForResultAsync();
#if LINUXGTK
            IsVisible = true;
            VisibilityChanged?.Invoke();
            try
            {
                var inlineResult = await resultTask;
                return inlineResult is LocalVaultIntroductionResult typedInlineResult
                    ? typedInlineResult
                    : null;
            }
            finally
            {
                IsVisible = false;
                VisibilityChanged?.Invoke();
            }
#else
            var nav = Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;
            if (nav == null)
                return null;

            var page = new ProgressModalPage("/modal/local-vault-intro", 620, 520);
            await nav.PushModalAsync(page, animated: true);
            var result = await resultTask;
            await nav.PopModalAsync(animated: true);

            return result is LocalVaultIntroductionResult typedResult ? typedResult : null;
#endif
        }
        finally
        {
            _modalParams.Clear();
            _modalLock.Release();
        }
    }
}
