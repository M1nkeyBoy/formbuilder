using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace StandaloneUiBuilder.Core;

// The document model is a tree of immutable records. Every edit produces a new
// ProjectDocument, which makes undo snapshots and dirty tracking cheap and reliable.

public sealed record ProjectDocument
{
    /// <summary>
    /// The format version this builder writes. Older versions (1: no anchors, 2: no containers,
    /// 3: no grid spans, 4: no sized grid rows and columns, 5: one screen, 6: the first seven
    /// control types only, 7: no button actions, 8: no fonts or colours) are still read. See
    /// docs/project-format.md for the history.
    /// </summary>
    public const int CurrentSchemaVersion = 9;

    public const int OldestSupportedSchemaVersion = 1;
    public const string DefaultName = "Untitled";

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required Guid ProjectId { get; init; }

    public string Name { get; init; } = DefaultName;

    /// <summary>The project's screens, in order. There is always at least one.</summary>
    public required ImmutableList<ScreenDocument> Screens { get; init; }

    /// <summary>The first screen: the one a generated application opens with.</summary>
    [JsonIgnore]
    public ScreenDocument MainScreen => Screens[0];

    public static ProjectDocument CreateBlank() => new()
    {
        ProjectId = Guid.NewGuid(),
        Screens = [new ScreenDocument()],
    };

    public ScreenDocument? FindScreen(string id) => Screens.FirstOrDefault(s => s.Id == id);

    /// <summary>Replaces the screen with the same ID.</summary>
    public ProjectDocument WithScreen(ScreenDocument screen)
    {
        var index = Screens.FindIndex(s => s.Id == screen.Id);
        if (index < 0)
        {
            throw new ArgumentException($"The project has no screen with ID \"{screen.Id}\".", nameof(screen));
        }

        return this with { Screens = Screens.SetItem(index, screen) };
    }
}

public sealed record ScreenDocument
{
    public const int DefaultWidth = 800;
    public const int DefaultHeight = 600;
    public const int DefaultGridSize = 10;

    public const string DefaultId = "main";
    public const string DefaultName = "Main";

    /// <summary>A stable ID, unique within the project. The first screen of a new project is "main".</summary>
    public string Id { get; init; } = DefaultId;

    /// <summary>An identifier, unique within the project; it names the screen's generated window.</summary>
    public string Name { get; init; } = DefaultName;

    public int Width { get; init; } = DefaultWidth;

    public int Height { get; init; } = DefaultHeight;

    public int GridSize { get; init; } = DefaultGridSize;

    /// <summary>Controls in draw order: later entries are drawn on top.</summary>
    public ImmutableList<ControlDocument> Controls { get; init; } = ImmutableList<ControlDocument>.Empty;
}

public sealed record ControlDocument
{
    public required Guid Id { get; init; }

    public required ControlType Type { get; init; }

    public required string Name { get; init; }

    public int X { get; init; }

    public int Y { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public ControlProperties Properties { get; init; } = new();

    /// <summary>
    /// Screen edges the control follows when the window is resized. Used only for controls
    /// placed directly on the screen; a container positions its own children.
    /// </summary>
    public AnchorEdges Anchor { get; init; } = AnchorEdges.Default;

    /// <summary>The controls inside a container, in order; null for other controls.</summary>
    public ImmutableList<ControlDocument>? Children { get; init; }

    /// <summary>The row of the cell this control fills, when it is inside a Grid.</summary>
    public int? Row { get; init; }

    /// <summary>The column of the cell this control fills, when it is inside a Grid.</summary>
    public int? Column { get; init; }

    /// <summary>Inside a Grid: how many rows the control covers; null means 1.</summary>
    public int? RowSpan { get; init; }

    /// <summary>Inside a Grid: how many columns the control covers; null means 1.</summary>
    public int? ColumnSpan { get; init; }

    [JsonIgnore]
    public ControlBounds Bounds => new(X, Y, Width, Height);

    public int Right() => X + Width;

    public int Bottom() => Y + Height;

    public ControlDocument WithBounds(ControlBounds bounds) =>
        this with { X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height };
}

/// <summary>
/// Type-specific values. A property is null when the control type does not support it;
/// <see cref="ControlDefinition.Normalize"/> enforces that.
/// </summary>
public sealed record ControlProperties
{
    public string? Text { get; init; }

    public bool? IsChecked { get; init; }

    public ImmutableList<string>? Items { get; init; }

    /// <summary>Controls that show text: the text size in DIPs; null for the default (12).</summary>
    public int? FontSize { get; init; }

    /// <summary>Controls that show text: true for bold text; null for normal.</summary>
    public bool? IsBold { get; init; }

    /// <summary>Controls that show text: the text colour as "#RRGGBB"; null for the default.</summary>
    public string? Foreground { get; init; }

    /// <summary>Any control: the background colour as "#RRGGBB"; null for the default.</summary>
    public string? Background { get; init; }

    /// <summary>Button: the ID of a screen the button opens, as a dialog over its own.</summary>
    public string? OpensScreen { get; init; }

    /// <summary>Button: true if the button closes its own screen.</summary>
    public bool? ClosesScreen { get; init; }

    /// <summary>TextBox: true for several lines of text that wrap; null for a single line.</summary>
    public bool? IsMultiline { get; init; }

    /// <summary>Slider and ProgressBar: the lowest value.</summary>
    public int? Minimum { get; init; }

    /// <summary>Slider and ProgressBar: the highest value.</summary>
    public int? Maximum { get; init; }

    /// <summary>Slider and ProgressBar: the current value, from Minimum to Maximum.</summary>
    public int? Value { get; init; }

    /// <summary>StackPanel and GroupBox: the direction children are lined up.</summary>
    public StackOrientation? Orientation { get; init; }

    /// <summary>StackPanel and GroupBox: the gap between children, in DIPs.</summary>
    public int? Spacing { get; init; }

    /// <summary>Grid: the number of rows.</summary>
    public int? Rows { get; init; }

    /// <summary>Grid: the number of columns.</summary>
    public int? Columns { get; init; }

    /// <summary>
    /// Grid: each row's size ("100", "*", "2*"), one per row; null when every row is an equal
    /// share. See <see cref="GridTrackSize"/>.
    /// </summary>
    public ImmutableList<string>? RowSizes { get; init; }

    /// <summary>Grid: each column's size, as for <see cref="RowSizes"/>.</summary>
    public ImmutableList<string>? ColumnSizes { get; init; }
}
