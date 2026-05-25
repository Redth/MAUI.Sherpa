using Microsoft.AspNetCore.Components;

namespace MauiSherpa.AppInspector.Components;

public class DataGridColumnDef<TItem>
{
    public string Header { get; set; } = "";
    public Func<TItem, string?> ValueAccessor { get; set; } = _ => null;
    public Func<TItem, IComparable?>? SortAccessor { get; set; }
    public double DefaultWidth { get; set; } = 100;
    public double MinWidth { get; set; } = 40;
    public bool IsSortable { get; set; } = true;
    public bool IsFilterable { get; set; } = true;
    public string? CssClass { get; set; }
    public RenderFragment<TItem>? CellTemplate { get; set; }
}
