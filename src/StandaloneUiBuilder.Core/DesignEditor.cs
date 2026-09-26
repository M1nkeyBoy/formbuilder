using System.Collections.Immutable;

namespace StandaloneUiBuilder.Core;

/// <summary>
/// Owns the document being edited, its undo history and whether it differs from the last save.
/// The UI reads <see cref="Document"/> and renders from it; it never edits the document directly.
/// Each successful edit method is one undoable step. Edits that change nothing record nothing.
/// Methods that can reject a value return a plain-language error message, or null on success.
/// </summary>
public sealed partial class DesignEditor
{
    // Each history entry remembers which screen was showing before and after the change, so undo
    // and redo return to the screen where the change is visible. An undo entry holds the
    // document before the change; a redo entry, the document after it.
    private readonly Stack<HistoryEntry> undoStack = new();
    private readonly Stack<HistoryEntry> redoStack = new();
    private ProjectDocument? savedDocument;
    private string activeScreenId;

    public DesignEditor()
        : this(ProjectDocument.CreateBlank())
    {
    }

    public DesignEditor(ProjectDocument document)
    {
        Document = document;
        savedDocument = document;
        activeScreenId = document.MainScreen.Id;
    }

    /// <summary>Raised after any change to <see cref="Document"/> or <see cref="IsDirty"/>.</summary>
    public event EventHandler? Changed;

    public ProjectDocument Document { get; private set; }

    /// <summary>
    /// The screen being edited. Which screen is showing is editor state, like the selection:
    /// it is not saved and changing it does not change the document.
    /// </summary>
    public ScreenDocument Screen => Document.FindScreen(activeScreenId) ?? Document.MainScreen;

    public bool CanUndo => undoStack.Count > 0;

    public bool CanRedo => redoStack.Count > 0;

    /// <summary>True when the document has changed since it was created, opened or saved.</summary>
    public bool IsDirty => !ReferenceEquals(Document, savedDocument);

    /// <summary>Starts a new blank document.</summary>
    public void New() => Reset(ProjectDocument.CreateBlank());

    /// <summary>Replaces the document, e.g. after opening a file, and clears undo history.</summary>
    public void Reset(ProjectDocument document, bool isDirty = false)
    {
        undoStack.Clear();
        redoStack.Clear();
        Document = document;
        savedDocument = isDirty ? null : document;
        activeScreenId = document.MainScreen.Id;
        OnChanged();
    }

    /// <summary>Finds a control anywhere, including inside containers.</summary>
    public ControlDocument? FindControl(Guid id) => ControlTree.Find(Screen.Controls, id);

    /// <summary>The container a control is in, or null if it is directly on the screen.</summary>
    public ControlDocument? ParentOf(Guid id) => ControlTree.ParentOf(Screen.Controls, id);

    /// <summary>
    /// Adds a control of the given type with its top-left corner at a point, snapped to the
    /// grid and kept inside the screen. The new control is drawn on top of existing ones.
    /// </summary>
    public ControlDocument AddControl(ControlType type, double x, double y)
    {
        var definition = ControlCatalog.Get(type);
        if (!definition.InToolbox)
        {
            throw new ArgumentException($"A {type} cannot be placed on the screen.", nameof(type));
        }

        var screen = Screen;
        var control = NewControl(screen, type).WithBounds(DesignGeometry.Place(screen, definition, x, y));

        Commit(Document.WithScreen(screen with { Controls = screen.Controls.Add(control) }));
        return control;
    }

    /// <summary>
    /// Adds a new control inside a container, where a screen point falls: at that position in
    /// a StackPanel, or in that cell of a Grid. Returns null if the container does not exist or
    /// cannot hold the type (see <see cref="ControlDefinition.CanHold"/>).
    /// </summary>
    public ControlDocument? AddControlTo(ControlType type, Guid containerId, double x, double y)
    {
        var screen = Screen;
        if (Placed(containerId) is not { } container || !ControlCatalog.Get(container.Control.Type).CanHold(type))
        {
            return null;
        }

        var control = NewControl(screen, type);
        var (index, row, column) = DropPosition(container, x, y, ignore: null);
        control = control with { Row = row, Column = column };
        Commit(Document.WithScreen(screen with { Controls = ControlTree.Insert(screen.Controls, containerId, index, control) }));
        return control;
    }

    /// <summary>
    /// Moves a control (and anything inside it) into a container, where a screen point falls.
    /// A control cannot be moved into itself or into a container inside it.
    /// </summary>
    public string? MoveIntoContainer(Guid id, Guid containerId, double x, double y)
    {
        var screen = Screen;
        if (FindControl(id) is not { } control || Placed(containerId) is not { } container)
        {
            return "The control no longer exists.";
        }

        if (ControlTree.IsSelfOrDescendant(screen.Controls, id, containerId))
        {
            return "A container cannot go inside itself.";
        }

        if (!ControlCatalog.Get(container.Control.Type).CanHold(control.Type))
        {
            return control.Type == ControlType.TabPage
                ? "A tab page can only go into a TabControl."
                : $"A TabControl holds only tab pages; put \"{control.Name}\" on one of its pages.";
        }

        var (index, row, column) = DropPosition(container, x, y, ignore: id);
        // Moving to another cell of the same grid keeps the span when it still fits there.
        var keepSpan = ParentOf(id)?.Id == containerId && row is { } r && column is { } c
            && r + (control.RowSpan ?? 1) <= (container.Control.Properties.Rows ?? 1)
            && c + (control.ColumnSpan ?? 1) <= (container.Control.Properties.Columns ?? 1);
        var moved = control with
        {
            X = 0,
            Y = 0,
            Anchor = AnchorEdges.Default,
            Row = row,
            Column = column,
            RowSpan = keepSpan ? control.RowSpan : null,
            ColumnSpan = keepSpan ? control.ColumnSpan : null,
        };
        var controls = ControlTree.Insert(ControlTree.Remove(screen.Controls, [id]), containerId, index, moved);
        if (moved.Type == ControlType.TabPage)
        {
            // The moved page is the one shown in its new TabControl.
            controls = ControlTree.Replace(controls, containerId, tabs =>
                tabs with { Properties = tabs.Properties with { SelectedTab = tabs.Children!.FindIndex(c => c.Id == id) } });
        }

        controls = ControlTree.KeepShownTabs(controls);
        if (!ControlTree.All(controls).SequenceEqual(ControlTree.All(screen.Controls)))
        {
            Commit(Document.WithScreen(screen with { Controls = controls }));
        }

        return null;
    }

    /// <summary>
    /// Takes a control out of its container and puts it on the screen with its top-left at a
    /// point, snapped to the grid, keeping its designed size.
    /// </summary>
    public string? MoveToScreen(Guid id, double x, double y)
    {
        var screen = Screen;
        if (FindControl(id) is not { } control)
        {
            return "The control no longer exists.";
        }

        if (ControlTree.IsRoot(screen.Controls, id))
        {
            return null;
        }

        if (control.Type == ControlType.TabPage)
        {
            return "A tab page stays in a TabControl. Drag it onto another TabControl, or delete it.";
        }

        var size = new ControlDefinition(control.Type, Math.Min(control.Width, screen.Width), Math.Min(control.Height, screen.Height), 0, 0, false, false, false);
        var moved = control.WithBounds(DesignGeometry.Place(screen, size, x, y)) with { Row = null, Column = null, RowSpan = null, ColumnSpan = null };
        var controls = ControlTree.KeepShownTabs(ControlTree.Remove(screen.Controls, [id])).Add(moved);
        Commit(Document.WithScreen(screen with { Controls = controls }));
        return null;
    }

    /// <summary>Moves a control earlier (negative) or later (positive) among its container's children.</summary>
    public bool MoveWithinContainer(Guid id, int delta)
    {
        if (ParentOf(id) is not { Children: { } children } parent)
        {
            return false;
        }

        var index = children.FindIndex(c => c.Id == id);
        var target = Math.Clamp(index + delta, 0, children.Count - 1);
        if (target == index)
        {
            return false;
        }

        var reordered = children.RemoveAt(index).Insert(target, children[index]);
        var properties = parent.Properties;
        if (parent.Type == ControlType.TabControl)
        {
            // The page that was showing still is.
            var shown = children[ContainerLayout.ShownTab(parent)].Id;
            properties = properties with { SelectedTab = reordered.FindIndex(c => c.Id == shown) };
        }

        Replace(parent, parent with { Children = reordered, Properties = properties });
        return true;
    }

    /// <summary>
    /// Sets the size of a control inside a StackPanel or GroupBox along the stack's direction:
    /// its height in a vertical stack, its width in a horizontal one.
    /// </summary>
    public string? SetStackSize(Guid id, int size)
    {
        if (FindControl(id) is not { } control || ParentOf(id) is not { } stack || !ControlCatalog.Get(stack.Type).IsStack)
        {
            return "The control is not in a StackPanel or GroupBox.";
        }

        var definition = ControlCatalog.Get(control.Type);
        var vertical = stack.Properties.Orientation != StackOrientation.Horizontal;
        var minimum = vertical ? definition.MinHeight : definition.MinWidth;
        if (size < minimum || size > 10000)
        {
            return $"{(vertical ? "Height" : "Width")} must be between {minimum} and 10000 for a {control.Type}.";
        }

        var resized = vertical ? control with { Height = size } : control with { Width = size };
        if (resized != control)
        {
            Replace(control, resized);
        }

        return null;
    }

    /// <summary>Sets how many rows and columns a control inside a Grid covers. It must still fit.</summary>
    public string? SetGridSpan(Guid id, int rowSpan, int columnSpan)
    {
        if (FindControl(id) is not { } control || ParentOf(id) is not { Type: ControlType.Grid } grid)
        {
            return "The control is not in a Grid.";
        }

        var rows = grid.Properties.Rows ?? 1;
        var columns = grid.Properties.Columns ?? 1;
        var row = control.Row ?? 0;
        var column = control.Column ?? 0;
        if (rowSpan < 1 || columnSpan < 1 || row + rowSpan > rows || column + columnSpan > columns)
        {
            return $"From row {row}, column {column} it can span up to {rows - row} rows and {columns - column} columns.";
        }

        // A span of 1 is the default and is not stored.
        var spanned = control with { RowSpan = rowSpan == 1 ? null : rowSpan, ColumnSpan = columnSpan == 1 ? null : columnSpan };
        if (spanned != control)
        {
            Replace(control, spanned);
        }

        return null;
    }

    /// <summary>Puts a control inside a Grid into another cell. With its span, it must still fit.</summary>
    public string? SetGridCell(Guid id, int row, int column)
    {
        if (FindControl(id) is not { } control || ParentOf(id) is not { Type: ControlType.Grid } grid)
        {
            return "The control is not in a Grid.";
        }

        var rows = grid.Properties.Rows ?? 1;
        var columns = grid.Properties.Columns ?? 1;
        if (row < 0 || row >= rows || column < 0 || column >= columns)
        {
            return $"Row must be 0 to {rows - 1} and column 0 to {columns - 1}.";
        }

        if (row + (control.RowSpan ?? 1) > rows || column + (control.ColumnSpan ?? 1) > columns)
        {
            return $"With its span of {control.RowSpan ?? 1} × {control.ColumnSpan ?? 1} cells it would not fit there. Reduce the span first.";
        }

        if (control.Row != row || control.Column != column)
        {
            Replace(control, control with { Row = row, Column = column });
        }

        return null;
    }

    public string? SetOrientation(Guid id, StackOrientation orientation) =>
        EditProperties(id, d => d.IsStack, "an orientation", p => p.Orientation == orientation ? p : p with { Orientation = orientation });

    public string? SetSpacing(Guid id, int spacing) =>
        spacing is < 0 or > ControlDefinition.MaxSpacing
            ? $"Spacing must be between 0 and {ControlDefinition.MaxSpacing}."
            : EditProperties(id, d => d.IsStack, "spacing", p => p.Spacing == spacing ? p : p with { Spacing = spacing });

    /// <summary>Changes a Grid's rows and columns. Every child must still have a cell.</summary>
    public string? SetGridSize(Guid id, int rows, int columns)
    {
        if (FindControl(id) is not { Type: ControlType.Grid } grid)
        {
            return "The control is not a Grid.";
        }

        if (rows is < 1 or > ControlDefinition.MaxRowsOrColumns || columns is < 1 or > ControlDefinition.MaxRowsOrColumns)
        {
            return $"Rows and columns must be between 1 and {ControlDefinition.MaxRowsOrColumns}.";
        }

        if (grid.Children?.FirstOrDefault(c => (c.Row ?? 0) + (c.RowSpan ?? 1) > rows || (c.Column ?? 0) + (c.ColumnSpan ?? 1) > columns) is { } outside)
        {
            return $"\"{outside.Name}\" uses row {outside.Row}, column {outside.Column} and its span, which would no longer all exist. Move it or reduce its span first.";
        }

        // Existing sizes are kept for the rows and columns that remain; new ones are equal shares.
        return EditProperties(id, d => d.IsGrid, "rows and columns", p =>
            p.Rows == rows && p.Columns == columns ? p : p with
            {
                Rows = rows,
                Columns = columns,
                RowSizes = ResizeTracks(p.RowSizes, rows),
                ColumnSizes = ResizeTracks(p.ColumnSizes, columns),
            });
    }

    /// <summary>
    /// Sets each row's and column's size: fixed DIPs ("100") or a share of the space left
    /// ("*", "2*"). There must be one size per row and per column.
    /// </summary>
    public string? SetGridTrackSizes(Guid id, IReadOnlyList<string> rowSizes, IReadOnlyList<string> columnSizes)
    {
        if (FindControl(id) is not { Type: ControlType.Grid } grid)
        {
            return "The control is not a Grid.";
        }

        var rows = grid.Properties.Rows ?? 1;
        var columns = grid.Properties.Columns ?? 1;
        if ((ValidateTracks(rowSizes, rows, "row") ?? ValidateTracks(columnSizes, columns, "column")) is { } error)
        {
            return error;
        }

        var newRows = Canonical(rowSizes);
        var newColumns = Canonical(columnSizes);
        return EditProperties(id, d => d.IsGrid, "row and column sizes", p =>
            SameTracks(p.RowSizes, newRows) && SameTracks(p.ColumnSizes, newColumns) ? p : p with { RowSizes = newRows, ColumnSizes = newColumns });
    }

    /// <summary>Returns an error message if the sizes are not one valid size per row or column.</summary>
    public static string? ValidateTracks(IReadOnlyList<string> sizes, int count, string what)
    {
        if (sizes.Count != count)
        {
            return $"Give {count} {what} size{(count == 1 ? "" : "s")}, one per {what}, separated by commas.";
        }

        var invalid = sizes.FirstOrDefault(s => !GridTrackSize.TryParse(s, out _));
        return invalid is null ? null
            : $"\"{invalid.Trim()}\" is not a {what} size. Use a number of DIPs (1 to {GridTrackSize.MaxFixed}) or a share such as * or 2*.";
    }

    // Stored in WPF's form ("2*" rather than " 2 *"), and not stored at all when every size is
    // an equal share, which is the default.
    private static ImmutableList<string>? Canonical(IReadOnlyList<string> sizes)
    {
        var parsed = sizes.Select(s => GridTrackSize.TryParse(s, out var size) ? size : GridTrackSize.Share).ToList();
        return parsed.All(s => s == GridTrackSize.Share) ? null : parsed.Select(s => s.ToString()).ToImmutableList();
    }

    private static ImmutableList<string>? ResizeTracks(ImmutableList<string>? sizes, int count) =>
        sizes is null ? null
        : Canonical(sizes.Take(count).Concat(Enumerable.Repeat("*", Math.Max(0, count - sizes.Count))).ToList());

    private static bool SameTracks(ImmutableList<string>? a, ImmutableList<string>? b) =>
        a is null ? b is null : b is not null && a.SequenceEqual(b);

    /// <summary>Removes a control (and anything inside it). Returns false if no control has that ID.</summary>
    public bool DeleteControl(Guid id) => DeleteControls([id]) > 0;

    /// <summary>Moves and/or resizes a control to exact bounds.</summary>
    public string? SetBounds(Guid id, ControlBounds bounds)
    {
        if (FindControl(id) is not { } control)
        {
            return "The control no longer exists.";
        }

        if (!ControlTree.IsRoot(Screen.Controls, id))
        {
            return "Its container decides where this control goes.";
        }

        if (DocumentValidator.ValidateBounds(Screen, control.Type, bounds) is { } error)
        {
            return error;
        }

        if (control.Bounds != bounds)
        {
            Replace(control, control.WithBounds(bounds));
        }

        return null;
    }

    public string? Rename(Guid id, string name)
    {
        if (FindControl(id) is not { } control)
        {
            return "The control no longer exists.";
        }

        name = name.Trim();
        if (DocumentValidator.ValidateName(Screen, id, name) is { } error)
        {
            return error;
        }

        if (control.Name != name)
        {
            Replace(control, control with { Name = name });
        }

        return null;
    }

    public string? SetAnchor(Guid id, AnchorEdges anchor)
    {
        if (FindControl(id) is not { } control)
        {
            return "The control no longer exists.";
        }

        if (AnchorLayout.Validate(anchor) is { } error)
        {
            return error;
        }

        if (control.Anchor != anchor)
        {
            Replace(control, control with { Anchor = anchor });
        }

        return null;
    }

    public string? SetText(Guid id, string text) =>
        EditProperties(id, d => d.HasText, "text", p => p.Text == text ? p : p with { Text = text });

    /// <summary>
    /// Ticks or clears a CheckBox or RadioButton. Choosing a RadioButton clears the other
    /// RadioButtons beside it (in the same container, or directly on the screen) in the same
    /// step, as the running application would.
    /// </summary>
    public string? SetIsChecked(Guid id, bool isChecked)
    {
        if (FindControl(id) is not { Type: ControlType.RadioButton } radio || !isChecked)
        {
            return EditProperties(id, d => d.HasIsChecked, "a checked state", p => p.IsChecked == isChecked ? p : p with { IsChecked = isChecked });
        }

        static ControlDocument Choose(ControlDocument control, Guid chosen) =>
            control.Type != ControlType.RadioButton ? control
            : control.Properties.IsChecked == (control.Id == chosen) ? control
            : control with { Properties = control.Properties with { IsChecked = control.Id == chosen } };

        var screen = Screen;
        var controls = ParentOf(id) is { } parent
            ? ControlTree.Replace(screen.Controls, parent.Id, p => p with { Children = p.Children!.ConvertAll(c => Choose(c, radio.Id)) })
            : screen.Controls.ConvertAll(c => Choose(c, radio.Id));
        if (!controls.SequenceEqual(screen.Controls))
        {
            Commit(Document.WithScreen(screen with { Controls = controls }));
        }

        return null;
    }

    /// <summary>
    /// Sets what a Button does when clicked, besides calling its hook: open another screen
    /// (by ID), close its own screen, or neither (both null or false).
    /// </summary>
    public string? SetButtonAction(Guid id, string? opensScreen, bool closesScreen)
    {
        if (opensScreen is not null && closesScreen)
        {
            return "A button can open a screen or close its own, not both.";
        }

        if (opensScreen is not null && Document.FindScreen(opensScreen) is null)
        {
            return "That screen no longer exists.";
        }

        return EditProperties(id, d => d.HasAction, "an action", p =>
            p.OpensScreen == opensScreen && (p.ClosesScreen == true) == closesScreen ? p
            : p with { OpensScreen = opensScreen, ClosesScreen = closesScreen ? true : null });
    }

    /// <summary>
    /// Puts a picture in an Image, from a PNG, JPEG, GIF or BMP file's bytes, or removes it
    /// (null). The picture is stored in the project.
    /// </summary>
    public string? SetImage(Guid id, byte[]? data)
    {
        if (data is not null && ImageFile.Validate(data) is { } error)
        {
            return error;
        }

        var encoded = data is null ? null : Convert.ToBase64String(data);
        return EditProperties(id, d => d.HasImage, "a picture", p => p.ImageData == encoded ? p : p with { ImageData = encoded });
    }

    public string? SetImageStretch(Guid id, ImageStretch stretch) =>
        EditProperties(id, d => d.HasImage, "a picture", p => p.Stretch == stretch ? p : p with { Stretch = stretch });

    /// <summary>Sets a control's text size (null for the default) and weight.</summary>
    public string? SetFont(Guid id, int? size, bool isBold)
    {
        if (size is < ControlDefinition.MinFontSize or > ControlDefinition.MaxFontSize)
        {
            return $"Text size must be between {ControlDefinition.MinFontSize} and {ControlDefinition.MaxFontSize}.";
        }

        return EditProperties(id, d => d.HasFont, "a font", p =>
            p.FontSize == size && (p.IsBold == true) == isBold ? p : p with { FontSize = size, IsBold = isBold ? true : null });
    }

    /// <summary>
    /// Sets a control's text and background colours as "#RRGGBB" (or "RRGGBB"); blank for the
    /// default. Only controls that show text have a text colour.
    /// </summary>
    public string? SetColors(Guid id, string? foreground, string? background)
    {
        if (FindControl(id) is not { } control)
        {
            return "The control no longer exists.";
        }

        if (!ControlColor.TryParse(foreground, out var text) || !ControlColor.TryParse(background, out var fill))
        {
            return "Enter colours as #RRGGBB, for example #1E6FD9, or leave them blank.";
        }

        if (text is not null && !ControlCatalog.Get(control.Type).HasFont)
        {
            return $"A {control.Type} does not have a text colour.";
        }

        if (fill is not null && !ControlCatalog.Get(control.Type).HasBackground)
        {
            return $"A {control.Type} does not have a background colour.";
        }

        var properties = control.Properties;
        if (properties.Foreground != text || properties.Background != fill)
        {
            Replace(control, control with { Properties = properties with { Foreground = text, Background = fill } });
        }

        return null;
    }

    /// <summary>Makes a TextBox hold several wrapping lines of text, or one line.</summary>
    public string? SetIsMultiline(Guid id, bool isMultiline) =>
        EditProperties(id, d => d.HasMultiline, "a multi-line setting", p =>
            (p.IsMultiline == true) == isMultiline ? p : p with { IsMultiline = isMultiline ? true : null });

    /// <summary>Sets a Slider's or ProgressBar's minimum, maximum and value together.</summary>
    public string? SetRange(Guid id, int minimum, int maximum, int value)
    {
        if (ValidateRange(minimum, maximum, value) is { } error)
        {
            return error;
        }

        return EditProperties(id, d => d.HasRange, "a range", p =>
            p.Minimum == minimum && p.Maximum == maximum && p.Value == value ? p : p with { Minimum = minimum, Maximum = maximum, Value = value });
    }

    /// <summary>Checks a Slider's or ProgressBar's minimum, maximum and value; returns an error message or null.</summary>
    public static string? ValidateRange(int minimum, int maximum, int value)
    {
        const int Limit = ControlDefinition.MaxRangeValue;
        if (Math.Abs((long)minimum) > Limit || Math.Abs((long)maximum) > Limit)
        {
            return $"Minimum and maximum must be between {-Limit} and {Limit}.";
        }

        if (minimum >= maximum)
        {
            return "Maximum must be greater than minimum.";
        }

        if (value < minimum || value > maximum)
        {
            return $"Value must be between {minimum} and {maximum}.";
        }

        return null;
    }

    /// <summary>Replaces a ComboBox's or ListBox's items. Blank entries are dropped; order is kept.</summary>
    public string? SetItems(Guid id, IEnumerable<string> items)
    {
        var cleaned = items.Select(i => i.Trim()).Where(i => i.Length > 0).ToImmutableList();
        return EditProperties(id, d => d.HasItems, "items", p =>
            p.Items is { } current && current.SequenceEqual(cleaned) ? p : p with { Items = cleaned });
    }

    public void Undo()
    {
        if (undoStack.TryPop(out var entry))
        {
            redoStack.Push(entry with { Document = Document });
            Document = entry.Document;
            activeScreenId = entry.ScreenBefore;
            OnChanged();
        }
    }

    public void Redo()
    {
        if (redoStack.TryPop(out var entry))
        {
            undoStack.Push(entry with { Document = Document });
            Document = entry.Document;
            activeScreenId = entry.ScreenAfter;
            OnChanged();
        }
    }

    /// <summary>Shows another screen for editing. Returns false if there is no such screen.</summary>
    public bool SelectScreen(string id)
    {
        if (Document.FindScreen(id) is null)
        {
            return false;
        }

        if (id != Screen.Id)
        {
            activeScreenId = id;
            OnChanged();
        }

        return true;
    }

    /// <summary>
    /// Adds an empty screen after the current one, the same size as it, and shows it. Its name
    /// is the lowest free one of Screen2, Screen3, and so on.
    /// </summary>
    public ScreenDocument AddScreen()
    {
        var current = Screen;
        var screen = new ScreenDocument
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = NextScreenName("Screen2"),
            Width = current.Width,
            Height = current.Height,
            GridSize = current.GridSize,
        };
        InsertScreen(screen);
        return screen;
    }

    /// <summary>
    /// Copies the current screen, with new control IDs, after it and shows the copy. The copy is
    /// named after the original with the lowest free number: Main becomes Main2.
    /// </summary>
    public ScreenDocument DuplicateScreen()
    {
        var current = Screen;
        var controls = current.Controls.ConvertAll(ControlTree.WithNewIds);

        // The copy's tab order names the copied controls.
        var newIds = ControlTree.All(current.Controls).Zip(ControlTree.All(controls)).ToDictionary(p => p.First.Id, p => p.Second.Id);
        var copy = current with
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = NextScreenName(current.Name),
            Controls = controls,
            TabOrder = current.TabOrder?.Where(newIds.ContainsKey).Select(id => newIds[id]).ToImmutableList(),
        };
        InsertScreen(copy);
        return copy;
    }

    /// <summary>Renames the current screen. The name must be an identifier not used by another screen.</summary>
    public string? RenameScreen(string name)
    {
        var screen = Screen;
        if (DocumentValidator.ValidateScreenName(Document, screen.Id, name) is { } error)
        {
            return error;
        }

        if (screen.Name != name)
        {
            Commit(Document.WithScreen(screen with { Name = name }));
        }

        return null;
    }

    /// <summary>Deletes the current screen and shows the one before it. The last screen cannot be deleted.</summary>
    public string? DeleteScreen()
    {
        if (Document.Screens.Count == 1)
        {
            return "A project needs at least one screen.";
        }

        // Buttons that opened the deleted screen no longer open anything.
        var deleted = Screen.Id;
        static ControlDocument Unlink(ControlDocument control, string screenId) => control with
        {
            Properties = control.Properties.OpensScreen == screenId ? control.Properties with { OpensScreen = null } : control.Properties,
            Children = control.Children?.ConvertAll(child => Unlink(child, screenId)),
        };

        var index = Document.Screens.FindIndex(s => s.Id == deleted);
        var screens = Document.Screens.RemoveAt(index)
            .ConvertAll(s => s with { Controls = s.Controls.ConvertAll(c => Unlink(c, deleted)) });
        Commit(Document with { Screens = screens }, screens[Math.Max(0, index - 1)].Id);
        return null;
    }

    /// <summary>
    /// Moves the current screen earlier (negative) or later (positive) in the screen order.
    /// The first screen is the one a generated application opens with.
    /// </summary>
    public bool MoveScreen(int delta)
    {
        var screen = Screen;
        var index = Document.Screens.FindIndex(s => s.Id == screen.Id);
        var target = Math.Clamp(index + delta, 0, Document.Screens.Count - 1);
        if (target == index)
        {
            return false;
        }

        Commit(Document with { Screens = Document.Screens.RemoveAt(index).Insert(target, screen) });
        return true;
    }

    private void InsertScreen(ScreenDocument screen)
    {
        var index = Document.Screens.FindIndex(s => s.Id == Screen.Id);
        Commit(Document with { Screens = Document.Screens.Insert(index + 1, screen) }, screen.Id);
    }

    private string NextScreenName(string name) =>
        UniqueName(name, new HashSet<string>(Document.Screens.Select(s => s.Name), StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Moves several controls by the same offset as one step. The offset is reduced if needed
    /// so every control stays inside the screen. Returns false if nothing moved.
    /// </summary>
    public bool MoveControls(IReadOnlyCollection<Guid> ids, int dx, int dy)
    {
        var screen = Screen;
        var moving = screen.Controls.Where(c => ids.Contains(c.Id)).ToList();
        if (moving.Count == 0)
        {
            return false;
        }

        dx = Math.Clamp(dx, -moving.Min(c => c.X), screen.Width - moving.Max(c => c.X + c.Width));
        dy = Math.Clamp(dy, -moving.Min(c => c.Y), screen.Height - moving.Max(c => c.Y + c.Height));
        if (dx == 0 && dy == 0)
        {
            return false;
        }

        var controls = screen.Controls.ConvertAll(c => ids.Contains(c.Id) ? c with { X = c.X + dx, Y = c.Y + dy } : c);
        Commit(Document.WithScreen(screen with { Controls = controls }));
        return true;
    }

    /// <summary>Removes several controls as one step. Returns the number removed.</summary>
    public int DeleteControls(IReadOnlyCollection<Guid> ids)
    {
        var screen = Screen;
        var removed = ControlTree.All(screen.Controls).Count(c => ids.Contains(c.Id));
        if (removed > 0)
        {
            Commit(Document.WithScreen(screen with { Controls = ControlTree.KeepShownTabs(ControlTree.Remove(screen.Controls, ids)) }));
        }

        return removed;
    }

    /// <summary>
    /// Adds copies of controls as one step, on top of everything else, shifted by an offset
    /// and kept inside the screen. Copies get new IDs, and new names where the original name
    /// is taken. Returns the copies.
    /// </summary>
    public IReadOnlyList<ControlDocument> PasteControls(IReadOnlyList<ControlDocument> copies, int offset)
    {
        var screen = Screen;
        if (copies.Count == 0)
        {
            return [];
        }

        var dx = Math.Clamp(offset, -copies.Min(c => c.X), screen.Width - copies.Max(c => c.X + c.Width));
        var dy = Math.Clamp(offset, -copies.Min(c => c.Y), screen.Height - copies.Max(c => c.Y + c.Height));
        var taken = new HashSet<string>(ControlTree.All(screen.Controls).Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        var pasted = new List<ControlDocument>();
        foreach (var copy in copies)
        {
            // Skip anything that cannot fit (a copy from a larger screen).
            // A tab page cannot stand on its own on the screen.
            if (!ControlCatalog.TryGet(copy.Type, out var definition) || !definition.InToolbox
                || copy.Width > screen.Width || copy.Height > screen.Height)
            {
                continue;
            }

            pasted.Add(Renamed(ControlTree.WithNewIds(copy), taken) with
            {
                X = Math.Clamp(copy.X + dx, 0, screen.Width - copy.Width),
                Y = Math.Clamp(copy.Y + dy, 0, screen.Height - copy.Height),
                Row = null,
                Column = null,
                RowSpan = null,
                ColumnSpan = null,
            });
        }

        if (pasted.Count > 0)
        {
            Commit(Document.WithScreen(screen with { Controls = screen.Controls.AddRange(pasted) }));
        }

        return pasted;
    }

    /// <summary>Moves controls to the top of the draw order, keeping their order among themselves.</summary>
    public bool BringToFront(IReadOnlyCollection<Guid> ids) => Reorder(ids, toFront: true);

    /// <summary>Moves controls to the bottom of the draw order, keeping their order among themselves.</summary>
    public bool SendToBack(IReadOnlyCollection<Guid> ids) => Reorder(ids, toFront: false);

    /// <summary>Changes the screen's design size. Every control must still fit.</summary>
    public string? SetScreenSize(int width, int height)
    {
        const int Minimum = 100;
        const int Maximum = 10000;
        if (width < Minimum || height < Minimum)
        {
            return $"The screen must be at least {Minimum} × {Minimum}.";
        }

        if (width > Maximum || height > Maximum)
        {
            return $"The screen can be at most {Maximum} × {Maximum}.";
        }

        var screen = Screen;
        if (screen.Controls.FirstOrDefault(c => c.X + c.Width > width || c.Y + c.Height > height) is { } outside)
        {
            return $"\"{outside.Name}\" would be outside the screen. Move or resize it first.";
        }

        if (screen.Width != width || screen.Height != height)
        {
            Commit(Document.WithScreen(screen with { Width = width, Height = height }));
        }

        return null;
    }

    private bool Reorder(IReadOnlyCollection<Guid> ids, bool toFront)
    {
        var screen = Screen;
        var chosen = screen.Controls.Where(c => ids.Contains(c.Id)).ToList();
        var others = screen.Controls.Where(c => !ids.Contains(c.Id)).ToList();
        var reordered = (toFront ? others.Concat(chosen) : chosen.Concat(others)).ToImmutableList();
        if (chosen.Count == 0 || reordered.SequenceEqual(screen.Controls))
        {
            return false;
        }

        Commit(Document.WithScreen(screen with { Controls = reordered }));
        return true;
    }

    /// <summary>Gives a pasted control and everything inside it names not already taken.</summary>
    private static ControlDocument Renamed(ControlDocument control, HashSet<string> taken)
    {
        var name = UniqueName(control.Name, taken);
        taken.Add(name);
        return control with { Name = name, Children = control.Children?.ConvertAll(child => Renamed(child, taken)) };
    }

    /// <summary>
    /// A new control with the lowest free default name. A new TabControl comes with two pages,
    /// "Tab 1" and "Tab 2".
    /// </summary>
    private static ControlDocument NewControl(ScreenDocument screen, ControlType type)
    {
        var control = NewControl(type, NextDefaultName(screen, type));
        if (type == ControlType.TabControl)
        {
            var withTabs = screen with { Controls = screen.Controls.Add(control) };
            for (var i = 1; i <= 2; i++)
            {
                control = control with { Children = control.Children!.Add(NewPage(withTabs, i)) };
                withTabs = screen with { Controls = screen.Controls.Add(control) };
            }
        }

        return control;
    }

    /// <summary>A new, empty tab page whose tab says "Tab n".</summary>
    private static ControlDocument NewPage(ScreenDocument screen, int number)
    {
        var page = NewControl(ControlType.TabPage, NextDefaultName(screen, ControlType.TabPage));
        return page with { Properties = page.Properties with { Text = $"Tab {number}" } };
    }

    private static ControlDocument NewControl(ControlType type, string name)
    {
        var definition = ControlCatalog.Get(type);
        return new ControlDocument
        {
            Id = Guid.NewGuid(),
            Type = type,
            Name = name,
            Properties = definition.CreateDefaultProperties(name),
            Children = definition.IsContainer ? [] : null,
        }.WithBounds(new ControlBounds(0, 0, definition.DefaultWidth, definition.DefaultHeight));
    }

    private PlacedControl? Placed(Guid containerId) =>
        ContainerLayout.Flatten(Screen).FirstOrDefault(p => p.Control.Id == containerId && p.Control.Children is not null);

    /// <summary>Where a control dropped at a screen point goes in a container.</summary>
    private static (int Index, int? Row, int? Column) DropPosition(PlacedControl container, double x, double y, Guid? ignore)
    {
        if (container.Control.Type == ControlType.Grid)
        {
            var (row, column) = ContainerLayout.GridCellAt(container, x, y);
            return (container.Control.Children?.Count ?? 0, row, column);
        }

        if (container.Control.Type == ControlType.TabControl)
        {
            // A page dropped on another TabControl becomes its last tab; on its own, it stays put.
            var children = container.Control.Children ?? [];
            var current = children.FindIndex(c => c.Id == ignore);
            return (current >= 0 ? current : children.Count, null, null);
        }

        return (ContainerLayout.StackIndexAt(container, x, y, ignore), null, null);
    }

    /// <summary>
    /// The name itself if free; otherwise the name without trailing digits plus the lowest free
    /// number from 2 up: Button1 becomes Button2, SubmitButton becomes SubmitButton2.
    /// </summary>
    private static string UniqueName(string name, HashSet<string> taken)
    {
        if (!taken.Contains(name))
        {
            return name;
        }

        var stem = name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        if (stem.Length == 0)
        {
            stem = "Control";
        }

        for (var n = 2; ; n++)
        {
            var candidate = stem + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>Returns the lowest free default name for a type: Button1, Button2, and so on.</summary>
    public static string NextDefaultName(ScreenDocument screen, ControlType type)
    {
        var taken = new HashSet<string>(ControlTree.All(screen.Controls).Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        for (var n = 1; ; n++)
        {
            var candidate = $"{type}{n}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>Records that the current document has been saved.</summary>
    public void MarkSaved()
    {
        savedDocument = Document;
        OnChanged();
    }

    private string? EditProperties(Guid id, Func<ControlDefinition, bool> supports, string what, Func<ControlProperties, ControlProperties> edit)
    {
        if (FindControl(id) is not { } control)
        {
            return "The control no longer exists.";
        }

        if (!supports(ControlCatalog.Get(control.Type)))
        {
            return $"A {control.Type} does not have {what}.";
        }

        var properties = edit(control.Properties);
        if (!ReferenceEquals(properties, control.Properties))
        {
            Replace(control, control with { Properties = properties });
        }

        return null;
    }

    private void Replace(ControlDocument current, ControlDocument replacement)
    {
        var screen = Screen;
        Commit(Document.WithScreen(screen with { Controls = ControlTree.Replace(screen.Controls, current.Id, _ => replacement) }));
    }

    /// <param name="screenId">The screen to show afterwards; by default the current one.</param>
    private void Commit(ProjectDocument next, string? screenId = null)
    {
        var before = Screen.Id;
        var after = screenId ?? before;
        undoStack.Push(new HistoryEntry(Document, before, after));
        redoStack.Clear();

        // Removed controls leave the tab order.
        Document = next with { Screens = next.Screens.ConvertAll(TabSequence.Tidy) };
        activeScreenId = after;
        OnChanged();
    }

    private sealed record HistoryEntry(ProjectDocument Document, string ScreenBefore, string ScreenAfter);

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
