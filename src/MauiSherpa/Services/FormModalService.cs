using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;
using Microsoft.Maui.Controls;

namespace MauiSherpa.Services;

public class FormModalService : IFormModalService
{
    public async Task<TResult?> ShowAsync<TResult>(IFormPage<TResult> page)
    {
        var contentPage = page as ContentPage
            ?? throw new ArgumentException("Form page must be a ContentPage", nameof(page));

        return await ShowPageAsync(contentPage, page.GetResultAsync);
    }

    public async Task ShowViewAsync(object page, Func<Task> waitForClose)
    {
        var contentPage = page as ContentPage
            ?? throw new ArgumentException("Page must be a ContentPage", nameof(page));

        await ShowPageAsync<object?>(contentPage, async () =>
        {
            await waitForClose();
            return null;
        });
    }

    private static async Task<TResult?> ShowPageAsync<TResult>(ContentPage page, Func<Task<TResult?>> waitForResult)
    {
        Window? window = null;
        INavigation? navigation = null;
        var dismissed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnModalPopped(object? sender, ModalPoppedEventArgs args)
        {
            if (!ReferenceEquals(args.Modal, page))
                return;
            dismissed.TrySetResult();
            (page as INativeModalDismissible)?.OnNativeDismissed();
        }

        try
        {
            await NativeUiDispatcher.RunAsync(async () =>
            {
                window = Application.Current?.Windows.FirstOrDefault()
                    ?? throw new InvalidOperationException("No application window available");
                navigation = window.Page?.Navigation
                    ?? throw new InvalidOperationException("No navigation context available");
                if (page is IFormPageBuildable buildable)
                    buildable.EnsureBuilt();
                window.ModalPopped += OnModalPopped;
                await navigation.PushModalAsync(page, animated: true);
            });

            var resultTask = waitForResult();
            await Task.WhenAny(resultTask, dismissed.Task);
            return dismissed.Task.IsCompleted ? default : await resultTask;
        }
        finally
        {
            if (window != null)
            {
                await NativeUiDispatcher.RunAsync(async () =>
                {
                    window.ModalPopped -= OnModalPopped;
                    if (navigation == null)
                        return;
                    if (ReferenceEquals(navigation.ModalStack.LastOrDefault(), page))
                        await navigation.PopModalAsync(animated: true);
                    else if (navigation.ModalStack.Contains(page))
                        throw new InvalidOperationException("Cannot close a modal while another modal is above it");
                    (page as INativeModalDismissible)?.OnNativeDismissed();
                });
            }
        }
    }
}
