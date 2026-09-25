using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Output.WinForms;
using StandaloneUiBuilder.Output.Wpf;

namespace StandaloneUiBuilder.Core.Tests;

public class ScreenTests
{
    [Fact]
    public void ANewProjectHasOneScreenBeingEdited()
    {
        var editor = new DesignEditor();

        var screen = Assert.Single(editor.Document.Screens);
        Assert.Same(screen, editor.Screen);
        Assert.Equal(("main", "Main"), (screen.Id, screen.Name));
    }

    [Fact]
    public void AddScreenShowsANewEmptyScreenAfterTheCurrentOneWithTheSameSize()
    {
        var editor = new DesignEditor();
        editor.SetScreenSize(1024, 768);
        editor.AddControl(ControlType.Button, 10, 10);

        var added = editor.AddScreen();

        Assert.Equal(["Main", "Screen2"], editor.Document.Screens.Select(s => s.Name));
        Assert.Same(added, editor.Screen);
        Assert.Empty(added.Controls);
        Assert.Equal((1024, 768), (added.Width, added.Height));
        Assert.NotEqual("main", added.Id);
    }

    [Fact]
    public void EditsApplyToTheScreenBeingShown()
    {
        var editor = new DesignEditor();
        editor.AddControl(ControlType.Button, 10, 10);
        editor.AddScreen();

        var label = editor.AddControl(ControlType.Label, 10, 10);

        Assert.Equal("Label1", label.Name);
        Assert.Equal(["Button1"], editor.Document.MainScreen.Controls.Select(c => c.Name));
        Assert.Equal(["Label1"], editor.Screen.Controls.Select(c => c.Name));
        Assert.Null(editor.FindControl(editor.Document.MainScreen.Controls[0].Id));
    }

    [Fact]
    public void ControlNamesOnlyNeedToBeUniqueWithinTheirScreen()
    {
        var editor = new DesignEditor();
        editor.AddControl(ControlType.Button, 10, 10);
        editor.AddScreen();

        var button = editor.AddControl(ControlType.Button, 10, 10);

        Assert.Equal("Button1", button.Name);
        Assert.Empty(DocumentValidator.Validate(editor.Document));
    }

    [Fact]
    public void SwitchingScreensIsNotAnEdit()
    {
        var editor = new DesignEditor();
        editor.AddScreen();
        editor.MarkSaved();
        var undoable = editor.CanUndo;

        Assert.True(editor.SelectScreen("main"));

        Assert.Equal("main", editor.Screen.Id);
        Assert.False(editor.IsDirty);
        Assert.Equal(undoable, editor.CanUndo);
        Assert.False(editor.SelectScreen("missing"));
    }

    [Fact]
    public void UndoAndRedoReturnToTheScreenTheChangeWasOn()
    {
        var editor = new DesignEditor();
        var second = editor.AddScreen();
        editor.AddControl(ControlType.Button, 10, 10);
        editor.SelectScreen("main");

        editor.Undo();

        Assert.Equal(second.Id, editor.Screen.Id);
        Assert.Empty(editor.Screen.Controls);

        editor.SelectScreen("main");
        editor.Redo();

        Assert.Equal(second.Id, editor.Screen.Id);
        Assert.Single(editor.Screen.Controls);

        editor.Undo();
        editor.Undo();

        Assert.Single(editor.Document.Screens);
        Assert.Equal("main", editor.Screen.Id);
    }

    [Fact]
    public void DuplicateScreenCopiesControlsWithNewIds()
    {
        var editor = new DesignEditor();
        var grid = editor.AddControl(ControlType.Grid, 10, 10);
        editor.AddControlTo(ControlType.Button, grid.Id, 20, 20);

        var copy = editor.DuplicateScreen();

        Assert.Equal(["Main", "Main2"], editor.Document.Screens.Select(s => s.Name));
        Assert.Equal(copy.Id, editor.Screen.Id);
        var originals = ControlTree.All(editor.Document.MainScreen.Controls).ToList();
        var copies = ControlTree.All(copy.Controls).ToList();
        Assert.Equal(originals.Select(c => c.Name), copies.Select(c => c.Name));
        Assert.Empty(originals.Select(c => c.Id).Intersect(copies.Select(c => c.Id)));
        Assert.Empty(DocumentValidator.Validate(editor.Document));
    }

    [Theory]
    [InlineData("", "cannot be empty")]
    [InlineData("Order form", "letters, digits and underscores")]
    [InlineData("main", "already named \"Main\"")]
    public void ScreenNamesMustBeUniqueIdentifiers(string name, string expected)
    {
        var editor = new DesignEditor();
        editor.AddScreen();

        var error = editor.RenameScreen(name);

        Assert.Contains(expected, error);
        Assert.Equal("Screen2", editor.Screen.Name);
    }

    [Fact]
    public void RenameScreenIsOneUndoableStep()
    {
        var editor = new DesignEditor();

        Assert.Null(editor.RenameScreen("Customer"));
        Assert.Equal("Customer", editor.Screen.Name);

        editor.Undo();

        Assert.Equal("Main", editor.Screen.Name);
    }

    [Fact]
    public void DeleteScreenShowsThePreviousScreenAndKeepsTheLastOne()
    {
        var editor = new DesignEditor();
        Assert.Equal("A project needs at least one screen.", editor.DeleteScreen());

        editor.AddScreen();
        var third = editor.AddScreen();
        editor.SelectScreen(editor.Document.Screens[1].Id);

        Assert.Null(editor.DeleteScreen());

        Assert.Equal(["Main", third.Name], editor.Document.Screens.Select(s => s.Name));
        Assert.Equal("main", editor.Screen.Id);
    }

    [Fact]
    public void MoveScreenChangesTheOrderAndStopsAtTheEnds()
    {
        var editor = new DesignEditor();
        var second = editor.AddScreen();

        Assert.False(editor.MoveScreen(1));
        Assert.True(editor.MoveScreen(-1));

        Assert.Equal([second.Id, "main"], editor.Document.Screens.Select(s => s.Id));
        Assert.Same(editor.Document.MainScreen, editor.Screen);
    }

    [Fact]
    public void SeveralScreensSurviveSaveAndOpen()
    {
        var editor = new DesignEditor();
        editor.AddControl(ControlType.Button, 10, 10);
        editor.AddScreen();
        editor.RenameScreen("Settings");
        editor.AddControl(ControlType.CheckBox, 20, 20);

        var reloaded = ProjectFile.Deserialize(ProjectFile.Serialize(editor.Document));

        Assert.Equal(["Main", "Settings"], reloaded.Screens.Select(s => s.Name));
        Assert.Equal(editor.Document.Screens.Select(s => s.Id), reloaded.Screens.Select(s => s.Id));
        Assert.Equal(ControlType.CheckBox, reloaded.Screens[1].Controls.Single().Type);
    }

    [Fact]
    public void AnOlderFileWithOneScreenOpensAsAProjectWithThatScreen()
    {
        const string json = """
            {
              "schemaVersion": 5,
              "projectId": "9e9608a0-1ab6-4dd4-8da0-592260982971",
              "name": "Old",
              "screen": { "id": "main", "name": "Main screen", "width": 640, "height": 480, "gridSize": 10, "controls": [] }
            }
            """;

        var document = ProjectFile.Deserialize(json);

        var screen = Assert.Single(document.Screens);
        Assert.Equal(("main", "Main", 640), (screen.Id, screen.Name, screen.Width));
    }

    [Fact]
    public void UnsupportedTypesInLaterScreensAndContainersAreReportedByName()
    {
        const string json = """
            {
              "schemaVersion": 6,
              "projectId": "9e9608a0-1ab6-4dd4-8da0-592260982971",
              "screens": [
                { "id": "main", "name": "Main", "controls": [] },
                { "id": "b", "name": "Other", "controls": [
                  { "id": "9e651cb8-84b8-4140-a171-62075666768e", "type": "StackPanel", "name": "Stack1", "width": 100, "height": 100,
                    "children": [ { "id": "9e651cb8-84b8-4140-a171-62075666768f", "type": "Calendar", "name": "Volume" } ] } ] }
              ]
            }
            """;

        var error = Assert.Throws<ProjectFileException>(() => ProjectFile.Deserialize(json));

        Assert.Contains("\"Calendar\" (control \"Volume\")", error.Message);
    }

    [Fact]
    public void ValidationNamesTheScreenWhenThereAreSeveral()
    {
        var editor = new DesignEditor();
        editor.AddScreen();
        var document = editor.Document.WithScreen(editor.Screen with { Name = "Main", Width = 0 });

        var errors = DocumentValidator.Validate(document);

        Assert.Contains("Screen \"Main\": The screen size 0 × 600 is not valid.", errors);
        Assert.Contains(errors, e => e.Contains("already named", StringComparison.Ordinal));
    }

    [Fact]
    public void EachScreenBecomesItsOwnWindowAndForm()
    {
        var editor = new DesignEditor();
        editor.AddControl(ControlType.Button, 10, 10);
        editor.AddScreen();
        editor.RenameScreen("Settings");
        editor.AddControl(ControlType.CheckBox, 10, 10);
        var document = editor.Document with { Name = "Demo" };

        var wpf = WpfGenerator.Generate(document, "Demo").ToDictionary(f => f.RelativePath);
        var winForms = WinFormsGenerator.Generate(document, "Demo").ToDictionary(f => f.RelativePath);

        Assert.Contains("x:Class=\"Demo.MainWindow\"", wpf["MainWindow.xaml"].Content);
        Assert.Contains("Title=\"Demo\"", wpf["MainWindow.xaml"].Content);
        Assert.Contains("x:Class=\"Demo.SettingsWindow\"", wpf["SettingsWindow.xaml"].Content);
        Assert.Contains("Title=\"Settings\"", wpf["SettingsWindow.xaml"].Content);
        Assert.Contains("CheckBox1_Click", wpf["SettingsWindow.Events.g.cs"].Content);
        Assert.DoesNotContain("CheckBox1", wpf["MainWindow.Events.g.cs"].Content);
        Assert.False(wpf["SettingsWindow.xaml.cs"].Regenerate);
        Assert.Contains("StartupUri=\"MainWindow.xaml\"", wpf["App.xaml"].Content);

        Assert.Contains("partial class SettingsForm", winForms["SettingsForm.Designer.cs"].Content);
        Assert.Contains("this.Text = \"Settings\";", winForms["SettingsForm.Designer.cs"].Content);
        Assert.Contains("public partial class SettingsForm : Form", winForms["SettingsForm.cs"].Content);
        Assert.Contains("new MainForm()", winForms["Program.cs"].Content);
    }

    [Fact]
    public void ALaterScreenCannotTakeTheFirstScreensClassName()
    {
        var editor = new DesignEditor();
        editor.RenameScreen("Start");
        editor.AddScreen();
        editor.RenameScreen("main");

        Assert.Contains(WpfGenerator.Check(editor.Document), p => p.Contains("would become mainWindow", StringComparison.Ordinal));
        Assert.Contains(WinFormsGenerator.Check(editor.Document), p => p.Contains("would become mainForm", StringComparison.Ordinal));
    }

    [Fact]
    public void AControlCannotShareItsScreensClassName()
    {
        var editor = new DesignEditor();
        editor.AddScreen();
        editor.RenameScreen("Settings");
        var button = editor.AddControl(ControlType.Button, 10, 10);
        editor.Rename(button.Id, "SettingsWindow");

        Assert.Contains("Screen \"Settings\": \"SettingsWindow\" clashes with a member of the generated window. Rename the control.", WpfGenerator.Check(editor.Document));
        Assert.Empty(WinFormsGenerator.Check(editor.Document));
    }
}
