using System.Collections.Immutable;

namespace StandaloneUiBuilder.Core;

/// <summary>
/// Describes one control type: its default and minimum size and which type-specific
/// properties it supports.
/// </summary>
public sealed record ControlDefinition(
    ControlType Type,
    int DefaultWidth,
    int DefaultHeight,
    int MinWidth,
    int MinHeight,
    bool HasText,
    bool HasIsChecked,
    bool HasItems,
    bool IsStack = false,
    bool IsGrid = false)
{
    public const int MaxSpacing = 200;
    public const int MaxRowsOrColumns = 20;

    /// <summary>True for types that hold child controls.</summary>
    public bool IsContainer => IsStack || IsGrid;

    public ControlProperties CreateDefaultProperties(string name) => Type switch
    {
        ControlType.TextBox => new ControlProperties { Text = "" },
        ControlType.CheckBox => new ControlProperties { Text = name, IsChecked = false },
        ControlType.ComboBox => new ControlProperties { Items = ["Item 1", "Item 2", "Item 3"] },
        ControlType.StackPanel => new ControlProperties { Orientation = StackOrientation.Vertical, Spacing = 6 },
        ControlType.Grid => new ControlProperties { Rows = 2, Columns = 2 },
        _ => new ControlProperties { Text = name },
    };

    /// <summary>
    /// Returns properties containing exactly the values this type supports, filling any
    /// missing ones with neutral defaults and dropping the rest.
    /// </summary>
    public ControlProperties Normalize(ControlProperties properties) => new()
    {
        Text = HasText ? properties.Text ?? "" : null,
        IsChecked = HasIsChecked ? properties.IsChecked ?? false : null,
        Items = HasItems ? properties.Items ?? ImmutableList<string>.Empty : null,
        Orientation = IsStack ? properties.Orientation ?? StackOrientation.Vertical : null,
        Spacing = IsStack ? properties.Spacing ?? 0 : null,
        Rows = IsGrid ? properties.Rows ?? 1 : null,
        Columns = IsGrid ? properties.Columns ?? 1 : null,
        RowSizes = IsGrid ? properties.RowSizes : null,
        ColumnSizes = IsGrid ? properties.ColumnSizes : null,
    };
}

/// <summary>The registry of built-in control types.</summary>
public static class ControlCatalog
{
    public static IReadOnlyList<ControlDefinition> All { get; } =
    [
        new(ControlType.Label, 80, 20, 20, 16, HasText: true, HasIsChecked: false, HasItems: false),
        new(ControlType.Button, 100, 30, 30, 20, HasText: true, HasIsChecked: false, HasItems: false),
        new(ControlType.TextBox, 120, 30, 30, 20, HasText: true, HasIsChecked: false, HasItems: false),
        new(ControlType.CheckBox, 100, 20, 20, 16, HasText: true, HasIsChecked: true, HasItems: false),
        new(ControlType.ComboBox, 120, 30, 40, 20, HasText: false, HasIsChecked: false, HasItems: true),
        new(ControlType.StackPanel, 200, 150, 20, 20, HasText: false, HasIsChecked: false, HasItems: false, IsStack: true),
        new(ControlType.Grid, 240, 160, 20, 20, HasText: false, HasIsChecked: false, HasItems: false, IsGrid: true),
    ];

    public static bool TryGet(ControlType type, out ControlDefinition definition)
    {
        foreach (var candidate in All)
        {
            if (candidate.Type == type)
            {
                definition = candidate;
                return true;
            }
        }

        definition = null!;
        return false;
    }

    public static ControlDefinition Get(ControlType type) =>
        TryGet(type, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown control type.");
}
