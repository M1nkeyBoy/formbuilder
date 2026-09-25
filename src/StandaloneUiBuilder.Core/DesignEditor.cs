using System.Collections.Immutable;

namespace StandaloneUiBuilder.Core;

/// <summary>
/// Owns the document being edited, its undo history and whether it differs from the last save.
/// The UI reads <see cref="Document"/> and renders from it; it never edits the document directly.
/// Each successful edit method is one undoable step. Edits that change nothing record nothing.
/// Methods that can reject a value return a plain-language error message, or null on success.
/// </summary>
public sealed class DesignEditor
{
    private readonly Stack<ProjectDocument> undoStack = new();
    private readonly Stack<ProjectDocument> redoStack = new();
    private ProjectDocument? savedDocument;

    public DesignEditor()
        : this(ProjectDocument.CreateBlank())
    {
    }

    public DesignEditor(ProjectDocument document)
    {
        Document = document;
        savedDocument = document;
    }

    /// <summary>Raised after any change to <see cref="Document"/> or <see cref="IsDirty"/>.</summary>
    public event EventHandler? Changed;

    public ProjectDocument Document { get; private set; }

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
        OnChanged();
    }

    /// <summary>Finds a control anywhere, including inside containers.</summary>
    public ControlDocument? FindControl(Guid id) => ControlTree.Find(Document.Screen.Controls, id);

    /// <summary>The container a control is in, or null if it is directly on the screen.</summary>
    public ControlDocument? ParentOf(Guid id) => ControlTree.ParentOf(Document.Screen.Controls, id);

    /// <summary>
    /// Adds a control of the given type with its top-left corner at a point, snapped to the
    /// grid and kept inside the screen. The new control is drawn on top of existing ones.
    /// </summary>
    public ControlDocument AddControl(ControlType type, double x, double y)
    {
        var definition = ControlCatalog.Get(type);
        var screen = Document.Screen;
        var name = NextDefaultName(screen, type);

        var control = NewControl(type, name).WithBounds(DesignGeometry.Place(screen, definition, x, y));

        Commit(Document with { Screen = screen with { Controls = screen.Controls.Add(control) } });
        return control;
    }

    /// <summary>
    /// Adds a new control inside a container, where a screen point falls: at that position in
    /// a StackPanel, or in that cell of a Grid. Returns null if the container does not exist.
    /// </summary>
    public ControlDocument? AddControlTo(ControlType type, Guid containerId, double x, double y)
    {
        var screen = Document.Screen;
        if (Placed(containerId) is not { } container)
        {
            return null;
        }

        var control = NewControl(type, NextDefaultName(screen, type));
        var (index, row, column) = DropPosition(container, x, y, ignore: null);
        control = control with { Row = row, Column = column };
        Commit(Document with { Screen = screen with { Controls = ControlTree.Insert(screen.Controls, containerId, index, control) } });
        return control;
    }

    /// <summary>
    /// Moves a control (and anything inside it) into a container, where a screen point falls.
    /// A control cannot be moved into itself or into a container inside it.
    /// </summary>
    public string? MoveIntoContainer(Guid id, Guid containerId, double x, double y)
    {
        var screen = Document.Screen;
        if (FindControl(id) is not { } control || Placed(containerId) is not { } container)
        {
            return "The control no longer exists.";
        }

        if (ControlTree.IsSelfOrDescendant(screen.Controls, id, containerId))
        {
            return "A container cannot go inside itself.";
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
        if (!ControlTree.All(controls).SequenceEqual(ControlTree.All(screen.Controls)))
        {
            Commit(Document with { Screen = screen with { Controls = controls } });
        }

        return null;
    }

    /// <summary>
    /// Takes a control out of its container and puts it on the screen with its top-left at a
    /// point, snapped to the grid, keeping its designed size.
    /// </summary>
    public string? MoveToScreen(Guid id, double x, double y)
    {
        var screen = Document.Screen;
        if (FindControl(id) is not { } control)
        {
            return "The control no longer exists.";
        }

        if (ControlTree.IsRoot(screen.Controls, id))
        {
            return null;
        }

        var size = new ControlDefinition(control.Type, Math.Min(control.Width, screen.Width), Math.Min(control.Height, screen.Height), 0, 0, false, false, false);
        var moved = control.WithBounds(DesignGeometry.Place(screen, size, x, y)) with { Row = null, Column = null, RowSpan = null, ColumnSpan = null };
        var controls = ControlTree.Remove(screen.Controls, [id]).Add(moved);
        Commit(Document with { Screen = screen with { Controls = controls } });
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
        Replace(parent, parent with { Children = reordered });
        return true;
    }

    /// <summary>
    /// Sets the size of a control inside a StackPanel along the stack's direction: its height
    /// in a vertical stack, its width in a horizontal one.
    /// </summary>
    public string? SetStackSize(Guid id, int size)
    {
        if (FindControl(id) is not { } control || ParentOf(id) is not { Type: ControlType.StackPanel } stack)
        {
            return "The control is not in a StackPanel.";
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

        return EditProperties(id, d => d.IsGrid, "rows and columns", p =>
            p.Rows == rows && p.Columns == columns ? p : p with { Rows = rows, Columns = columns });
    }

    /// <summary>Removes a control (and anything inside it). Returns false if no control has that ID.</summary>
    public bool DeleteControl(Guid id) => DeleteControls([id]) > 0;

    /// <summary>Moves and/or resizes a control to exact bounds.</summary>
    public string? SetBounds(Guid id, ControlBounds bounds)
    {
        if (FindControl(id) is not { } control)
        {
            return "The control no longer exists.";
        }

        if (!ControlTree.IsRoot(Document.Screen.Controls, id))
        {
            return "Its container decides where this control goes.";
        }

        if (DocumentValidator.ValidateBounds(Document.Screen, control.Type, bounds) is { } error)
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
        if (DocumentValidator.ValidateName(Document.Screen, id, name) is { } error)
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

    public string? SetIsChecked(Guid id, bool isChecked) =>
        EditProperties(id, d => d.HasIsChecked, "a checked state", p => p.IsChecked == isChecked ? p : p with { IsChecked = isChecked });

    /// <summary>Replaces a ComboBox's items. Blank entries are dropped; order is kept.</summary>
    public string? SetItems(Guid id, IEnumerable<string> items)
    {
        var cleaned = items.Select(i => i.Trim()).Where(i => i.Length > 0).ToImmutableList();
        return EditProperties(id, d => d.HasItems, "items", p =>
            p.Items is { } current && current.SequenceEqual(cleaned) ? p : p with { Items = cleaned });
    }

    public void Undo()
    {
        if (undoStack.TryPop(out var previous))
        {
            redoStack.Push(Document);
            Document = previous;
            OnChanged();
        }
    }

    public void Redo()
    {
        if (redoStack.TryPop(out var next))
        {
            undoStack.Push(Document);
            Document = next;
            OnChanged();
        }
    }

    /// <summary>
    /// Moves several controls by the same offset as one step. The offset is reduced if needed
    /// so every control stays inside the screen. Returns false if nothing moved.
    /// </summary>
    public bool MoveControls(IReadOnlyCollection<Guid> ids, int dx, int dy)
    {
        var screen = Document.Screen;
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
        Commit(Document with { Screen = screen with { Controls = controls } });
        return true;
    }

    /// <summary>Removes several controls as one step. Returns the number removed.</summary>
    public int DeleteControls(IReadOnlyCollection<Guid> ids)
    {
        var screen = Document.Screen;
        var removed = ControlTree.All(screen.Controls).Count(c => ids.Contains(c.Id));
        if (removed > 0)
        {
            Commit(Document with { Screen = screen with { Controls = ControlTree.Remove(screen.Controls, ids) } });
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
        var screen = Document.Screen;
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
            if (!ControlCatalog.TryGet(copy.Type, out _) || copy.Width > screen.Width || copy.Height > screen.Height)
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
            Commit(Document with { Screen = screen with { Controls = screen.Controls.AddRange(pasted) } });
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

        var screen = Document.Screen;
        if (screen.Controls.FirstOrDefault(c => c.X + c.Width > width || c.Y + c.Height > height) is { } outside)
        {
            return $"\"{outside.Name}\" would be outside the screen. Move or resize it first.";
        }

        if (screen.Width != width || screen.Height != height)
        {
            Commit(Document with { Screen = screen with { Width = width, Height = height } });
        }

        return null;
    }

    private bool Reorder(IReadOnlyCollection<Guid> ids, bool toFront)
    {
        var screen = Document.Screen;
        var chosen = screen.Controls.Where(c => ids.Contains(c.Id)).ToList();
        var others = screen.Controls.Where(c => !ids.Contains(c.Id)).ToList();
        var reordered = (toFront ? others.Concat(chosen) : chosen.Concat(others)).ToImmutableList();
        if (chosen.Count == 0 || reordered.SequenceEqual(screen.Controls))
        {
            return false;
        }

        Commit(Document with { Screen = screen with { Controls = reordered } });
        return true;
    }

    /// <summary>Gives a pasted control and everything inside it names not already taken.</summary>
    private static ControlDocument Renamed(ControlDocument control, HashSet<string> taken)
    {
        var name = UniqueName(control.Name, taken);
        taken.Add(name);
        return control with { Name = name, Children = control.Children?.ConvertAll(child => Renamed(child, taken)) };
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
        ContainerLayout.Flatten(Document.Screen).FirstOrDefault(p => p.Control.Id == containerId && p.Control.Children is not null);

    /// <summary>Where a control dropped at a screen point goes in a container.</summary>
    private static (int Index, int? Row, int? Column) DropPosition(PlacedControl container, double x, double y, Guid? ignore)
    {
        if (container.Control.Type == ControlType.Grid)
        {
            var (row, column) = ContainerLayout.GridCellAt(container, x, y);
            return (container.Control.Children?.Count ?? 0, row, column);
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
        var screen = Document.Screen;
        Commit(Document with { Screen = screen with { Controls = ControlTree.Replace(screen.Controls, current.Id, _ => replacement) } });
    }

    private void Commit(ProjectDocument next)
    {
        undoStack.Push(Document);
        redoStack.Clear();
        Document = next;
        OnChanged();
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
