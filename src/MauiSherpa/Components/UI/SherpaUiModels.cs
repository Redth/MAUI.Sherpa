namespace MauiSherpa.Components.UI;

public sealed record SherpaSelectOption<TValue>(
    TValue Value,
    string Label,
    string? Description = null,
    string? Icon = null,
    string? Group = null,
    string? DotCssClass = null,
    bool Disabled = false);

public sealed record SherpaTabItem(
    string Key,
    string Label,
    string? Icon = null,
    int? Count = null,
    bool HasAttentionDot = false,
    string AttentionDotClass = "sherpa-dot-warning",
    string? AttentionTitle = null,
    bool Disabled = false);

public enum SherpaTabVariant
{
    Standard,
    Inspector
}

public sealed record SherpaSegmentOption<TValue>(
    TValue Value,
    string Label,
    string? Icon = null,
    string? Title = null,
    bool Disabled = false);

public sealed record SherpaMenuItem(
    string Label,
    string? Icon = null,
    Func<Task>? OnClickAsync = null,
    bool IsDanger = false,
    bool Disabled = false,
    bool IsDivider = false,
    string? Title = null,
    bool IsExternalScreenshotAction = true)
{
    public static SherpaMenuItem Divider { get; } = new("", IsDivider: true);

    public static SherpaMenuItem Action(
        string label,
        string icon,
        Action onClick,
        bool isDanger = false,
        bool disabled = false,
        string? title = null,
        bool isExternalScreenshotAction = true) =>
        new(
            label,
            icon,
            () =>
            {
                onClick();
                return Task.CompletedTask;
            },
            isDanger,
            disabled,
            false,
            title,
            isExternalScreenshotAction);

    public static SherpaMenuItem Action(
        string label,
        string icon,
        Func<Task> onClickAsync,
        bool isDanger = false,
        bool disabled = false,
        string? title = null,
        bool isExternalScreenshotAction = true) =>
        new(
            label,
            icon,
            onClickAsync,
            isDanger,
            disabled,
            false,
            title,
            isExternalScreenshotAction);
}
