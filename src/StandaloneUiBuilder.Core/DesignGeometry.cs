namespace StandaloneUiBuilder.Core;

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
}
