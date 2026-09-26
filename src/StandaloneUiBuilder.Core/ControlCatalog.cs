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
    bool IsGrid = false,
    bool HasRange = false,
    bool HasMultiline = false,
    bool IsTabs = false)
{
    public const int MaxSpacing = 200;
    public const int MaxRowsOrColumns = 20;

    /// <summary>The largest magnitude a Slider or ProgressBar value may have.</summary>
    public const int MaxRangeValue = 1_000_000;

    /// <summary>True for types that hold child controls.</summary>
    public bool IsContainer => IsStack || IsGrid || IsTabs;

    /// <summary>
    /// False for types that are not placed from the toolbox: a TabPage comes with its
    /// TabControl, or from its Add tab command.
    /// </summary>
    public bool InToolbox => Type != ControlType.TabPage;

    /// <summary>True if a container of this type can hold a control of the given type.</summary>
    public bool CanHold(ControlType child) =>
        IsContainer && (Type == ControlType.TabControl) == (child == ControlType.TabPage);

    /// <summary>True for types that show text, and so have a font and a text colour.</summary>
    public bool HasFont => Type is ControlType.Label or ControlType.Button or ControlType.TextBox or ControlType.PasswordBox
        or ControlType.CheckBox or ControlType.RadioButton or ControlType.ComboBox or ControlType.ListBox
        or ControlType.DatePicker or ControlType.GroupBox;

    /// <summary>The text size used when a control has none of its own, in DIPs.</summary>
    public const int DefaultFontSize = 12;

    public const int MinFontSize = 6;

    public const int MaxFontSize = 72;

    /// <summary>True for types that can have a background colour: all but Image and TabControl (colour its pages instead).</summary>
    public bool HasBackground => Type is not (ControlType.Image or ControlType.TabControl);

    /// <summary>True for types that show a picture.</summary>
    public bool HasImage => Type == ControlType.Image;

    /// <summary>True for types the user can reach with Tab: the ones that take input.</summary>
    public bool IsTabStop => Type is ControlType.Button or ControlType.TextBox or ControlType.PasswordBox
        or ControlType.CheckBox or ControlType.RadioButton or ControlType.ComboBox or ControlType.ListBox
        or ControlType.Slider or ControlType.DatePicker or ControlType.TabControl;

    /// <summary>True for types that can open or close a screen when clicked.</summary>
    public bool HasAction => Type == ControlType.Button;

    public ControlProperties CreateDefaultProperties(string name) => Type switch
    {
        ControlType.TextBox => new ControlProperties { Text = "" },
        ControlType.CheckBox or ControlType.RadioButton => new ControlProperties { Text = name, IsChecked = false },
        ControlType.ComboBox or ControlType.ListBox => new ControlProperties { Items = ["Item 1", "Item 2", "Item 3"] },
        ControlType.Slider or ControlType.ProgressBar => new ControlProperties { Minimum = 0, Maximum = 100, Value = 50 },
        ControlType.DatePicker or ControlType.PasswordBox => new ControlProperties(),
        ControlType.Image => new ControlProperties { Stretch = ImageStretch.Uniform },
        ControlType.StackPanel => new ControlProperties { Orientation = StackOrientation.Vertical, Spacing = 6 },
        ControlType.GroupBox => new ControlProperties { Text = name, Orientation = StackOrientation.Vertical, Spacing = 6 },
        ControlType.Grid => new ControlProperties { Rows = 2, Columns = 2 },
        ControlType.TabControl => new ControlProperties { SelectedTab = 0 },
        ControlType.TabPage => new ControlProperties { Text = name, Orientation = StackOrientation.Vertical, Spacing = 6 },
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
        SelectedTab = IsTabs ? properties.SelectedTab ?? 0 : null,

        ImageData = HasImage ? properties.ImageData : null,
        Stretch = HasImage ? properties.Stretch ?? ImageStretch.Uniform : null,
        FontSize = HasFont ? properties.FontSize : null,
        IsBold = HasFont && properties.IsBold == true ? true : null,
        Foreground = HasFont ? properties.Foreground : null,
        Background = HasBackground ? properties.Background : null,
        OpensScreen = HasAction ? properties.OpensScreen : null,
        ClosesScreen = HasAction && properties.ClosesScreen == true ? true : null,

        // Stored only when true, so single-line text boxes read and write as before.
        IsMultiline = HasMultiline && properties.IsMultiline == true ? true : null,
        Minimum = HasRange ? properties.Minimum ?? 0 : null,
        Maximum = HasRange ? properties.Maximum ?? 100 : null,
        Value = HasRange ? properties.Value ?? properties.Minimum ?? 0 : null,
    };
}

/// <summary>The registry of built-in control types.</summary>
public static class ControlCatalog
{
    public static IReadOnlyList<ControlDefinition> All { get; } =
    [
        new(ControlType.Label, 80, 20, 20, 16, HasText: true, HasIsChecked: false, HasItems: false),
        new(ControlType.Button, 100, 30, 30, 20, HasText: true, HasIsChecked: false, HasItems: false),
        new(ControlType.TextBox, 120, 30, 30, 20, HasText: true, HasIsChecked: false, HasItems: false, HasMultiline: true),
        new(ControlType.PasswordBox, 120, 30, 30, 20, HasText: false, HasIsChecked: false, HasItems: false),
        new(ControlType.CheckBox, 100, 20, 20, 16, HasText: true, HasIsChecked: true, HasItems: false),
        new(ControlType.RadioButton, 100, 20, 20, 16, HasText: true, HasIsChecked: true, HasItems: false),
        new(ControlType.ComboBox, 120, 30, 40, 20, HasText: false, HasIsChecked: false, HasItems: true),
        new(ControlType.ListBox, 120, 100, 40, 30, HasText: false, HasIsChecked: false, HasItems: true),
        new(ControlType.Slider, 150, 30, 40, 20, HasText: false, HasIsChecked: false, HasItems: false, HasRange: true),
        new(ControlType.ProgressBar, 150, 20, 20, 8, HasText: false, HasIsChecked: false, HasItems: false, HasRange: true),
        new(ControlType.DatePicker, 140, 30, 80, 20, HasText: false, HasIsChecked: false, HasItems: false),
        new(ControlType.Image, 120, 90, 8, 8, HasText: false, HasIsChecked: false, HasItems: false),
        new(ControlType.StackPanel, 200, 150, 20, 20, HasText: false, HasIsChecked: false, HasItems: false, IsStack: true),
        new(ControlType.Grid, 240, 160, 20, 20, HasText: false, HasIsChecked: false, HasItems: false, IsGrid: true),
        new(ControlType.GroupBox, 220, 160, 40, 40, HasText: true, HasIsChecked: false, HasItems: false, IsStack: true),
        new(ControlType.TabControl, 300, 200, 60, 60, HasText: false, HasIsChecked: false, HasItems: false, IsTabs: true),
        new(ControlType.TabPage, 200, 150, 20, 20, HasText: true, HasIsChecked: false, HasItems: false, IsStack: true),
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
