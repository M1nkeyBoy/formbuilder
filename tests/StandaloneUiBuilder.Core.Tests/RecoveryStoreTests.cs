using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Core.Tests;

public sealed class RecoveryStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "uib-recovery-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ProjectDocument EditedDocument()
    {
        var editor = new DesignEditor();
        editor.AddControl(ControlType.Button, 40, 40);
        return editor.Document;
    }

    [Fact]
    public void DraftOfARunningSessionIsNotOffered()
    {
        var store = new RecoveryStore(directory);
        using var session = store.StartSession();

        session.WriteDraft(EditedDocument(), projectPath: null);

        Assert.Empty(new RecoveryStore(directory).FindOrphanedDrafts());
    }

    [Fact]
    public void DraftLeftByAnEndedSessionIsOfferedWithItsDocumentAndPath()
    {
        var document = EditedDocument();
        var session = new RecoveryStore(directory).StartSession();
        session.WriteDraft(document, @"C:\Projects\form.uibproj");
        session.Dispose();

        var draft = Assert.Single(new RecoveryStore(directory).FindOrphanedDrafts());

        Assert.Equal(@"C:\Projects\form.uibproj", draft.ProjectPath);
        Assert.Equal(document.ProjectId, draft.Document.ProjectId);
        Assert.Equal(document.MainScreen.Controls.Single().Id, draft.Document.MainScreen.Controls.Single().Id);
    }

    [Fact]
    public void DeletedDraftIsNotOffered()
    {
        var session = new RecoveryStore(directory).StartSession();
        session.WriteDraft(EditedDocument(), projectPath: null);
        session.DeleteDraft();
        session.Dispose();

        Assert.Empty(new RecoveryStore(directory).FindOrphanedDrafts());
    }

    [Fact]
    public void DiscardRemovesADraft()
    {
        var session = new RecoveryStore(directory).StartSession();
        session.WriteDraft(EditedDocument(), projectPath: null);
        session.Dispose();
        var store = new RecoveryStore(directory);

        RecoveryStore.Discard(store.FindOrphanedDrafts().Single().DraftPath);

        Assert.Empty(store.FindOrphanedDrafts());
    }

    [Fact]
    public void UnreadableDraftIsDeleted()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "broken.draft.json");
        File.WriteAllText(path, "{ not json");

        Assert.Empty(new RecoveryStore(directory).FindOrphanedDrafts());
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void MissingDirectoryHasNoDrafts()
    {
        Assert.Empty(new RecoveryStore(directory).FindOrphanedDrafts());
    }

    [Fact]
    public void RecoveryNeverTouchesTheProjectFile()
    {
        var projectPath = Path.Combine(directory, "project.uibproj");
        Directory.CreateDirectory(directory);
        ProjectFile.Save(ProjectDocument.CreateBlank(), projectPath);
        var saved = File.ReadAllText(projectPath);

        var session = new RecoveryStore(Path.Combine(directory, "recovery")).StartSession();
        session.WriteDraft(EditedDocument(), projectPath);
        session.Dispose();

        Assert.Equal(saved, File.ReadAllText(projectPath));
    }
}
