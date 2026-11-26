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
