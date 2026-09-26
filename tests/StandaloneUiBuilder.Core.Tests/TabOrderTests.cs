using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Core.Tests;

public class TabOrderTests
{
    private static IEnumerable<string> Names(DesignEditor editor) => TabSequence.Resolve(editor.Screen).Select(c => c.Name);

    // Button1 at the bottom left, TextBox1 at the top right, Label1 (no tab stop), CheckBox1 at the top left.
    private static DesignEditor Sample()
    {
        var editor = new DesignEditor();
        editor.AddControl(ControlType.Button, 20, 300);
        editor.AddControl(ControlType.TextBox, 300, 20);
        editor.AddControl(ControlType.Label, 20, 200);
        editor.AddControl(ControlType.CheckBox, 20, 24);
        return editor;
    }

    private static Guid Id(DesignEditor editor, string name) => ControlTree.All(editor.Screen.Controls).Single(c => c.Name == name).Id;

    [Fact]
    public void WithoutATabOrderTabFollowsTheControlsOrderAndSkipsLabels()
    {
        var editor = Sample();

        Assert.Equal(["Button1", "TextBox1", "CheckBox1"], Names(editor));
        Assert.Null(editor.Screen.TabOrder);
    }

    [Fact]
    public void ByPositionGoesTopToBottomThenLeftToRight()
    {
        var editor = Sample();

        Assert.True(editor.SetTabOrderByPosition());

        // CheckBox1 (y 20) and TextBox1 (y 20) share a row; CheckBox1 is further left.
        Assert.Equal(["CheckBox1", "TextBox1", "Button1"], Names(editor));
        Assert.False(editor.SetTabOrderByPosition());
        editor.Undo();
        Assert.Equal(["Button1", "TextBox1", "CheckBox1"], Names(editor));
    }

    [Fact]
    public void PuttingControlsInTurnSetsTheOrderAndTheDefaultOrderIsNotStored()
    {
        var editor = Sample();

        Assert.True(editor.PutInTabOrder(Id(editor, "CheckBox1"), 0));
        Assert.Equal(["CheckBox1", "Button1", "TextBox1"], Names(editor));
        Assert.NotNull(editor.Screen.TabOrder);

        editor.PutInTabOrder(Id(editor, "Button1"), 0);
        editor.PutInTabOrder(Id(editor, "TextBox1"), 1);
        editor.PutInTabOrder(Id(editor, "CheckBox1"), 2);
        Assert.Equal(["Button1", "TextBox1", "CheckBox1"], Names(editor));
        Assert.Null(editor.Screen.TabOrder);

        Assert.False(editor.PutInTabOrder(Id(editor, "Label1"), 0));
    }

    [Fact]
    public void NewControlsComeLastAndDeletedOnesLeaveTheOrder()
    {
        var editor = Sample();
        editor.SetTabOrderByPosition();
        var added = editor.AddControl(ControlType.ComboBox, 500, 500);

        Assert.Equal(["CheckBox1", "TextBox1", "Button1", added.Name], Names(editor));

        editor.DeleteControl(Id(editor, "TextBox1"));
        Assert.Equal(2, editor.Screen.TabOrder!.Count);
        Assert.Empty(DocumentValidator.Validate(editor.Document));
    }

    [Fact]
    public void ResetGoesBackToTheControlsOrder()
    {
        var editor = Sample();
        editor.SetTabOrderByPosition();

        Assert.True(editor.ResetTabOrder());
        Assert.Null(editor.Screen.TabOrder);
        Assert.False(editor.ResetTabOrder());
    }

    [Fact]
    public void SiblingRanksKeepAContainersControlsTogether()
    {
        var editor = new DesignEditor();
        var stack = editor.AddControl(ControlType.StackPanel, 20, 20);
        var a = editor.AddControlTo(ControlType.Button, stack.Id, 30, 30)!;
        var b = editor.AddControlTo(ControlType.Button, stack.Id, 30, 200)!;
        var c = editor.AddControl(ControlType.TextBox, 400, 20);

        editor.SetTabOrder([c.Id, b.Id, a.Id]);

        var ranks = TabSequence.SiblingRanks(editor.Screen);
        Assert.Equal(0, ranks[c.Id]);
        Assert.Equal(1, ranks[stack.Id]);
        Assert.Equal(0, ranks[b.Id]);
        Assert.Equal(1, ranks[a.Id]);
    }

    [Fact]
    public void DuplicatingAScreenCopiesItsTabOrder()
    {
        var editor = Sample();
        editor.SetTabOrderByPosition();

        editor.DuplicateScreen();

        Assert.Equal(["CheckBox1", "TextBox1", "Button1"], Names(editor));
        Assert.Empty(DocumentValidator.Validate(editor.Document));
    }

    [Fact]
    public void TabOrderSurvivesSavingAndStrayIdsAreRejected()
    {
        var editor = Sample();
        editor.SetTabOrderByPosition();

        var reloaded = ProjectFile.Deserialize(ProjectFile.Serialize(editor.Document));
        Assert.Equal(editor.Screen.TabOrder, reloaded.MainScreen.TabOrder);

        var label = Id(editor, "Label1");
        var bad = reloaded.WithScreen(reloaded.MainScreen with { TabOrder = [label, label] });
        var errors = DocumentValidator.Validate(bad);
        Assert.Contains(errors, e => e.Contains("more than once"));
        Assert.Contains(errors, e => e.Contains("not a control on the screen that takes input"));
    }
}
