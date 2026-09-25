using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Core.Tests;

public class ArrangeTests
{
    private static (DesignEditor Editor, ControlDocument A, ControlDocument B, ControlDocument C) Three()
    {
        var editor = new DesignEditor();
        var a = editor.AddControl(ControlType.Button, 10, 10);        // 100 × 30
        var b = editor.AddControl(ControlType.TextBox, 200, 100);     // 120 × 30
        var c = editor.AddControl(ControlType.Label, 500, 300);       // 80 × 20
        return (editor, a, b, c);
    }

    private static ControlBounds Bounds(DesignEditor editor, ControlDocument control) => editor.FindControl(control.Id)!.Bounds;

    // The reference TextBox is at (200, 100), 120 × 30; the Button at (10, 10), 100 × 30.
    [Theory]
    [InlineData(AlignTo.Lefts, 200, 10)]
    [InlineData(AlignTo.Rights, 220, 10)]
    [InlineData(AlignTo.Centers, 210, 10)]
    [InlineData(AlignTo.Tops, 10, 100)]
    [InlineData(AlignTo.Bottoms, 10, 100)]
    public void AlignLinesControlsUpWithTheReference(AlignTo edge, int expectedX, int expectedY)
    {
        var (editor, a, b, _) = Three();

        Assert.True(editor.Align([a.Id, b.Id], b.Id, edge));

        Assert.Equal(new ControlBounds(200, 100, 120, 30), Bounds(editor, b));
        Assert.Equal((expectedX, expectedY), (Bounds(editor, a).X, Bounds(editor, a).Y));
    }

    [Fact]
    public void AligningIsOneUndoableStepAndLeavesTheReference()
    {
        var (editor, a, b, c) = Three();

        editor.Align([a.Id, b.Id, c.Id], c.Id, AlignTo.Middles);

        Assert.Equal(300, Bounds(editor, c).Y);
        Assert.Equal((295, 295), (Bounds(editor, a).Y, Bounds(editor, b).Y));
        editor.Undo();
        Assert.Equal((10, 100), (Bounds(editor, a).Y, Bounds(editor, b).Y));
    }

    [Fact]
    public void SameSizeCopiesTheReferenceButKeepsMinimums()
    {
        var (editor, a, b, c) = Three();
        editor.SetBounds(c.Id, new ControlBounds(500, 300, 20, 16));

        Assert.True(editor.MakeSameSize([a.Id, b.Id, c.Id], c.Id, SameSize.Both));

        // A Button is at least 30 × 20 and a TextBox 30 × 20.
        Assert.Equal((30, 20), (Bounds(editor, a).Width, Bounds(editor, a).Height));
        Assert.Equal((30, 20), (Bounds(editor, b).Width, Bounds(editor, b).Height));

        var (other, x, y, _) = Three();
        other.MakeSameSize([x.Id, y.Id], y.Id, SameSize.Width);
        Assert.Equal((120, 30), (Bounds(other, x).Width, Bounds(other, x).Height));
    }

    [Fact]
    public void DistributeSpacesControlsEvenlyBetweenTheOutermost()
    {
        var (editor, a, b, c) = Three();

        Assert.True(editor.Distribute([c.Id, a.Id, b.Id], horizontally: true));

        // From 10 to 580 holds 300 of controls: two gaps of 135.
        Assert.Equal([10, 245, 500], new[] { a, b, c }.Select(x => Bounds(editor, x).X));
        Assert.False(editor.Distribute([a.Id, b.Id], horizontally: true));
    }

    [Fact]
    public void ControlsInsideContainersAreLeftToTheirContainer()
    {
        var editor = new DesignEditor();
        var stack = editor.AddControl(ControlType.StackPanel, 10, 10);
        var inside = editor.AddControlTo(ControlType.Button, stack.Id, 20, 20)!;
        var outside = editor.AddControl(ControlType.Button, 400, 300);

        Assert.False(editor.Align([inside.Id, outside.Id], inside.Id, AlignTo.Lefts));
        Assert.True(editor.Align([inside.Id, outside.Id, stack.Id], outside.Id, AlignTo.Lefts));
        Assert.Equal(400, editor.FindControl(stack.Id)!.X);
    }

    [Fact]
    public void ArrangedControlsStayOnTheScreen()
    {
        var editor = new DesignEditor();
        var wide = editor.AddControl(ControlType.Button, 10, 10);
        editor.SetBounds(wide.Id, new ControlBounds(10, 10, 700, 30));
        var right = editor.AddControl(ControlType.Button, 690, 100);

        editor.MakeSameSize([wide.Id, right.Id], wide.Id, SameSize.Width);

        Assert.Equal(new ControlBounds(100, 100, 700, 30), editor.FindControl(right.Id)!.Bounds);
    }
}
