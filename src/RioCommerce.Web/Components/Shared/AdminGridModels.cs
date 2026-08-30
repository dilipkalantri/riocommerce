using Microsoft.AspNetCore.Components;

namespace RioCommerce.Web.Components.Shared;

// Column visibility priority → maps to responsive auto-hide (Medium hides on tablet, Low on laptop).
public enum GridPriority { High, Medium, Low }

// A declarative, typed column definition. Pages build a List<AdminGridColumn<T>> in code and pass it to AdminGrid.
public class AdminGridColumn<TItem>
{
    public string Title { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;        // stable id for show/hide persistence (defaults to Title)
    public bool Sortable { get; set; }
    public string? SortKey { get; set; }                   // value sent to OnSort (defaults to Key)
    public GridPriority Priority { get; set; } = GridPriority.High;
    public string? Align { get; set; }                     // "right" | "center"
    public bool DefaultHidden { get; set; }
    public RenderFragment<TItem> Cell { get; set; } = _ => _ => { };

    public AdminGridColumn() { }
    public AdminGridColumn(string title) { Title = title; Key = title; }

    public string EffectiveSortKey => SortKey ?? Key;
    public string PriorityClass => Priority switch
    {
        GridPriority.Medium => "col-hide-md",
        GridPriority.Low => "col-hide-lg",
        _ => ""
    };
    public string AlignClass => Align switch { "right" => "ag-right", "center" => "ag-center", _ => "" };
}
