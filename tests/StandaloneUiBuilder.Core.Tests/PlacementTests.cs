using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Core.Tests;

public class PlacementTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(4.9, 0)]
    [InlineData(5, 10)]
    [InlineData(14, 10)]
    [InlineData(15, 20)]
    [InlineData(-4, 0)]
    public void SnapRoundsToNearestGridLine(double value, int expected)
    {
        Assert.Equal(expected, DesignGeometry.Snap(value, 10));
    }

    [Fact]
    public void AddedControlSnapsToGridAndUsesDefaults()
    {
        var editor = new DesignEditor();

        var button = editor.AddControl(ControlType.Button, 123, 47);

        Assert.Equal(new ControlBounds(120, 50, 100, 30), button.Bounds);
        Assert.Equal("Button1", button.Name);
        Assert.Equal("Button1", button.Properties.Text);
        Assert.NotEqual(Guid.Empty, button.Id);
        Assert.True(editor.IsDirty);
        Assert.Empty(DocumentValidator.Validate(editor.Document));
    }

    [Theory]
    [InlineData(-50, -50, 0, 0)]
    [InlineData(790, 590, 700, 570)]
    public void AddedControlIsKeptInsideTheScreen(double x, double y, int expectedX, int expectedY)
    {
        var editor = new DesignEditor();

        var button = editor.AddControl(ControlType.Button, x, y);

        Assert.Equal(expectedX, button.X);
        Assert.Equal(expectedY, button.Y);
    }

    [Fact]
    public void DefaultNamesAreUniquePerTypeAndReuseGaps()
    {
        var editor = new DesignEditor();

        var first = editor.AddControl(ControlType.Button, 0, 0);
        editor.AddControl(ControlType.Button, 0, 0);
        var label = editor.AddControl(ControlType.Label, 0, 0);
        editor.DeleteControl(first.Id);
        var third = editor.AddControl(ControlType.Button, 0, 0);

        Assert.Equal("Label1", label.Name);
        Assert.Equal("Button1", third.Name);
    }

    [Fact]
    public void NewControlsAreAddedOnTop()
    {
        var editor = new DesignEditor();

        var first = editor.AddControl(ControlType.Label, 0, 0);
        var second = editor.AddControl(ControlType.TextBox, 0, 0);

        Assert.Equal([first.Id, second.Id], editor.Screen.Controls.Select(c => c.Id));
    }

    [Fact]
    public void DeletingOneControlLeavesOthersUntouched()
    {
        var editor = new DesignEditor();
        var label = editor.AddControl(ControlType.Label, 10, 10);
        var button = editor.AddControl(ControlType.Button, 50, 50);
        var combo = editor.AddControl(ControlType.ComboBox, 100, 100);

        Assert.True(editor.DeleteControl(button.Id));

        Assert.Equal([label, combo], editor.Screen.Controls);
    }

    [Fact]
    public void DeletingAMissingControlChangesNothing()
    {
        var editor = new DesignEditor();
        var before = editor.Document;

        Assert.False(editor.DeleteControl(Guid.NewGuid()));
        Assert.Same(before, editor.Document);
        Assert.False(editor.IsDirty);
    }

    [Theory]
    [InlineData(ControlType.Label)]
    [InlineData(ControlType.Button)]
    [InlineData(ControlType.TextBox)]
    [InlineData(ControlType.CheckBox)]
    [InlineData(ControlType.ComboBox)]
    public void EveryTypeCanBePlacedWithValidDefaults(ControlType type)
    {
        var editor = new DesignEditor();

        editor.AddControl(type, 200, 200);

        Assert.Empty(DocumentValidator.Validate(editor.Document));
    }
}
