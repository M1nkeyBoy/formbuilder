using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace StandaloneUiBuilder.Core;

// The document model is a tree of immutable records. Every edit produces a new
// ProjectDocument, which makes undo snapshots and dirty tracking cheap and reliable.

public sealed record ProjectDocument
{
    /// <summary>
    /// The format version this builder writes. Version 1 files (no anchors) are still read.
    /// See docs/project-format.md for the history.
    /// </summary>
    public const int CurrentSchemaVersion = 2;

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

    /// <summary>Screen edges the control follows when the window is resized.</summary>
    public AnchorEdges Anchor { get; init; } = AnchorEdges.Default;

    [JsonIgnore]
    public ControlBounds Bounds => new(X, Y, Width, Height);

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
}
