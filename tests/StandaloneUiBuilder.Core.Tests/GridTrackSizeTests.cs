using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Core.Tests;

public class GridTrackSizeTests
{
    [Theory]
    [InlineData("100", 100, false, "100")]
    [InlineData(" 40 ", 40, false, "40")]
    [InlineData("*", 1, true, "*")]
    [InlineData("2*", 2, true, "2*")]
    [InlineData(" 1.5 * ", 1.5, true, "1.5*")]
    [InlineData("1*", 1, true, "*")]
    public void ParsesFixedSizesAndShares(string text, double value, bool proportional, string canonical)
    {
        Assert.True(GridTrackSize.TryParse(text, out var size));
        Assert.Equal(new GridTrackSize(value, proportional), size);
        Assert.Equal(canonical, size.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("0*")]
    [InlineData("auto")]
    [InlineData("12.5")]
    [InlineData("x*")]
    [InlineData("20000")]
    public void RejectsAnythingElse(string text)
    {
        Assert.False(GridTrackSize.TryParse(text, out _));
    }

    [Fact]
    public void FixedTracksComeFirstAndSharesSplitTheRest()
    {
        var sizes = new[] { new GridTrackSize(60, false), new GridTrackSize(1, true), new GridTrackSize(2, true) };

        Assert.Equal([0, 60, 140, 300], GridTrackSize.Edges(sizes, 300));
    }

    [Fact]
    public void EqualSharesTileWithoutGaps()
    {
        Assert.Equal([0, 33, 67, 100], GridTrackSize.Edges(GridTrackSize.Resolve(null, 3), 100));
    }

    [Fact]
    public void WhenFixedTracksOverflowSharesGetNothing()
    {
        var sizes = new[] { new GridTrackSize(200, false), new GridTrackSize(1, true), new GridTrackSize(200, false) };

        Assert.Equal([0, 200, 200, 400], GridTrackSize.Edges(sizes, 300));
    }

    [Fact]
    public void SizedGridPlacesChildrenInSizedCells()
    {
        var editor = new DesignEditor();
        var grid = editor.AddControl(ControlType.Grid, 0, 0);
        editor.SetBounds(grid.Id, new ControlBounds(0, 0, 300, 200));
        Assert.Null(editor.SetGridTrackSizes(grid.Id, ["50", "*"], ["*", "2*"]));
        var button = editor.AddControlTo(ControlType.Button, grid.Id, 150, 100)!; // right column, bottom row

        var bounds = ContainerLayout.Flatten(editor.Screen).Single(p => p.Control.Id == button.Id).Bounds;

        Assert.Equal((1, 1), (button.Row, button.Column));
        Assert.Equal(new ControlBounds(100, 50, 200, 150), bounds);
        Assert.Empty(DocumentValidator.Validate(editor.Document));
    }

    [Fact]
    public void DropTargetFollowsSizedTracks()
    {
        var editor = new DesignEditor();
        var grid = editor.AddControl(ControlType.Grid, 0, 0);
        editor.SetBounds(grid.Id, new ControlBounds(0, 0, 300, 200));
        editor.SetGridTrackSizes(grid.Id, ["20", "*"], ["250", "*"]);

        var inNarrowTop = editor.AddControlTo(ControlType.Label, grid.Id, 240, 10)!;

        Assert.Equal((0, 0), (inNarrowTop.Row, inNarrowTop.Column));
    }

    [Fact]
    public void SizesAreStoredCanonicallyAndOmittedWhenAllEqual()
    {
        var editor = new DesignEditor();
        var grid = editor.AddControl(ControlType.Grid, 0, 0);

        editor.SetGridTrackSizes(grid.Id, [" 100", "2 *"], ["*", "1*"]);
        var properties = editor.FindControl(grid.Id)!.Properties;

        Assert.Equal(["100", "2*"], properties.RowSizes!);
        Assert.Null(properties.ColumnSizes);

        editor.SetGridTrackSizes(grid.Id, ["*", "*"], ["*", "*"]);
        Assert.Null(editor.FindControl(grid.Id)!.Properties.RowSizes);
    }

    [Fact]
    public void WrongCountOrBadSizeIsRefusedWithAClearMessage()
    {
        var editor = new DesignEditor();
        var grid = editor.AddControl(ControlType.Grid, 0, 0);
        var before = editor.Document;

        Assert.Contains("Give 2 row sizes", editor.SetGridTrackSizes(grid.Id, ["*"], ["*", "*"]));
        Assert.Contains("\"auto\" is not a column size", editor.SetGridTrackSizes(grid.Id, ["*", "*"], ["auto", "*"]));
        Assert.Same(before, editor.Document);
    }

    [Fact]
    public void ChangingRowCountKeepsExistingSizes()
    {
        var editor = new DesignEditor();
        var grid = editor.AddControl(ControlType.Grid, 0, 0);
        editor.SetGridTrackSizes(grid.Id, ["40", "*"], ["*", "*"]);

        editor.SetGridSize(grid.Id, 3, 2);
        Assert.Equal(["40", "*", "*"], editor.FindControl(grid.Id)!.Properties.RowSizes!);

        editor.SetGridSize(grid.Id, 1, 2);
        Assert.Equal(["40"], editor.FindControl(grid.Id)!.Properties.RowSizes!);
    }

    [Fact]
    public void SizesRoundTripAndBadStoredSizesAreRejectedOnLoad()
    {
        var editor = new DesignEditor();
        var grid = editor.AddControl(ControlType.Grid, 0, 0);
        editor.SetGridTrackSizes(grid.Id, ["40", "3*"], ["*", "*"]);

        var json = ProjectFile.Serialize(editor.Document);
        Assert.Contains("\"rowSizes\": [", json);
        Assert.Equal(["40", "3*"], ControlTree.Find(ProjectFile.Deserialize(json).MainScreen.Controls, grid.Id)!.Properties.RowSizes!);

        var broken = json.Replace("\"3*\"", "\"lots\"");
        Assert.Contains("\"lots\" is not a row size", Assert.Throws<ProjectFileException>(() => ProjectFile.Deserialize(broken)).Message);
    }
}
