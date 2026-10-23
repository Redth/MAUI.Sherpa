using Microsoft.Maui.Controls;

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

    /// <summary>Fired by the Blazor component when the native form actions change.</summary>
    public event Action? ActionStateChanged;

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
        string? secondaryActionText = null)
    {
        HasActionState = true;
        ActionEnabled = enabled;
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

    internal static Button CreateHeaderActionButton(HybridFormBridge bridge, HybridHeaderAction action)
    {
        var button = new Button
        {
            Text = action.FallbackText ?? string.Empty,
            FontSize = 13,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            CornerRadius = 6,
            Padding = new Thickness(8, 4),
            WidthRequest = 32,
            HeightRequest = 28,
            MinimumWidthRequest = 32,
            IsEnabled = action.IsEnabled,
            IsVisible = action.IsVisible,
            VerticalOptions = LayoutOptions.Center,
        };
        button.SetDynamicResource(Button.TextColorProperty, FormTheme.TextSecondary);
        if (!string.IsNullOrEmpty(action.ToolTip))
            ToolTipProperties.SetText(button, action.ToolTip);

        button.Clicked += async (_, _) => await bridge.RequestActionAsync(action.Id);
        return button;
    }

#if MACOSAPP
    internal static void SyncMacHeaderActionButtons(
        View? headerContainer,
        HybridFormBridge bridge,
        ref List<AppKit.NSButton>? macButtons)
    {
        if (macButtons != null)
        {
            foreach (var oldBtn in macButtons)
            {
                oldBtn.RemoveFromSuperview();
                oldBtn.Dispose();
            }
            macButtons.Clear();
        }

        if (headerContainer?.Handler?.PlatformView is not AppKit.NSView containerView)
            return;

        var visibleActions = bridge.HeaderActions.Where(a => a.IsVisible).ToList();
        if (visibleActions.Count == 0)
            return;

        macButtons ??= new List<AppKit.NSButton>();

        var bounds = containerView.Bounds;
        var containerWidth = bounds.Width > 0 ? bounds.Width : 540;
        var containerHeight = bounds.Height > 0 ? bounds.Height : 56;

        nfloat buttonWidth = 30;
        nfloat buttonHeight = 26;
        nfloat spacing = 6;
        nfloat rightEdge = containerWidth - 24;
        nfloat y = (nfloat)Math.Max(4, (containerHeight - buttonHeight) / 2 + 3);

        var symbolConfig = AppKit.NSImageSymbolConfiguration.Create(13f, AppKit.NSFontWeight.Medium);

        for (var i = visibleActions.Count - 1; i >= 0; i--)
        {
            var action = visibleActions[i];
            var actionId = action.Id;
            rightEdge -= buttonWidth;

            var nsButton = AppKit.NSButton.CreateButton(action.FallbackText ?? string.Empty, () =>
            {
                _ = bridge.RequestActionAsync(actionId);
            });
            nsButton.BezelStyle = AppKit.NSBezelStyle.AccessoryBarAction;
            nsButton.ControlSize = AppKit.NSControlSize.Regular;
            nsButton.Enabled = action.IsEnabled;
            if (!string.IsNullOrEmpty(action.ToolTip))
                nsButton.ToolTip = action.ToolTip;

            var symbol = AppKit.NSImage.GetSystemSymbol(action.Icon, null);
            if (symbol != null)
            {
                if (symbolConfig != null)
                    symbol = symbol.GetImage(symbolConfig) ?? symbol;
                nsButton.Image = symbol;
                nsButton.ImagePosition = AppKit.NSCellImagePosition.ImageOnly;
                nsButton.Title = string.Empty;
            }

            nsButton.Frame = new CoreGraphics.CGRect(rightEdge, y, buttonWidth, buttonHeight);
            nsButton.AutoresizingMask = AppKit.NSViewResizingMask.MinXMargin;

            containerView.AddSubview(nsButton);
            macButtons.Add(nsButton);

            rightEdge -= spacing;
        }
    }
#endif

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
