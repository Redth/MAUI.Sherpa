namespace MauiSherpa.Pages.Forms;

/// <summary>
/// Singleton service for passing parameters and results between parent pages
/// and modal dialog Blazor components rendered inside ProgressModalPage.
/// Parent sets parameters before pushing the modal, dialog reads them.
/// Dialog sets the result, parent reads it after modal is popped.
/// </summary>
public class ModalParameterService
{
    private readonly Dictionary<string, object?> _params = new();
    private TaskCompletionSource<object?>? _tcs;

    public void Set<T>(string key, T? value) => _params[key] = value;

    public T? Get<T>(string key) =>
        _params.TryGetValue(key, out var v) && v is T t ? t : default;

    public bool Has(string key) => _params.ContainsKey(key);

    /// <summary>Called by the parent to start waiting for a result.</summary>
    public Task<object?> WaitForResultAsync()
    {
        _tcs = new TaskCompletionSource<object?>();
        return _tcs.Task;
    }

    /// <summary>Called by the dialog to complete with a result.</summary>
    public void Complete(object? result)
    {
        _tcs?.TrySetResult(result);
        _tcs = null;
    }

    /// <summary>Called by the dialog to cancel (no result).</summary>
    public void Cancel()
    {
        _tcs?.TrySetResult(null);
        _tcs = null;
    }

    /// <summary>Clears all parameters.</summary>
    public void Clear() => _params.Clear();
}
