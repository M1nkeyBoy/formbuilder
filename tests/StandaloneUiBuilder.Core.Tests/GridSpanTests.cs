using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Core.Tests;

public class GridSpanTests
{
    /// <summary>A 3 × 3 grid at (0, 0), 300 × 300, with a button in its top-left cell.</summary>
    private static (DesignEditor Editor, ControlDocument Grid, ControlDocument Button) GridWithButton()
    {
        var editor = new DesignEditor();
        var grid = editor.AddControl(ControlType.Grid, 0, 0);
        editor.SetGridSize(grid.Id, 3, 3);
        editor.SetBounds(grid.Id, new ControlBounds(0, 0, 300, 300));
        var button = editor.AddControlTo(ControlType.Button, grid.Id, 10, 10)!;
        return (editor, editor.FindControl(grid.Id)!, button);
    }

    private static ControlBounds BoundsOf(DesignEditor editor, Guid id) =>
        ContainerLayout.Flatten(editor.Screen).Single(p => p.Control.Id == id).Bounds;

    [Fact]
    public void SpannedControlCoversSeveralCells()
    {
        var (editor, _, button) = GridWithButton();

        Assert.Null(editor.SetGridSpan(button.Id, 2, 3));

        Assert.Equal(new ControlBounds(0, 0, 300, 200), BoundsOf(editor, button.Id));
        Assert.Empty(DocumentValidator.Validate(editor.Document));
    }

    [Fact]
    public void SpanOfOneIsNotStored()
    {
        var (editor, _, button) = GridWithButton();
        editor.SetGridSpan(button.Id, 2, 2);

        editor.SetGridSpan(button.Id, 1, 1);

        var stored = editor.FindControl(button.Id)!;
        Assert.Null(stored.RowSpan);
        Assert.Null(stored.ColumnSpan);
        Assert.DoesNotContain("Span", ProjectFile.Serialize(editor.Document));
    }

    [Fact]
    public void SpanMustFitInsideTheGrid()
    {
        var (editor, _, button) = GridWithButton();
        editor.SetGridCell(button.Id, 1, 1);
        var before = editor.Document;

        Assert.Contains("up to 2 rows and 2 columns", editor.SetGridSpan(button.Id, 3, 1));
        Assert.NotNull(editor.SetGridSpan(button.Id, 0, 1));
        Assert.Same(before, editor.Document);
    }

    [Fact]
    public void MovingASpannedControlMustKeepItInside()
    {
        var (editor, _, button) = GridWithButton();
        editor.SetGridSpan(button.Id, 1, 2);

        Assert.NotNull(editor.SetGridCell(button.Id, 0, 2));
        Assert.Null(editor.SetGridCell(button.Id, 2, 1));
    }

    [Fact]
    public void GridCannotShrinkBelowWhatItsChildrenSpan()
    {
        var (editor, grid, button) = GridWithButton();
        editor.SetGridSpan(button.Id, 1, 3);

        Assert.Contains("Button1", editor.SetGridSize(grid.Id, 3, 2));
        Assert.Null(editor.SetGridSize(grid.Id, 1, 3));
    }

    [Fact]
    public void DraggingWithinTheSameGridKeepsTheSpanWhereItFits()
    {
        var (editor, grid, button) = GridWithButton();
        editor.SetGridSpan(button.Id, 2, 1);

        // Into the middle column, top row: two rows still fit.
        editor.MoveIntoContainer(button.Id, grid.Id, 150, 10);
        Assert.Equal((2, (int?)null), (editor.FindControl(button.Id)!.RowSpan ?? 1, editor.FindControl(button.Id)!.ColumnSpan));

        // Into the bottom row: the span no longer fits, so it resets.
        editor.MoveIntoContainer(button.Id, grid.Id, 150, 250);
        Assert.Null(editor.FindControl(button.Id)!.RowSpan);
        Assert.Empty(DocumentValidator.Validate(editor.Document));
    }

    [Fact]
    public void SpanIsDroppedWhenAControlLeavesTheGrid()
    {
        var (editor, _, button) = GridWithButton();
        editor.SetGridSpan(button.Id, 2, 2);

        editor.MoveToScreen(button.Id, 400, 400);

        Assert.Null(editor.FindControl(button.Id)!.RowSpan);
        Assert.Empty(DocumentValidator.Validate(editor.Document));
    }

    [Fact]
    public void SpansRoundTripThroughTheProjectFile()
    {
        var (editor, _, button) = GridWithButton();
        editor.SetGridSpan(button.Id, 2, 3);

        var json = ProjectFile.Serialize(editor.Document);
        var reloaded = ProjectFile.Deserialize(json);

        Assert.Contains("\"rowSpan\": 2", json);
        Assert.Contains("\"columnSpan\": 3", json);
        Assert.Equal(2, ControlTree.Find(reloaded.MainScreen.Controls, button.Id)!.RowSpan);
    }

    [Theory]
    [InlineData("\"row\": 1, \"column\": 0, \"rowSpan\": 2", "span goes beyond")]
    [InlineData("\"row\": 0, \"column\": 0, \"columnSpan\": 0", "span goes beyond")]
    public void InvalidSpansAreRejectedOnLoad(string cell, string expected)
    {
        var json = $$"""
            { "schemaVersion": 4, "projectId": "9e9608a0-1ab6-4dd4-8da0-592260982971", "screen": { "controls": [
              { "id": "11111111-1111-1111-1111-111111111111", "type": "Grid", "name": "G", "x": 0, "y": 0, "width": 200, "height": 100,
                "properties": { "rows": 2, "columns": 2 },
                "children": [ { "id": "22222222-2222-2222-2222-222222222222", "type": "Button", "name": "B", "width": 100, "height": 30, {{cell}} } ] } ] } }
            """;

        var ex = Assert.Throws<ProjectFileException>(() => ProjectFile.Deserialize(json));

        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void SpanOutsideAGridIsRejectedOnLoad()
    {
        var json = """
            { "schemaVersion": 4, "projectId": "9e9608a0-1ab6-4dd4-8da0-592260982971", "screen": { "controls": [
              { "id": "11111111-1111-1111-1111-111111111111", "type": "Button", "name": "B", "x": 0, "y": 0, "width": 100, "height": 30, "rowSpan": 2 } ] } }
            """;

        Assert.Contains("only controls inside a Grid", Assert.Throws<ProjectFileException>(() => ProjectFile.Deserialize(json)).Message);
    }
}
