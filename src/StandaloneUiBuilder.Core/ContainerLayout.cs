namespace StandaloneUiBuilder.Core;

/// <summary>A control with its position on the screen, and the container it is in.</summary>
public sealed record PlacedControl(ControlDocument Control, ControlBounds Bounds, Guid? ParentId, int Depth);

/// <summary>
/// How containers arrange their children, and where every control ends up. These rules are
/// the reference: the designer, the Preview and generated code all follow them.
/// </summary>
/// <remarks>
/// A vertical StackPanel places its children top to bottom, each keeping its height and
/// stretching to the stack's width, with Spacing between them; a horizontal one does the same
/// left to right. Children that do not fit are clipped. A Grid divides itself into equal rows
/// and columns; each child fills the cell at its Row and Column, extended over RowSpan rows
/// and ColumnSpan columns.
/// </remarks>
public static class ContainerLayout
{
    /// <summary>Where each child of a container goes, relative to the container's top-left.</summary>
    public static IReadOnlyList<(ControlDocument Child, ControlBounds Bounds)> Arrange(ControlDocument container, int width, int height)
    {
        var children = container.Children ?? [];
        var properties = container.Properties;
        var arranged = new List<(ControlDocument, ControlBounds)>(children.Count);

        if (container.Type == ControlType.StackPanel)
        {
            var spacing = properties.Spacing ?? 0;
            var vertical = properties.Orientation != StackOrientation.Horizontal;
            var offset = 0;
            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (i > 0)
                {
                    offset += spacing;
                }

                arranged.Add((child, vertical
                    ? new ControlBounds(0, offset, width, child.Height)
                    : new ControlBounds(offset, 0, child.Width, height)));
                offset += vertical ? child.Height : child.Width;
            }
        }
        else if (container.Type == ControlType.Grid)
        {
            var rows = Math.Max(1, properties.Rows ?? 1);
            var columns = Math.Max(1, properties.Columns ?? 1);
            foreach (var child in children)
            {
                var row = Math.Clamp(child.Row ?? 0, 0, rows - 1);
                var column = Math.Clamp(child.Column ?? 0, 0, columns - 1);
                var lastRow = Math.Clamp(row + (child.RowSpan ?? 1), row + 1, rows);
                var lastColumn = Math.Clamp(column + (child.ColumnSpan ?? 1), column + 1, columns);
                var x = CellEdge(width, columns, column);
                var y = CellEdge(height, rows, row);
                arranged.Add((child, new ControlBounds(x, y, CellEdge(width, columns, lastColumn) - x, CellEdge(height, rows, lastRow) - y)));
            }
        }

        return arranged;
    }

    /// <summary>
    /// Every control with its bounds on the screen, parents before children, at the design
    /// size or at a resized screen size (controls on the screen follow their anchors).
    /// </summary>
    public static IReadOnlyList<PlacedControl> Flatten(ScreenDocument screen, int? width = null, int? height = null)
    {
        var placed = new List<PlacedControl>();
        foreach (var control in screen.Controls)
        {
            var bounds = width is null && height is null
                ? control.Bounds
                : AnchorLayout.Resolve(screen, control, width ?? screen.Width, height ?? screen.Height);
            Add(placed, control, bounds, parentId: null, depth: 0);
        }

        return placed;
    }

    /// <summary>The innermost container at a screen point, ignoring some controls (and what is inside them).</summary>
    public static PlacedControl? ContainerAt(ScreenDocument screen, double x, double y, IReadOnlyCollection<Guid>? ignore = null)
    {
        ignore ??= [];
        var ignored = ignore.SelectMany(id => ControlTree.Find(screen.Controls, id) is { } c ? ControlTree.All([c]) : []).Select(c => c.Id).ToHashSet();
        return Flatten(screen)
            .Where(p => p.Control.Children is not null && !ignored.Contains(p.Control.Id) && Contains(p.Bounds, x, y))
            .OrderByDescending(p => p.Depth)
            .FirstOrDefault();
    }

    /// <summary>For a StackPanel, the index a control dropped at a screen point goes to.</summary>
    public static int StackIndexAt(PlacedControl stack, double x, double y, Guid? ignore = null)
    {
        var vertical = stack.Control.Properties.Orientation != StackOrientation.Horizontal;
        var index = 0;
        foreach (var (child, bounds) in Arrange(stack.Control, stack.Bounds.Width, stack.Bounds.Height))
        {
            if (child.Id == ignore)
            {
                continue;
            }

            var middle = vertical ? stack.Bounds.Y + bounds.Y + bounds.Height / 2.0 : stack.Bounds.X + bounds.X + bounds.Width / 2.0;
            if ((vertical ? y : x) > middle)
            {
                index++;
            }
        }

        return index;
    }

    /// <summary>For a Grid, the cell at a screen point.</summary>
    public static (int Row, int Column) GridCellAt(PlacedControl grid, double x, double y)
    {
        var rows = Math.Max(1, grid.Control.Properties.Rows ?? 1);
        var columns = Math.Max(1, grid.Control.Properties.Columns ?? 1);
        var column = (int)Math.Floor((x - grid.Bounds.X) * columns / Math.Max(1, grid.Bounds.Width));
        var row = (int)Math.Floor((y - grid.Bounds.Y) * rows / Math.Max(1, grid.Bounds.Height));
        return (Math.Clamp(row, 0, rows - 1), Math.Clamp(column, 0, columns - 1));
    }

    private static void Add(List<PlacedControl> placed, ControlDocument control, ControlBounds bounds, Guid? parentId, int depth)
    {
        placed.Add(new PlacedControl(control, bounds, parentId, depth));
        foreach (var (child, relative) in Arrange(control, bounds.Width, bounds.Height))
        {
            Add(placed, child, relative with { X = bounds.X + relative.X, Y = bounds.Y + relative.Y }, control.Id, depth + 1);
        }
    }

    // Cell boundaries are rounded from exact fractions so cells tile the grid with no gaps.
    private static int CellEdge(int size, int count, int index) =>
        (int)Math.Round(size * (double)index / count, MidpointRounding.AwayFromZero);

    private static bool Contains(ControlBounds bounds, double x, double y) =>
        x >= bounds.X && x < bounds.Right && y >= bounds.Y && y < bounds.Bottom;
}
