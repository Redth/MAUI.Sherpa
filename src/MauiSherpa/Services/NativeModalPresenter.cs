using MauiSherpa.Pages.Forms;
using Microsoft.Maui.Controls;

namespace MauiSherpa.Services;

internal static class NativeModalPresenter
{
    public static async Task<object?> ShowProgressAsync(
        ModalParameterService parameters, string route, int width, int height)
    {
        var resultTask = parameters.WaitForResultAsync();
        var dismissed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Window? window = null;
        INavigation? navigation = null;
        ProgressModalPage? page = null;

        void OnModalPopped(object? sender, ModalPoppedEventArgs args)
        {
            if (ReferenceEquals(args.Modal, page))
                dismissed.TrySetResult();
        }

        try
        {
            await NativeUiDispatcher.RunAsync(async () =>
            {
                window = Application.Current?.Windows.FirstOrDefault()
                    ?? throw new InvalidOperationException("No application window available");
                navigation = window.Page?.Navigation
                    ?? throw new InvalidOperationException("No navigation context available");
                page = new ProgressModalPage(route, width, height);
                window.ModalPopped += OnModalPopped;
                await navigation.PushModalAsync(page, animated: true);
            });

            if (await Task.WhenAny(resultTask, dismissed.Task) == dismissed.Task)
                return null;

            var result = await resultTask;
            await NativeUiDispatcher.RunAsync(async () =>
            {
                if (ReferenceEquals(navigation!.ModalStack.LastOrDefault(), page))
                    await navigation.PopModalAsync(animated: true);
                else if (navigation.ModalStack.Contains(page!))
                    throw new InvalidOperationException("Cannot close a modal while another modal is above it");
            });
            return result;
        }
        finally
        {
            if (window != null)
                await NativeUiDispatcher.RunAsync(() =>
                {
                    window.ModalPopped -= OnModalPopped;
                    page?.OnNativeDismissed();
                    return Task.CompletedTask;
                });
            if (!resultTask.IsCompleted)
                parameters.Cancel();
        }
    }
}
