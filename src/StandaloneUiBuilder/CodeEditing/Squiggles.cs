using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using StandaloneUiBuilder.CodeAnalysis;

namespace StandaloneUiBuilder.CodeEditing;

/// <summary>Wavy underlines under the code's problems: red for errors, green for warnings.</summary>
internal sealed class Squiggles : IBackgroundRenderer
{
    private static readonly Pen ErrorPen = FrozenPen(Color.FromRgb(0xE5, 0x1E, 0x1E));
    private static readonly Pen WarningPen = FrozenPen(Color.FromRgb(0x2E, 0x9E, 0x44));

    private IReadOnlyList<CodeProblem> problems = [];

    public KnownLayer Layer => KnownLayer.Selection;

    /// <summary>The problems to underline; those outside the code are not drawn.</summary>
    public void Show(IReadOnlyList<CodeProblem> shown) => problems = shown;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid || textView.Document is not { } document)
        {
            return;
        }

        foreach (var problem in problems.Where(p => p.Start >= 0 && p.Start <= document.TextLength))
        {
            // A problem with no width (a missing semicolon) is shown under the character before it.
            var start = Math.Min(problem.Start, document.TextLength);
            var length = Math.Min(problem.Length, document.TextLength - start);
            if (length == 0)
            {
                start = Math.Max(0, start - 1);
                length = Math.Min(1, document.TextLength - start);
            }

            var segment = new TextSegment { StartOffset = start, Length = length };
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
            {
                drawingContext.DrawGeometry(null, problem.IsError ? ErrorPen : WarningPen, Wave(rect.BottomLeft, Math.Max(rect.Width, 6)));
            }
        }
    }

    private static StreamGeometry Wave(Point start, double width)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(start.X, start.Y - 1), false, false);
            for (var x = 2.0; x <= width; x += 2)
            {
                context.LineTo(new Point(start.X + x, start.Y - 1 + (x % 4 == 0 ? 0 : 2)), true, false);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static Pen FrozenPen(Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 1);
        pen.Freeze();
        return pen;
    }
}
