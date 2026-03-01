using MauiSherpa.Core.Interfaces;
using Microsoft.Maui.Controls;

namespace MauiSherpa.Services;

public class FormModalService : IFormModalService
{
    public async Task<TResult?> ShowAsync<TResult>(IFormPage<TResult> page)
    {
        var nav = Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;
        if (nav == null)
        {
            System.Diagnostics.Debug.WriteLine("[FormModal] No navigation context available");
            throw new InvalidOperationException("No navigation context available");
        }

        var contentPage = page as ContentPage
            ?? throw new ArgumentException("Form page must be a ContentPage", nameof(page));

        // Build content before pushing so native sheet can measure size
        if (contentPage is Pages.Forms.IFormPageBuildable buildable)
            buildable.EnsureBuilt();

        System.Diagnostics.Debug.WriteLine($"[FormModal] Pushing modal: {contentPage.GetType().Name}, Content null? {contentPage.Content == null}");
        try
        {
            await nav.PushModalAsync(contentPage, animated: true);
            System.Diagnostics.Debug.WriteLine("[FormModal] Modal pushed, awaiting result");
            var result = await page.GetResultAsync();
            await nav.PopModalAsync(animated: true);
            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FormModal] ERROR: {ex}");
            throw;
        }
    }

    public async Task ShowViewAsync(object page, Func<Task> waitForClose)
    {
        var nav = Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;
        if (nav == null)
            throw new InvalidOperationException("No navigation context available");

        var contentPage = page as ContentPage
            ?? throw new ArgumentException("Page must be a ContentPage", nameof(page));

        await nav.PushModalAsync(contentPage, animated: true);
        await waitForClose();
        await nav.PopModalAsync(animated: true);
    }
}
