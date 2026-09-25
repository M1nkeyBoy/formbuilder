namespace StandaloneUiBuilder.Core;

/// <summary>
/// Owns the document being edited and tracks whether it differs from the last save.
/// The UI reads <see cref="Document"/> and renders from it; it never edits the document directly.
/// </summary>
public sealed class DesignEditor
{
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

    /// <summary>True when the document has changed since it was created, opened or saved.</summary>
    public bool IsDirty => !ReferenceEquals(Document, savedDocument);

    /// <summary>Starts a new blank document.</summary>
    public void New() => Reset(ProjectDocument.CreateBlank());

    /// <summary>Replaces the document, e.g. after opening a file.</summary>
    public void Reset(ProjectDocument document, bool isDirty = false)
    {
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

    private void Commit(ProjectDocument next)
    {
        Document = next;
        OnChanged();
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
