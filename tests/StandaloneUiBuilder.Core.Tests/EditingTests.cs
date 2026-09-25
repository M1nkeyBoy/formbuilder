using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Core.Tests;

public class EditingTests
{
    private static readonly ScreenDocument Screen = new();
    private static readonly ControlDefinition ButtonDefinition = ControlCatalog.Get(ControlType.Button);

    [Fact]
    public void MoveSnapsAndKeepsSize()
    {
        var moved = DesignGeometry.Move(Screen, new ControlBounds(100, 100, 100, 30), 23, -18);

        Assert.Equal(new ControlBounds(120, 80, 100, 30), moved);
    }

    [Theory]
    [InlineData(-500, -500, 0, 0)]
    [InlineData(5000, 5000, 700, 570)]
    public void MoveStaysInsideTheScreen(double dx, double dy, int x, int y)
    {
        var moved = DesignGeometry.Move(Screen, new ControlBounds(100, 100, 100, 30), dx, dy);

        Assert.Equal(new ControlBounds(x, y, 100, 30), moved);
    }

    [Fact]
    public void ResizeFromBottomRightKeepsTopLeft()
    {
        var resized = DesignGeometry.Resize(Screen, ButtonDefinition, new ControlBounds(100, 100, 100, 30), ResizeEdges.Right | ResizeEdges.Bottom, 36, 21);

        Assert.Equal(new ControlBounds(100, 100, 140, 50), resized);
    }

    [Fact]
    public void ResizeFromTopLeftKeepsBottomRight()
    {
        var resized = DesignGeometry.Resize(Screen, ButtonDefinition, new ControlBounds(100, 100, 100, 30), ResizeEdges.Left | ResizeEdges.Top, -20, -10);

        Assert.Equal(new ControlBounds(80, 90, 120, 40), resized);
    }

    [Fact]
    public void ResizeRespectsMinimumSize()
    {
        var start = new ControlBounds(100, 100, 100, 30);

        var fromRight = DesignGeometry.Resize(Screen, ButtonDefinition, start, ResizeEdges.Right | ResizeEdges.Bottom, -500, -500);
        var fromLeft = DesignGeometry.Resize(Screen, ButtonDefinition, start, ResizeEdges.Left | ResizeEdges.Top, 500, 500);

        Assert.Equal(new ControlBounds(100, 100, 30, 20), fromRight);
        Assert.Equal(new ControlBounds(170, 110, 30, 20), fromLeft);
    }

    [Fact]
    public void ResizeStaysInsideTheScreen()
    {
        var start = new ControlBounds(100, 100, 100, 30);

        var grown = DesignGeometry.Resize(Screen, ButtonDefinition, start, ResizeEdges.Right | ResizeEdges.Bottom, 5000, 5000);
        var shrunkLeft = DesignGeometry.Resize(Screen, ButtonDefinition, start, ResizeEdges.Left | ResizeEdges.Top, -5000, -5000);

        Assert.Equal(new ControlBounds(100, 100, 700, 500), grown);
        Assert.Equal(new ControlBounds(0, 0, 200, 130), shrunkLeft);
    }

    [Fact]
    public void EdgeHandleOnlyChangesOneDimension()
    {
        var resized = DesignGeometry.Resize(Screen, ButtonDefinition, new ControlBounds(100, 100, 100, 30), ResizeEdges.Right, 40, 90);

        Assert.Equal(new ControlBounds(100, 100, 140, 30), resized);
    }

    [Fact]
    public void SetBoundsAcceptsExactUnsnappedValues()
    {
        var editor = new DesignEditor();
        var button = editor.AddControl(ControlType.Button, 0, 0);

        Assert.Null(editor.SetBounds(button.Id, new ControlBounds(13, 27, 101, 33)));
        Assert.Equal(new ControlBounds(13, 27, 101, 33), editor.FindControl(button.Id)!.Bounds);
    }

    [Fact]
    public void RejectedEditLeavesDocumentUnchanged()
    {
        var editor = new DesignEditor();
        var button = editor.AddControl(ControlType.Button, 0, 0);
        var before = editor.Document;

        Assert.Equal("Width must be at least 30 for a Button.", editor.SetBounds(button.Id, button.Bounds with { Width = 5 }));
        Assert.NotNull(editor.Rename(button.Id, "not valid"));
        Assert.NotNull(editor.SetIsChecked(button.Id, true));
        Assert.NotNull(editor.SetItems(button.Id, ["A"]));

        Assert.Same(before, editor.Document);
    }

    [Fact]
    public void RenameRejectsDuplicatesAndTrims()
    {
        var editor = new DesignEditor();
        var first = editor.AddControl(ControlType.Button, 0, 0);
        var second = editor.AddControl(ControlType.Button, 0, 0);

        Assert.NotNull(editor.Rename(second.Id, "button1"));
        Assert.Null(editor.Rename(second.Id, "  SubmitButton "));
        Assert.Equal("SubmitButton", editor.FindControl(second.Id)!.Name);
        Assert.Equal("Button1", editor.FindControl(first.Id)!.Name);
    }

    [Fact]
    public void TypeSpecificPropertiesCanBeEdited()
    {
        var editor = new DesignEditor();
        var check = editor.AddControl(ControlType.CheckBox, 0, 0);
        var combo = editor.AddControl(ControlType.ComboBox, 0, 100);

        Assert.Null(editor.SetText(check.Id, "Remember me"));
        Assert.Null(editor.SetIsChecked(check.Id, true));
        Assert.Null(editor.SetItems(combo.Id, ["Red", " ", "Green ", "Blue"]));

        var checkProperties = editor.FindControl(check.Id)!.Properties;
        Assert.Equal("Remember me", checkProperties.Text);
        Assert.True(checkProperties.IsChecked);
        Assert.Equal(["Red", "Green", "Blue"], editor.FindControl(combo.Id)!.Properties.Items!);
        Assert.NotNull(editor.SetText(combo.Id, "ComboBoxes have no text"));
    }

    [Fact]
    public void EditsThatChangeNothingAreNotRecorded()
    {
        var editor = new DesignEditor();
        var button = editor.AddControl(ControlType.Button, 0, 0);
        var before = editor.Document;

        Assert.Null(editor.SetBounds(button.Id, button.Bounds));
        Assert.Null(editor.Rename(button.Id, button.Name));
        Assert.Null(editor.SetText(button.Id, button.Properties.Text!));

        Assert.Same(before, editor.Document);
        editor.Undo();
        Assert.Empty(editor.Document.Screen.Controls);
    }

    [Fact]
    public void UndoAndRedoRestoreEveryKindOfEdit()
    {
        var editor = new DesignEditor();
        var states = new List<ProjectDocument> { editor.Document };

        var button = editor.AddControl(ControlType.Button, 0, 0);
        states.Add(editor.Document);
        var combo = editor.AddControl(ControlType.ComboBox, 200, 200);
        states.Add(editor.Document);
        editor.SetBounds(button.Id, new ControlBounds(50, 60, 150, 40));
        states.Add(editor.Document);
        editor.Rename(button.Id, "Submit");
        states.Add(editor.Document);
        editor.SetText(button.Id, "Send");
        states.Add(editor.Document);
        editor.SetItems(combo.Id, ["One"]);
        states.Add(editor.Document);
        editor.DeleteControl(combo.Id);
        states.Add(editor.Document);

        for (var i = states.Count - 2; i >= 0; i--)
        {
            editor.Undo();
            Assert.Same(states[i], editor.Document);
        }

        Assert.False(editor.CanUndo);

        for (var i = 1; i < states.Count; i++)
        {
            editor.Redo();
            Assert.Same(states[i], editor.Document);
        }

        Assert.False(editor.CanRedo);
    }

    [Fact]
    public void NewEditClearsRedo()
    {
        var editor = new DesignEditor();
        editor.AddControl(ControlType.Label, 0, 0);
        editor.Undo();

        editor.AddControl(ControlType.Button, 0, 0);

        Assert.False(editor.CanRedo);
    }

    [Fact]
    public void UndoingBackToTheSavedStateIsClean()
    {
        var editor = new DesignEditor();
        editor.AddControl(ControlType.Label, 0, 0);
        editor.MarkSaved();

        editor.AddControl(ControlType.Button, 0, 0);
        Assert.True(editor.IsDirty);

        editor.Undo();
        Assert.False(editor.IsDirty);

        editor.Undo();
        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void ResetClearsHistory()
    {
        var editor = new DesignEditor();
        editor.AddControl(ControlType.Label, 0, 0);

        editor.New();

        Assert.False(editor.CanUndo);
        Assert.False(editor.CanRedo);
    }
}
