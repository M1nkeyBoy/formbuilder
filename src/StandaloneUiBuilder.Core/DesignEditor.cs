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

    /// <summary>Records that the current document has been saved.</summary>
    public void MarkSaved()
    {
        savedDocument = Document;
        OnChanged();
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
