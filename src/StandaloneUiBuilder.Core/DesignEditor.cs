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
