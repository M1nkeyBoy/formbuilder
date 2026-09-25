namespace StandaloneUiBuilder.Core;

/// <summary>Which edges a resize handle moves.</summary>
[Flags]
public enum ResizeEdges
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
}

/// <summary>Grid snapping and keep-inside-the-screen rules for pointer placement and editing.</summary>
public static class DesignGeometry
{
    /// <summary>Rounds a coordinate to the nearest grid line.</summary>
    public static int Snap(double value, int gridSize) =>
        gridSize <= 0 ? (int)Math.Round(value) : (int)Math.Round(value / gridSize, MidpointRounding.AwayFromZero) * gridSize;

    /// <summary>Returns the bounds for a new control whose top-left corner is dropped at a point.</summary>
    public static ControlBounds Place(ScreenDocument screen, ControlDefinition definition, double x, double y)
    {
        var width = Math.Min(definition.DefaultWidth, screen.Width);
        var height = Math.Min(definition.DefaultHeight, screen.Height);

        return new ControlBounds(
            Math.Clamp(Snap(x, screen.GridSize), 0, screen.Width - width),
            Math.Clamp(Snap(y, screen.GridSize), 0, screen.Height - height),
            width,
            height);
    }

    /// <summary>
    /// Moves bounds by a pointer offset. The new position snaps to the grid and the control
    /// stays entirely inside the screen.
    /// </summary>
    public static ControlBounds Move(ScreenDocument screen, ControlBounds start, double dx, double dy) => start with
    {
        X = Math.Clamp(Snap(start.X + dx, screen.GridSize), 0, Math.Max(0, screen.Width - start.Width)),
        Y = Math.Clamp(Snap(start.Y + dy, screen.GridSize), 0, Math.Max(0, screen.Height - start.Height)),
    };

    /// <summary>
    /// Resizes bounds by dragging the given edges by a pointer offset. Moved edges snap to the
    /// grid, the opposite edges stay put, and the result respects the type's minimum size and
    /// the screen bounds.
    /// </summary>
    public static ControlBounds Resize(ScreenDocument screen, ControlDefinition definition, ControlBounds start, ResizeEdges edges, double dx, double dy)
    {
        var left = start.X;
        var top = start.Y;
        var right = start.Right;
        var bottom = start.Bottom;

        if (edges.HasFlag(ResizeEdges.Left))
        {
            left = Math.Clamp(Snap(start.X + dx, screen.GridSize), 0, Math.Max(0, right - definition.MinWidth));
        }
        else if (edges.HasFlag(ResizeEdges.Right))
        {
            right = Math.Clamp(Snap(start.Right + dx, screen.GridSize), Math.Min(screen.Width, left + definition.MinWidth), screen.Width);
        }

        if (edges.HasFlag(ResizeEdges.Top))
        {
            top = Math.Clamp(Snap(start.Y + dy, screen.GridSize), 0, Math.Max(0, bottom - definition.MinHeight));
        }
        else if (edges.HasFlag(ResizeEdges.Bottom))
        {
            bottom = Math.Clamp(Snap(start.Bottom + dy, screen.GridSize), Math.Min(screen.Height, top + definition.MinHeight), screen.Height);
        }

        return new ControlBounds(left, top, right - left, bottom - top);
    }
}
