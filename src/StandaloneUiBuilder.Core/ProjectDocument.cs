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
    /// control types only, 7: no button actions, 8: no fonts or colours, 9: no images, 10: no tab controls, 11: no tab order, 12: no theme, 13: no data binding, 14: no commands, 15: no enabled bindings, 16: no screen code, 17: no platform, 18: no control libraries, 19: no style) are still
    /// read. See docs/project-format.md for the history.
    /// </summary>
    public const int CurrentSchemaVersion = 20;

    public const int OldestSupportedSchemaVersion = 1;
    public const string DefaultName = "Untitled";

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required Guid ProjectId { get; init; }

    public string Name { get; init; } = DefaultName;

    /// <summary>The platform the screens are for; not stored when Any (every platform).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ProjectPlatform Platform { get; init; }

    /// <summary>The control libraries the project uses, in the order they were added; null for none.</summary>
    public ImmutableList<LibraryPackage>? Libraries { get; init; }

    /// <summary>
    /// Licence keys for library vendors the builder registers keys for (Syncfusion), by vendor;
    /// null for none. Every export registers them when the app starts.
    /// </summary>
    public ImmutableSortedDictionary<string, string>? LicenseKeys { get; init; }

    /// <summary>Light or dark colours for every screen's standard controls; not stored when Light.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ProjectTheme Theme { get; init; }

    /// <summary>
    /// Classic or modern controls; not stored when Classic, the look of projects made before
    /// there was a choice. New projects are modern.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ProjectStyle Style { get; init; }

    /// <summary>
    /// Whether WPF draws the controls with its Fluent styles: in the modern style, and in the
    /// dark and system themes, which WPF only offers in Fluent.
    /// </summary>
    [JsonIgnore]
    public bool UsesFluent => Style == ProjectStyle.Modern || Theme != ProjectTheme.Light;

    /// <summary>The project's screens, in order. There is always at least one.</summary>
    public required ImmutableList<ScreenDocument> Screens { get; init; }

    /// <summary>The first screen: the one a generated application opens with.</summary>
    [JsonIgnore]
    public ScreenDocument MainScreen => Screens[0];

    public static ProjectDocument CreateBlank(ProjectPlatform platform = ProjectPlatform.Any) => new()
    {
        ProjectId = Guid.NewGuid(),
        Platform = platform,
        Style = ProjectStyle.Modern,
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

    /// <summary>
    /// C# members of the screen's view model written in the builder, such as the command and
    /// change hooks it implements; null for none. See <see cref="DataBindings.Hooks"/>.
    /// </summary>
    public string? Code { get; init; }

    /// <summary>Controls in draw order: later entries are drawn on top.</summary>
    public ImmutableList<ControlDocument> Controls { get; init; } = ImmutableList<ControlDocument>.Empty;

    /// <summary>
    /// The order Tab moves through the controls that take focus, by ID; null for the order the
    /// controls are in. Controls not listed come after the listed ones. See <see cref="TabSequence"/>.
    /// </summary>
    public ImmutableList<Guid>? TabOrder { get; init; }
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

    /// <summary>Image: the picture file's bytes, as base64; null for no picture yet.</summary>
    public string? ImageData { get; init; }

    /// <summary>Image: how the picture fills the box.</summary>
    public ImageStretch? Stretch { get; init; }

    /// <summary>Controls that show text: the text size in DIPs; null for the default (12).</summary>
    public int? FontSize { get; init; }

    /// <summary>Controls that show text: true for bold text; null for normal.</summary>
    public bool? IsBold { get; init; }

    /// <summary>Controls that show text: the text colour as "#RRGGBB"; null for the default.</summary>
    public string? Foreground { get; init; }

    /// <summary>Any control: the background colour as "#RRGGBB"; null for the default.</summary>
    public string? Background { get; init; }

    /// <summary>
    /// Controls with a value: the name of the property of the screen's view model that the value
    /// is bound to; null for none. See <see cref="DataBindings"/>.
    /// </summary>
    public string? Binding { get; init; }

    /// <summary>
    /// Button: the name of the command of the screen's view model the button runs when clicked,
    /// a method calling a partial On… method; null for none. See <see cref="DataBindings"/>.
    /// </summary>
    public string? Command { get; init; }

    /// <summary>
    /// Button: the name of an on-or-off property of the screen's view model that enables the
    /// button while it is true; null for always enabled. See <see cref="DataBindings"/>.
    /// </summary>
    public string? EnabledBinding { get; init; }

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

    /// <summary>TabControl: the index of the page shown, in the designer and when the screen opens.</summary>
    public int? SelectedTab { get; init; }

    /// <summary>StackPanel, GroupBox and TabPage: the direction children are lined up.</summary>
    public StackOrientation? Orientation { get; init; }

    /// <summary>StackPanel, GroupBox and TabPage: the gap between children, in DIPs.</summary>
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

    /// <summary>Custom: the library control's full type name. See <see cref="LibraryControl"/>.</summary>
    public string? LibraryType { get; init; }

    /// <summary>Custom: the assembly that defines the type.</summary>
    public string? LibraryAssembly { get; init; }

    /// <summary>Custom: the values set on the control, by property name; null for none.</summary>
    public ImmutableList<LibrarySetting>? LibrarySettings { get; init; }
}
