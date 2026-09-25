using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Core.Tests;

public class GroupEditingTests
{
    private static (DesignEditor Editor, ControlDocument A, ControlDocument B, ControlDocument C) ThreeControls()
    {
        var editor = new DesignEditor();
        var a = editor.AddControl(ControlType.Button, 100, 100);
        var b = editor.AddControl(ControlType.Label, 300, 200);
        var c = editor.AddControl(ControlType.TextBox, 500, 300);
        return (editor, a, b, c);
    }

    [Fact]
    public void MovingSeveralControlsIsOneUndoableStep()
    {
        var (editor, a, b, c) = ThreeControls();

        Assert.True(editor.MoveControls([a.Id, b.Id], 15, -7));

        Assert.Equal((115, 93), (editor.FindControl(a.Id)!.X, editor.FindControl(a.Id)!.Y));
        Assert.Equal((315, 193), (editor.FindControl(b.Id)!.X, editor.FindControl(b.Id)!.Y));
        Assert.Equal(c, editor.FindControl(c.Id));

        editor.Undo();
        Assert.Equal(a, editor.FindControl(a.Id));
        Assert.Equal(b, editor.FindControl(b.Id));
    }

    [Fact]
    public void GroupMoveStopsAtTheScreenEdgeForTheWholeGroup()
    {
        var (editor, a, _, c) = ThreeControls();

        editor.MoveControls([a.Id, c.Id], 1000, -1000);

        // c (120 wide at x 500) reaches the right edge; a (y 100) reaches the top.
        Assert.Equal(new ControlBounds(280, 0, 100, 30), editor.FindControl(a.Id)!.Bounds);
        Assert.Equal(new ControlBounds(680, 200, 120, 30), editor.FindControl(c.Id)!.Bounds);
    }

    [Fact]
    public void MovingNothingOrNowhereRecordsNothing()
    {
        var (editor, a, _, _) = ThreeControls();
        var before = editor.Document;

        Assert.False(editor.MoveControls([], 10, 10));
        Assert.False(editor.MoveControls([a.Id], 0, 0));
        Assert.Same(before, editor.Document);
    }

    [Fact]
    public void DeletingSeveralControlsIsOneStep()
    {
        var (editor, a, b, c) = ThreeControls();

        Assert.Equal(2, editor.DeleteControls([a.Id, c.Id]));
        Assert.Equal([b.Id], editor.Screen.Controls.Select(x => x.Id));

        editor.Undo();
        Assert.Equal(3, editor.Screen.Controls.Count);
    }

    [Fact]
    public void PastedCopiesGetNewIdsNamesAndAnOffset()
    {
        var (editor, a, b, _) = ThreeControls();
        editor.Rename(b.Id, "Caption");
        var copies = new[] { editor.FindControl(a.Id)!, editor.FindControl(b.Id)! };

        var pasted = editor.PasteControls(copies, 10);

        Assert.Equal(2, pasted.Count);
        Assert.All(pasted, p => Assert.DoesNotContain(p.Id, new[] { a.Id, b.Id }));
        Assert.Equal(["Button2", "Caption2"], pasted.Select(p => p.Name));
        Assert.Equal(new ControlBounds(110, 110, 100, 30), pasted[0].Bounds);
        Assert.Equal(pasted.Select(p => p.Id), editor.Screen.Controls.TakeLast(2).Select(c => c.Id));
        Assert.Empty(DocumentValidator.Validate(editor.Document));

        editor.Undo();
        Assert.Equal(3, editor.Screen.Controls.Count);
    }

    [Fact]
    public void PasteKeepsCopiesInsideTheScreen()
    {
        var editor = new DesignEditor();
        var corner = editor.AddControl(ControlType.Button, 700, 570);

        var pasted = editor.PasteControls([corner], 10).Single();

        Assert.Equal(new ControlBounds(700, 570, 100, 30), pasted.Bounds);
    }

    [Fact]
    public void PastingIntoAnotherDocumentKeepsFreeNames()
    {
        var (source, a, _, _) = ThreeControls();
        var target = new DesignEditor();

        var pasted = target.PasteControls([source.FindControl(a.Id)!], 10).Single();

        Assert.Equal("Button1", pasted.Name);
    }

    [Fact]
    public void PastedCopiesKeepPropertiesAndAnchors()
    {
        var editor = new DesignEditor();
        var combo = editor.AddControl(ControlType.ComboBox, 10, 10);
        editor.SetItems(combo.Id, ["A", "B"]);
        editor.SetAnchor(combo.Id, AnchorEdges.Right | AnchorEdges.Bottom);

        var pasted = editor.PasteControls([editor.FindControl(combo.Id)!], 10).Single();

        Assert.Equal(["A", "B"], pasted.Properties.Items!);
        Assert.Equal(AnchorEdges.Right | AnchorEdges.Bottom, pasted.Anchor);
    }

    [Fact]
    public void BringToFrontAndSendToBackKeepRelativeOrder()
    {
        var (editor, a, b, c) = ThreeControls();

        Assert.True(editor.BringToFront([a.Id, b.Id]));
        Assert.Equal([c.Id, a.Id, b.Id], editor.Screen.Controls.Select(x => x.Id));

        Assert.True(editor.SendToBack([b.Id]));
        Assert.Equal([b.Id, c.Id, a.Id], editor.Screen.Controls.Select(x => x.Id));

        // Already at the back: nothing to record.
        Assert.False(editor.SendToBack([b.Id]));

        editor.Undo();
        Assert.Equal([c.Id, a.Id, b.Id], editor.Screen.Controls.Select(x => x.Id));
    }

    [Fact]
    public void ScreenSizeCanChangeWhileEveryControlFits()
    {
        var (editor, _, _, _) = ThreeControls();

        Assert.Null(editor.SetScreenSize(1024, 768));
        Assert.Equal((1024, 768), (editor.Screen.Width, editor.Screen.Height));

        Assert.Contains("TextBox1", editor.SetScreenSize(600, 768));
        Assert.NotNull(editor.SetScreenSize(50, 768));
        Assert.Equal(1024, editor.Screen.Width);

        editor.Undo();
        Assert.Equal(800, editor.Screen.Width);
    }

    [Fact]
    public void LargerScreenAllowsPlacementFurtherOut()
    {
        var editor = new DesignEditor();
        editor.SetScreenSize(1200, 900);

        var button = editor.AddControl(ControlType.Button, 1000, 800);

        Assert.Equal(new ControlBounds(1000, 800, 100, 30), button.Bounds);
    }
}
