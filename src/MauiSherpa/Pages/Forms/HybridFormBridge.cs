namespace MauiSherpa.Pages.Forms;

/// <summary>
/// Describes an icon action button displayed in the top-right header area of a hybrid dialog.
/// </summary>
public record HybridHeaderAction(
    string Id,
    string Icon,
    string? ToolTip = null,
    string? FallbackText = null,
    bool IsVisible = true,
    bool IsEnabled = true);

/// <summary>
/// Communication bridge between a HybridFormPage (native MAUI) and its
/// embedded Blazor component. The MAUI page listens for validation changes;
/// the Blazor component sets validity and collects form data on submit.
/// </summary>
public partial class HybridFormBridge
{
    private SynchronizationContext? _callbackContext;
    private SynchronizationContext? _cancelCallbackContext;
    private Func<Task>? _submitRequested;
    private Action? _cancelRequested;
    private Func<Task>? _backRequested;
    private Func<Task>? _nextRequested;
    private Func<string, Task>? _actionRequested;

    /// <summary>Fired by the Blazor component when form validity changes.</summary>
    public event Action? ValidationChanged;

    /// <summary>Fired by the MAUI page when the native Submit button is clicked.</summary>
    public event Func<Task>? SubmitRequested
    {
        add { CaptureCallbackContext(); _submitRequested += value; }
        remove { _submitRequested -= value; }
    }

    /// <summary>Fired by the MAUI page when the native Cancel button is clicked.</summary>
    public event Action? CancelRequested
    {
        add { _cancelCallbackContext ??= SynchronizationContext.Current; _cancelRequested += value; }
        remove
        {
            _cancelRequested -= value;
            if (_cancelRequested == null)
                _cancelCallbackContext = null;
        }
    }

    /// <summary>Fired by the MAUI page when the native Back button is clicked (wizard).</summary>
    public event Func<Task>? BackRequested
    {
        add { CaptureCallbackContext(); _backRequested += value; }
        remove { _backRequested -= value; }
    }

    /// <summary>Fired by the MAUI page when the native Next button is clicked (wizard).</summary>
    public event Func<Task>? NextRequested
    {
        add { CaptureCallbackContext(); _nextRequested += value; }
        remove { _nextRequested -= value; }
    }

    /// <summary>Fired by the Blazor component when wizard button state changes.</summary>
    public event Action? WizardStateChanged;

    /// <summary>Fired by the Blazor component when the native form actions change.</summary>
    public event Action? ActionStateChanged;

    public event Action<bool>? ActionFocusRequested;

    public void RequestActionFocus(bool backwards) => ActionFocusRequested?.Invoke(backwards);

    /// <summary>Fired when the top-right header icon actions change.</summary>
    public event Action? HeaderActionsChanged;

    /// <summary>Icon action buttons displayed in the top-right header bar of the dialog.</summary>
    public IReadOnlyList<HybridHeaderAction> HeaderActions { get; private set; } = [];

    /// <summary>Current form validity, set by the Blazor component.</summary>
    public bool IsValid { get; private set; }

    /// <summary>The result object, set by the Blazor component before submit completes.</summary>
    public object? Result { get; set; }

    /// <summary>Optional parameters passed from the MAUI page to the Blazor component.</summary>
    public Dictionary<string, object?> Parameters { get; } = new();

    // Wizard state — set by Blazor component to control native buttons
    public bool ShowBack { get; private set; }
    public bool ShowNext { get; private set; }
    public bool ShowSubmit { get; private set; }
    public bool CanProceed { get; private set; }
    public bool IsSubmitting { get; private set; }
    public string? SubmitText { get; private set; }

    // Dynamic form action state — used by long-running forms that remain open after submit.
    public bool HasActionState { get; private set; }
    public bool ActionEnabled { get; private set; }
    public bool ActionVisible { get; private set; } = true;
    public bool ActionBusy { get; private set; }
    public string? ActionText { get; private set; }
    public string? SecondaryActionText { get; private set; }

    /// <summary>When true, native Cancel does not close the modal (Blazor handles it).</summary>
    public bool PreventClose { get; set; }

    /// <summary>When true, native Submit/Primary does not close the modal after the handler completes.</summary>
    public bool PreventSubmitClose { get; set; }

    /// <summary>Called by Blazor component to update form validity.</summary>
    public void SetValid(bool valid)
    {
        IsValid = valid;
        ValidationChanged?.Invoke();
    }

    /// <summary>Called by Blazor component to update wizard button visibility and state.</summary>
    public void SetWizardState(bool showBack, bool showNext, bool showSubmit, bool canProceed, bool isSubmitting = false, string? submitText = null)
    {
        ShowBack = showBack;
        ShowNext = showNext;
        ShowSubmit = showSubmit;
        CanProceed = canProceed;
        IsSubmitting = isSubmitting;
        SubmitText = submitText;
        WizardStateChanged?.Invoke();
    }

    /// <summary>
    /// Updates the native primary and secondary actions for a form that remains open
    /// while a long-running operation changes state.
    /// </summary>
    public void SetActionState(
        bool enabled,
        bool busy = false,
        string? actionText = null,
        string? secondaryActionText = null,
        bool visible = true)
    {
        HasActionState = true;
        ActionEnabled = enabled;
        ActionVisible = visible;
        ActionBusy = busy;
        ActionText = actionText;
        SecondaryActionText = secondaryActionText;
        ActionStateChanged?.Invoke();
    }

    /// <summary>Updates the top-right header icon action buttons on the native dialog header.</summary>
    public void SetHeaderActions(params HybridHeaderAction[] actions)
    {
        HeaderActions = actions ?? [];
        HeaderActionsChanged?.Invoke();
    }

    /// <summary>Fired by the MAUI page when a custom action button is clicked (e.g. "save", "reset").</summary>
    public event Func<string, Task>? ActionRequested
    {
        add { CaptureCallbackContext(); _actionRequested += value; }
        remove { _actionRequested -= value; }
    }

    /// <summary>Called by MAUI page when native submit button is pressed.</summary>
    public Task RequestSubmitAsync() => InvokeCallbackAsync(_submitRequested);

    /// <summary>Called by MAUI page when a custom action button is pressed.</summary>
    public Task RequestActionAsync(string actionId)
    {
        var callback = _actionRequested;
        return InvokeCallbackAsync(callback == null ? null : () => callback(actionId));
    }

    /// <summary>Fired by the Blazor component to programmatically close the modal with a result.</summary>
    public event Action? CloseRequested;

    /// <summary>Called by MAUI page when native cancel button is pressed.</summary>
    public Task RequestCancelAsync()
    {
        var callback = _cancelRequested;
        return InvokeCallbackAsync(callback == null ? null : () =>
        {
            callback();
            return Task.CompletedTask;
        }, _cancelCallbackContext);
    }

    /// <summary>Called by Blazor component to close the modal with the current Result.</summary>
    public void RequestClose() => CloseRequested?.Invoke();

    /// <summary>Called by MAUI page when native back button is pressed.</summary>
    public Task RequestBackAsync() => InvokeCallbackAsync(_backRequested);

    /// <summary>Called by MAUI page when native next button is pressed.</summary>
    public Task RequestNextAsync() => InvokeCallbackAsync(_nextRequested);

    private void CaptureCallbackContext()
    {
        // Native buttons can run on a different thread from the Blazor renderer.
        _callbackContext ??= SynchronizationContext.Current;
    }

    private Task InvokeCallbackAsync(Func<Task>? callback, SynchronizationContext? fallbackContext = null)
    {
        if (callback == null)
            return Task.CompletedTask;

        var context = _callbackContext ?? fallbackContext;
        if (context == null || SynchronizationContext.Current == context)
            return callback();

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(async _ =>
        {
            try
            {
                await callback();
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }, null);
        return completion.Task;
    }
}

/// <summary>
/// Singleton holder for the active HybridFormBridge.
/// Uses a stack to support nested modals — each modal pushes its bridge
/// on open and pops it on close, restoring the parent's bridge.
/// </summary>
public class HybridFormBridgeHolder
{
    private readonly Stack<HybridFormBridge> _stack = new();
    private readonly object _gate = new();

    public HybridFormBridge? Current
    {
        get
        {
            lock (_gate)
                return _stack.Count > 0 ? _stack.Peek() : null;
        }
    }

    /// <summary>Push a new bridge for a nested modal.</summary>
    public void Push(HybridFormBridge bridge)
    {
        lock (_gate)
            _stack.Push(bridge);
    }

    /// <summary>Pop the current bridge when a modal closes, restoring the parent's.</summary>
    public void Pop()
    {
        lock (_gate)
        {
            if (_stack.Count > 0) _stack.Pop();
        }
    }

    public void Remove(HybridFormBridge bridge)
    {
        lock (_gate)
        {
            var remaining = _stack.Where(item => !ReferenceEquals(item, bridge)).Reverse().ToArray();
            _stack.Clear();
            foreach (var item in remaining)
                _stack.Push(item);
        }
    }
}
