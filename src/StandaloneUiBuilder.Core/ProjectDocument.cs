using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace StandaloneUiBuilder.Core;

// The document model is a tree of immutable records. Every edit produces a new
// ProjectDocument, which makes undo snapshots and dirty tracking cheap and reliable.

public sealed record ProjectDocument
{
    /// <summary>
    /// The format version this builder writes. Versions 1 (no anchors) and 2 (no containers)
    /// are still read. See docs/project-format.md for the history.
    /// </summary>
    public const int CurrentSchemaVersion = 3;

    public const int OldestSupportedSchemaVersion = 1;
    public const string DefaultName = "Untitled";

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required Guid ProjectId { get; init; }

    public string Name { get; init; } = DefaultName;

    public required ScreenDocument Screen { get; init; }

    public static ProjectDocument CreateBlank() => new()
    {
        ProjectId = Guid.NewGuid(),
        Screen = new ScreenDocument(),
    };
}

public sealed record ScreenDocument
{
    public const int DefaultWidth = 800;
    public const int DefaultHeight = 600;
    public const int DefaultGridSize = 10;

    public string Id { get; init; } = "main";

    public string Name { get; init; } = "Main";

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

    /// <summary>StackPanel: the direction children are lined up.</summary>
    public StackOrientation? Orientation { get; init; }

    /// <summary>StackPanel: the gap between children, in DIPs.</summary>
    public int? Spacing { get; init; }

    /// <summary>Grid: the number of equal rows.</summary>
    public int? Rows { get; init; }

    /// <summary>Grid: the number of equal columns.</summary>
    public int? Columns { get; init; }
}
