namespace MauiSherpa.Pages.Forms;

/// <summary>
/// Communication bridge between a HybridFormPage (native MAUI) and its
/// embedded Blazor component. The MAUI page listens for validation changes;
/// the Blazor component sets validity and collects form data on submit.
/// </summary>
public class HybridFormBridge
{
    /// <summary>Fired by the Blazor component when form validity changes.</summary>
    public event Action? ValidationChanged;

    /// <summary>Fired by the MAUI page when the native Submit button is clicked.</summary>
    public event Func<Task>? SubmitRequested;

    /// <summary>Fired by the MAUI page when the native Cancel button is clicked.</summary>
    public event Action? CancelRequested;

    /// <summary>Fired by the MAUI page when the native Back button is clicked (wizard).</summary>
    public event Func<Task>? BackRequested;

    /// <summary>Fired by the MAUI page when the native Next button is clicked (wizard).</summary>
    public event Func<Task>? NextRequested;

    /// <summary>Fired by the Blazor component when wizard button state changes.</summary>
    public event Action? WizardStateChanged;

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

    /// <summary>Fired by the MAUI page when a custom action button is clicked (e.g. "save", "reset").</summary>
    public event Func<string, Task>? ActionRequested;

    /// <summary>Called by MAUI page when native submit button is pressed.</summary>
    public async Task RequestSubmitAsync()
    {
        if (SubmitRequested != null)
            await SubmitRequested.Invoke();
    }

    /// <summary>Called by MAUI page when a custom action button is pressed.</summary>
    public async Task RequestActionAsync(string actionId)
    {
        if (ActionRequested != null)
            await ActionRequested.Invoke(actionId);
    }

    /// <summary>Fired by the Blazor component to programmatically close the modal with a result.</summary>
    public event Action? CloseRequested;

    /// <summary>Called by MAUI page when native cancel button is pressed.</summary>
    public void RequestCancel() => CancelRequested?.Invoke();

    /// <summary>Called by Blazor component to close the modal with the current Result.</summary>
    public void RequestClose() => CloseRequested?.Invoke();

    /// <summary>Called by MAUI page when native back button is pressed.</summary>
    public async Task RequestBackAsync()
    {
        if (BackRequested != null)
            await BackRequested.Invoke();
    }

    /// <summary>Called by MAUI page when native next button is pressed.</summary>
    public async Task RequestNextAsync()
    {
        if (NextRequested != null)
            await NextRequested.Invoke();
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

    public HybridFormBridge? Current => _stack.Count > 0 ? _stack.Peek() : null;

    /// <summary>Push a new bridge for a nested modal.</summary>
    public void Push(HybridFormBridge bridge) => _stack.Push(bridge);

    /// <summary>Pop the current bridge when a modal closes, restoring the parent's.</summary>
    public void Pop()
    {
        if (_stack.Count > 0) _stack.Pop();
    }
}
