namespace MauiSherpa.Services;

/// <summary>
/// Coordinates z-ordering, collapsed bar stacking, and maximize behavior
/// between the Android device inspector and iOS simulator inspector.
/// </summary>
public class InspectorCoordinator
{
    private int _nextZ = 10002;

    public int AndroidZIndex { get; private set; } = 10002;
    public int AppleZIndex { get; private set; } = 10002;

    /// <summary>Which inspector was last focused (used for z-ordering).</summary>
    public string? FocusedInspector { get; private set; }

    public event Action? StateChanged;

    public void BringToFront(string inspector)
    {
        if (FocusedInspector == inspector) return;
        FocusedInspector = inspector;
        _nextZ++;
        if (inspector == "android")
            AndroidZIndex = _nextZ;
        else
            AppleZIndex = _nextZ;
        StateChanged?.Invoke();
    }

    public int GetZIndex(string inspector)
        => inspector == "android" ? AndroidZIndex : AppleZIndex;

    /// <summary>
    /// Returns 0 or 1 indicating the collapsed bar slot index for this inspector.
    /// Slot 0 = bottom, Slot 1 = stacked above slot 0.
    /// Only open+minimized inspectors get slots.
    /// </summary>
    public int GetCollapsedSlot(string inspector, bool androidCollapsed, bool appleCollapsed)
    {
        if (!androidCollapsed && !appleCollapsed)
            return 0; // only one collapsed, gets slot 0

        // Both collapsed — order by focus (most recent on top)
        if (androidCollapsed && appleCollapsed)
        {
            if (inspector == "android")
                return FocusedInspector == "android" ? 1 : 0;
            else
                return FocusedInspector == "apple" ? 1 : 0;
        }

        // Only one collapsed, it gets slot 0
        return 0;
    }
}
