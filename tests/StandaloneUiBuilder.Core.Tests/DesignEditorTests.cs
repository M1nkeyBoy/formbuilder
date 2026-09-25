using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Core.Tests;

public class DesignEditorTests
{
    [Fact]
    public void NewEditorIsClean()
    {
        var editor = new DesignEditor();

        Assert.False(editor.IsDirty);
        Assert.Empty(editor.Screen.Controls);
    }

    [Fact]
    public void NewReplacesTheDocumentAndRaisesChanged()
    {
        var editor = new DesignEditor();
        var original = editor.Document;
        var raised = 0;
        editor.Changed += (_, _) => raised++;

        editor.New();

        Assert.NotEqual(original.ProjectId, editor.Document.ProjectId);
        Assert.False(editor.IsDirty);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void ResetCanMarkADocumentDirty()
    {
        var editor = new DesignEditor();

        editor.Reset(ProjectDocument.CreateBlank(), isDirty: true);
        Assert.True(editor.IsDirty);

        editor.MarkSaved();
        Assert.False(editor.IsDirty);
    }
}
