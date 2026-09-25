namespace StandaloneUiBuilder.Core;

/// <summary>
/// The screen edges a control keeps a fixed distance from when the window is resized.
/// Anchored to both left and right, a control stretches horizontally; to one, it moves with
/// that edge. The same applies vertically. Every control has at least one horizontal and one
/// vertical anchor.
/// </summary>
[Flags]
public enum AnchorEdges
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
    Default = Left | Top,
}

/// <summary>How a control sits along one axis of a resizable screen.</summary>
public enum AxisAlignment
{
    /// <summary>Fixed size, fixed distance from the left or top edge.</summary>
    Start,

    /// <summary>Fixed size, fixed distance from the right or bottom edge.</summary>
    End,

    /// <summary>Fixed distances from both edges; the size changes with the screen.</summary>
    Stretch,
}

/// <summary>
/// A control's placement expressed as alignment plus margins, the form WPF and similar layout
/// systems use. Margins are distances from each screen edge at the design size; Width and
/// Height are null when the control stretches along that axis.
/// </summary>
public readonly record struct AnchoredPlacement(
    AxisAlignment Horizontal,
    AxisAlignment Vertical,
    int MarginLeft,
    int MarginTop,
    int MarginRight,
    int MarginBottom,
    int? Width,
    int? Height);

public static class AnchorLayout
{
    public static bool IsValid(AnchorEdges anchor) =>
        (anchor & ~(AnchorEdges.Left | AnchorEdges.Top | AnchorEdges.Right | AnchorEdges.Bottom)) == 0
        && (anchor & (AnchorEdges.Left | AnchorEdges.Right)) != 0
        && (anchor & (AnchorEdges.Top | AnchorEdges.Bottom)) != 0;

    public static string? Validate(AnchorEdges anchor) =>
        IsValid(anchor) ? null : "Anchor to at least one of Left or Right, and at least one of Top or Bottom.";

    /// <summary>
    /// True if resizing the window would change anything: some control is anchored to the
    /// right or bottom edge. Otherwise the generated window keeps a fixed size.
    /// </summary>
    public static bool IsResizable(ScreenDocument screen) =>
        screen.Controls.Any(c => (c.Anchor & (AnchorEdges.Right | AnchorEdges.Bottom)) != 0);

    public static AnchoredPlacement Place(ScreenDocument screen, ControlDocument control)
    {
        var (horizontal, width) = Axis(control.Anchor, AnchorEdges.Left, AnchorEdges.Right, control.Width);
        var (vertical, height) = Axis(control.Anchor, AnchorEdges.Top, AnchorEdges.Bottom, control.Height);
        return new AnchoredPlacement(
            horizontal,
            vertical,
            control.X,
            control.Y,
            screen.Width - control.Right(),
            screen.Height - control.Bottom(),
            width,
            height);
    }

    /// <summary>Where a control ends up when its screen is resized to the given size.</summary>
    public static ControlBounds Resolve(ScreenDocument screen, ControlDocument control, int width, int height)
    {
        var placement = Place(screen, control);
        var (x, w) = Resolve(placement.Horizontal, placement.MarginLeft, placement.MarginRight, control.Width, width);
        var (y, h) = Resolve(placement.Vertical, placement.MarginTop, placement.MarginBottom, control.Height, height);
        return new ControlBounds(x, y, w, h);
    }

    private static (AxisAlignment Alignment, int? Size) Axis(AnchorEdges anchor, AnchorEdges start, AnchorEdges end, int size) =>
        (anchor.HasFlag(start), anchor.HasFlag(end)) switch
        {
            (true, true) => (AxisAlignment.Stretch, null),
            (false, true) => (AxisAlignment.End, size),
            _ => (AxisAlignment.Start, size),
        };

    private static (int Position, int Size) Resolve(AxisAlignment alignment, int startMargin, int endMargin, int designSize, int available) =>
        alignment switch
        {
            AxisAlignment.Stretch => (startMargin, Math.Max(0, available - startMargin - endMargin)),
            AxisAlignment.End => (available - endMargin - designSize, designSize),
            _ => (startMargin, designSize),
        };
}
