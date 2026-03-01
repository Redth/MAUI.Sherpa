namespace MauiSherpa.Pages.Forms;

/// <summary>
/// Defines a button to display in the native footer of a HybridProgressPage.
/// </summary>
public record ProgressButton(
    string Id,
    string Text,
    ProgressButtonStyle Style = ProgressButtonStyle.Secondary,
    string? Icon = null,
    bool Enabled = true);

public enum ProgressButtonStyle
{
    Primary,
    Secondary,
    Warning,
    Danger,
    Copilot,
}

/// <summary>
/// Communication bridge between HybridProgressPage (native MAUI) and its
/// embedded Blazor component. Supports dynamic footer buttons that change
/// based on operation state (running, completed, failed, etc.).
/// </summary>
public class ProgressBridge
{
    /// <summary>Fired when the Blazor component updates the footer buttons.</summary>
    public event Action? ButtonsChanged;

    /// <summary>Fired when the Blazor component updates the title.</summary>
    public event Action? TitleChanged;

    /// <summary>Fired by the native page when a footer button is clicked.</summary>
    public event Func<string, Task>? ButtonClicked;

    /// <summary>Fired by the Blazor component to close the modal.</summary>
    public event Action? CloseRequested;

    /// <summary>Current footer buttons.</summary>
    public IReadOnlyList<ProgressButton> Buttons { get; private set; } = [];

    /// <summary>Current title (overrides the initial title if set).</summary>
    public string? Title { get; private set; }

    /// <summary>Called by Blazor component to update footer buttons.</summary>
    public void SetButtons(params ProgressButton[] buttons)
    {
        Buttons = buttons;
        ButtonsChanged?.Invoke();
    }

    /// <summary>Called by Blazor component to update the title.</summary>
    public void SetTitle(string title)
    {
        Title = title;
        TitleChanged?.Invoke();
    }

    /// <summary>Called by native page when a button is clicked.</summary>
    public async Task OnButtonClickedAsync(string buttonId)
    {
        if (ButtonClicked != null)
            await ButtonClicked.Invoke(buttonId);
    }

    /// <summary>Called by Blazor component to request closing the modal.</summary>
    public void RequestClose() => CloseRequested?.Invoke();
}

/// <summary>
/// Singleton holder for the active ProgressBridge.
/// </summary>
public class ProgressBridgeHolder
{
    public ProgressBridge? Current { get; set; }
}
