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

    public ControlDocument? FindControl(Guid id) => Document.Screen.Controls.Find(c => c.Id == id);

    /// <summary>
    /// Adds a control of the given type with its top-left corner at a point, snapped to the
    /// grid and kept inside the screen. The new control is drawn on top of existing ones.
    /// </summary>
    public ControlDocument AddControl(ControlType type, double x, double y)
    {
        var definition = ControlCatalog.Get(type);
        var screen = Document.Screen;
        var name = NextDefaultName(screen, type);

        var control = new ControlDocument
        {
            Id = Guid.NewGuid(),
            Type = type,
            Name = name,
            Properties = definition.CreateDefaultProperties(name),
        }.WithBounds(DesignGeometry.Place(screen, definition, x, y));

        Commit(Document with { Screen = screen with { Controls = screen.Controls.Add(control) } });
        return control;
    }

    /// <summary>Removes a control. Returns false if no control has that ID.</summary>
    public bool DeleteControl(Guid id)
    {
        var screen = Document.Screen;
        var index = screen.Controls.FindIndex(c => c.Id == id);
        if (index < 0)
        {
            return false;
        }

        Commit(Document with { Screen = screen with { Controls = screen.Controls.RemoveAt(index) } });
        return true;
    }

    /// <summary>Moves and/or resizes a control to exact bounds.</summary>
    public string? SetBounds(Guid id, ControlBounds bounds)
    {
        if (FindControl(id) is not { } control)
        {
            return "The control no longer exists.";
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
        var remaining = screen.Controls.RemoveAll(c => ids.Contains(c.Id));
        var removed = screen.Controls.Count - remaining.Count;
        if (removed > 0)
        {
            Commit(Document with { Screen = screen with { Controls = remaining } });
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
        var taken = new HashSet<string>(screen.Controls.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        var pasted = new List<ControlDocument>();
        foreach (var copy in copies)
        {
            // Skip anything that cannot fit (a copy from a larger screen).
            if (!ControlCatalog.TryGet(copy.Type, out _) || copy.Width > screen.Width || copy.Height > screen.Height)
            {
                continue;
            }

            var name = UniqueName(copy.Name, taken);
            taken.Add(name);
            pasted.Add(copy with
            {
                Id = Guid.NewGuid(),
                Name = name,
                X = Math.Clamp(copy.X + dx, 0, screen.Width - copy.Width),
                Y = Math.Clamp(copy.Y + dy, 0, screen.Height - copy.Height),
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
        var taken = new HashSet<string>(screen.Controls.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
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
        Commit(Document with { Screen = screen with { Controls = screen.Controls.Replace(current, replacement) } });
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
