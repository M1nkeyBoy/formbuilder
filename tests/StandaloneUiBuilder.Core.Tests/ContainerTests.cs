using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Core.Tests;

public class ContainerTests
{
    /// <summary>A vertical stack at (100, 100), 200 × 150, spacing 6, with a label and a text box.</summary>
    private static (DesignEditor Editor, ControlDocument Stack, ControlDocument Label, ControlDocument Box) StackWithTwo()
    {
        var editor = new DesignEditor();
        var stack = editor.AddControl(ControlType.StackPanel, 100, 100);
        var label = editor.AddControlTo(ControlType.Label, stack.Id, 150, 110)!;
        var box = editor.AddControlTo(ControlType.TextBox, stack.Id, 150, 240)!;
        return (editor, editor.FindControl(stack.Id)!, label, box);
    }

    private static ControlBounds BoundsOf(DesignEditor editor, Guid id) =>
        ContainerLayout.Flatten(editor.Screen).Single(p => p.Control.Id == id).Bounds;

    [Fact]
    public void ContainersAreInTheToolboxWithSensibleDefaults()
    {
        var stack = ControlCatalog.Get(ControlType.StackPanel);
        var grid = ControlCatalog.Get(ControlType.Grid);

        Assert.True(stack.IsContainer && stack.IsStack);
        Assert.True(grid.IsContainer && grid.IsGrid);
        Assert.Equal(StackOrientation.Vertical, stack.CreateDefaultProperties("S").Orientation);
        Assert.Equal((2, 2), (grid.CreateDefaultProperties("G").Rows, grid.CreateDefaultProperties("G").Columns));
    }

    [Fact]
    public void NewContainerStartsEmpty()
    {
        var editor = new DesignEditor();

        var stack = editor.AddControl(ControlType.StackPanel, 0, 0);

        Assert.NotNull(stack.Children);
        Assert.Empty(stack.Children);
        Assert.Empty(DocumentValidator.Validate(editor.Document));
    }

    [Fact]
    public void VerticalStackLinesChildrenUpAndStretchesThemAcross()
    {
        var (editor, stack, label, box) = StackWithTwo();

        Assert.Equal([label.Id, box.Id], stack.Children!.Select(c => c.Id));
        Assert.Equal(new ControlBounds(100, 100, 200, 20), BoundsOf(editor, label.Id));
        Assert.Equal(new ControlBounds(100, 126, 200, 30), BoundsOf(editor, box.Id));
        Assert.Empty(DocumentValidator.Validate(editor.Document));
    }

    [Fact]
    public void HorizontalStackLinesChildrenUpLeftToRight()
    {
        var (editor, stack, label, box) = StackWithTwo();

        editor.SetOrientation(stack.Id, StackOrientation.Horizontal);
        editor.SetSpacing(stack.Id, 10);

        Assert.Equal(new ControlBounds(100, 100, 80, 150), BoundsOf(editor, label.Id));
        Assert.Equal(new ControlBounds(190, 100, 120, 150), BoundsOf(editor, box.Id));
    }

    [Fact]
    public void DroppingIntoAStackInsertsAtThePointer()
    {
        var (editor, stack, label, box) = StackWithTwo();

        // Between the label (ends at 120) and the text box (starts at 126).
        var middle = editor.AddControlTo(ControlType.Button, stack.Id, 150, 123)!;

        Assert.Equal([label.Id, middle.Id, box.Id], editor.FindControl(stack.Id)!.Children!.Select(c => c.Id));
    }

    [Fact]
    public void GridChildrenFillTheirCells()
    {
        var editor = new DesignEditor();
        var grid = editor.AddControl(ControlType.Grid, 100, 100); // 240 × 160, 2 × 2
        var topLeft = editor.AddControlTo(ControlType.Button, grid.Id, 110, 110)!;
        var bottomRight = editor.AddControlTo(ControlType.CheckBox, grid.Id, 330, 250)!;

        Assert.Equal((0, 0), (topLeft.Row, topLeft.Column));
        Assert.Equal((1, 1), (bottomRight.Row, bottomRight.Column));
        Assert.Equal(new ControlBounds(100, 100, 120, 80), BoundsOf(editor, topLeft.Id));
        Assert.Equal(new ControlBounds(220, 180, 120, 80), BoundsOf(editor, bottomRight.Id));
        Assert.Empty(DocumentValidator.Validate(editor.Document));
    }

    [Fact]
    public void GridCellsTileWithoutGapsWhenSizesDoNotDivideEvenly()
    {
        var editor = new DesignEditor();
        var grid = editor.AddControl(ControlType.Grid, 0, 0);
        editor.SetGridSize(grid.Id, 1, 3);
        editor.SetBounds(grid.Id, new ControlBounds(0, 0, 100, 50));
        var cells = Enumerable.Range(0, 3).Select(c => editor.AddControlTo(ControlType.Label, grid.Id, c * 33.4 + 1, 10)!).ToList();

        var bounds = cells.Select(c => BoundsOf(editor, c.Id)).ToList();

        Assert.Equal([0, 33, 67], bounds.Select(b => b.X));
        Assert.Equal(100, bounds.Sum(b => b.Width));
    }

    [Fact]
    public void ContainersNestAndChildrenFollowWhenTheWindowIsResized()
    {
        var editor = new DesignEditor();
        var grid = editor.AddControl(ControlType.Grid, 0, 0);
        editor.SetAnchor(grid.Id, AnchorEdges.Left | AnchorEdges.Top | AnchorEdges.Right | AnchorEdges.Bottom);
        var stack = editor.AddControlTo(ControlType.StackPanel, grid.Id, 200, 100)!; // cell (1, 1)
        var button = editor.AddControlTo(ControlType.Button, stack.Id, 200, 100)!;

        var atDesign = ContainerLayout.Flatten(editor.Screen).Single(p => p.Control.Id == button.Id);
        var resized = ContainerLayout.Flatten(editor.Screen, 1040, 760).Single(p => p.Control.Id == button.Id);

        Assert.Equal(2, atDesign.Depth);
        Assert.Equal(new ControlBounds(120, 80, 120, 30), atDesign.Bounds);
        Assert.Equal(new ControlBounds(240, 160, 240, 30), resized.Bounds); // the grid doubled in size
    }

    [Fact]
    public void ControlsMoveIntoAndOutOfContainers()
    {
        var (editor, stack, label, _) = StackWithTwo();
        var loose = editor.AddControl(ControlType.Button, 500, 400);

        Assert.Null(editor.MoveIntoContainer(loose.Id, stack.Id, 150, 101));
        Assert.Equal(loose.Id, editor.FindControl(stack.Id)!.Children![0].Id);
        Assert.DoesNotContain(editor.Screen.Controls, c => c.Id == loose.Id);

        Assert.Null(editor.MoveToScreen(label.Id, 603, 497));
        var onScreen = Assert.Single(editor.Screen.Controls, c => c.Id == label.Id);
        Assert.Equal(new ControlBounds(600, 500, 80, 20), onScreen.Bounds);
        Assert.Empty(DocumentValidator.Validate(editor.Document));

        editor.Undo();
        editor.Undo();
        Assert.Contains(editor.Screen.Controls, c => c.Id == loose.Id);
    }

    [Fact]
    public void AContainerCannotGoInsideItself()
    {
        var editor = new DesignEditor();
        var outer = editor.AddControl(ControlType.StackPanel, 0, 0);
        var inner = editor.AddControlTo(ControlType.Grid, outer.Id, 10, 10)!;

        Assert.NotNull(editor.MoveIntoContainer(outer.Id, inner.Id, 20, 20));
        Assert.NotNull(editor.MoveIntoContainer(outer.Id, outer.Id, 20, 20));
    }

    [Fact]
    public void StackChildrenCanBeReorderedAndResizedAlongTheStack()
    {
        var (editor, stack, label, box) = StackWithTwo();

        Assert.True(editor.MoveWithinContainer(box.Id, -1));
        Assert.Equal([box.Id, label.Id], editor.FindControl(stack.Id)!.Children!.Select(c => c.Id));
        Assert.False(editor.MoveWithinContainer(box.Id, -1));

        Assert.Null(editor.SetStackSize(box.Id, 60));
        Assert.Equal(60, editor.FindControl(box.Id)!.Height);
        Assert.NotNull(editor.SetStackSize(box.Id, 5));

        // Position and size inside a container belong to the container.
        Assert.NotNull(editor.SetBounds(box.Id, new ControlBounds(0, 0, 100, 30)));
    }

    [Fact]
    public void GridCellsAndSizeAreValidated()
    {
        var editor = new DesignEditor();
        var grid = editor.AddControl(ControlType.Grid, 0, 0);
        var button = editor.AddControlTo(ControlType.Button, grid.Id, 200, 100)!; // (1, 1)

        Assert.Null(editor.SetGridCell(button.Id, 0, 1));
        Assert.NotNull(editor.SetGridCell(button.Id, 2, 0));
        Assert.Contains("Button1", editor.SetGridSize(grid.Id, 1, 1));
        Assert.Null(editor.SetGridSize(grid.Id, 3, 4));
        Assert.NotNull(editor.SetGridSize(grid.Id, 0, 4));
    }

    [Fact]
    public void NamesAreUniqueAcrossTheWholeTree()
    {
        var (editor, _, label, _) = StackWithTwo();
        var outside = editor.AddControl(ControlType.Label, 500, 500);

        Assert.Equal("Label2", outside.Name);
        Assert.NotNull(editor.Rename(outside.Id, label.Name.ToLowerInvariant()));
    }

    [Fact]
    public void DeletingAContainerDeletesWhatIsInside()
    {
        var (editor, stack, label, box) = StackWithTwo();

        Assert.True(editor.DeleteControl(stack.Id));

        Assert.Null(editor.FindControl(label.Id));
        Assert.Null(editor.FindControl(box.Id));
        Assert.True(editor.DeleteControl(editor.AddControl(ControlType.Grid, 0, 0).Id));
    }

    [Fact]
    public void PastingAContainerCopiesItsChildrenWithNewIdsAndNames()
    {
        var (editor, stack, _, _) = StackWithTwo();

        var copy = editor.PasteControls([editor.FindControl(stack.Id)!], 10).Single();

        Assert.Equal("StackPanel2", copy.Name);
        Assert.Equal(["Label2", "TextBox2"], copy.Children!.Select(c => c.Name));
        Assert.DoesNotContain(copy.Children!, c => c.Id == stack.Children![0].Id);
        Assert.Empty(DocumentValidator.Validate(editor.Document));
    }

    [Fact]
    public void ContainersRoundTripThroughTheProjectFile()
    {
        var editor = new DesignEditor();
        var grid = editor.AddControl(ControlType.Grid, 0, 0);
        var stack = editor.AddControlTo(ControlType.StackPanel, grid.Id, 200, 100)!;
        editor.SetOrientation(stack.Id, StackOrientation.Horizontal);
        editor.AddControlTo(ControlType.ComboBox, stack.Id, 130, 90);

        var json = ProjectFile.Serialize(editor.Document);
        var reloaded = ProjectFile.Deserialize(json);

        Assert.Contains("\"children\": [", json);
        Assert.Contains("\"orientation\": \"Horizontal\"", json);
        Assert.Contains("\"row\": 1", json);
        Assert.Equal(ProjectFile.Serialize(editor.Document), ProjectFile.Serialize(reloaded));
        Assert.Equal(3, ControlTree.All(reloaded.MainScreen.Controls).Count());
    }

    [Theory]
    [InlineData("""{ "id": "11111111-1111-1111-1111-111111111111", "type": "Button", "name": "A", "x": 0, "y": 0, "width": 100, "height": 30, "children": [ { "id": "22222222-2222-2222-2222-222222222222", "type": "Label", "name": "L", "width": 80, "height": 20 } ] }""", "cannot hold")]
    [InlineData("""{ "id": "11111111-1111-1111-1111-111111111111", "type": "Button", "name": "A", "x": 0, "y": 0, "width": 100, "height": 30, "row": 0, "column": 0 }""", "only controls inside a Grid")]
    public void InvalidContainerDataIsRejectedOnLoad(string control, string expected)
    {
        var json = $$"""{ "schemaVersion": 3, "projectId": "9e9608a0-1ab6-4dd4-8da0-592260982971", "screen": { "controls": [ {{control}} ] } }""";

        var ex = Assert.Throws<ProjectFileException>(() => ProjectFile.Deserialize(json));

        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void GridChildOutsideTheGridIsRejectedOnLoad()
    {
        var json = """
            { "schemaVersion": 3, "projectId": "9e9608a0-1ab6-4dd4-8da0-592260982971", "screen": { "controls": [
              { "id": "11111111-1111-1111-1111-111111111111", "type": "Grid", "name": "G", "x": 0, "y": 0, "width": 200, "height": 100,
                "properties": { "rows": 2, "columns": 2 },
                "children": [ { "id": "22222222-2222-2222-2222-222222222222", "type": "Button", "name": "B", "width": 100, "height": 30, "row": 5, "column": 0 } ] } ] } }
            """;

        var ex = Assert.Throws<ProjectFileException>(() => ProjectFile.Deserialize(json));

        Assert.Contains("needs a row and column", ex.Message);
    }
}
