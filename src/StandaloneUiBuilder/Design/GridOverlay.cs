using System.Windows;
using System.Windows.Media;

namespace StandaloneUiBuilder.Design;

/// <summary>
/// Draws the design grid. It never takes part in hit testing, so it cannot intercept
/// pointer events meant for the surface or its controls.
/// </summary>
internal sealed class GridOverlay : FrameworkElement
{
    private static readonly Pen MinorPen = CreatePen(Color.FromRgb(0xEB, 0xEE, 0xF2));
    private static readonly Pen MajorPen = CreatePen(Color.FromRgb(0xD5, 0xDA, 0xE1));

    private int gridSize = 10;

    public GridOverlay()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    public int GridSize
    {
        get => gridSize;
        set
        {
            if (value != gridSize)
            {
                gridSize = value;
                InvalidateVisual();
            }
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (gridSize <= 0)
        {
            return;
        }

        var width = ActualWidth;
        var height = ActualHeight;

        // Every fifth line is slightly darker to make distances easier to judge.
        for (var i = 1; i * gridSize < width; i++)
        {
            var x = i * gridSize + 0.5;
            drawingContext.DrawLine(i % 5 == 0 ? MajorPen : MinorPen, new Point(x, 0), new Point(x, height));
        }

        for (var i = 1; i * gridSize < height; i++)
        {
            var y = i * gridSize + 0.5;
            drawingContext.DrawLine(i % 5 == 0 ? MajorPen : MinorPen, new Point(0, y), new Point(width, y));
        }
    }

    private static Pen CreatePen(Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 1);
        pen.Freeze();
        return pen;
    }
}
