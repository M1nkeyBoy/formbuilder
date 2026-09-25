using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Design;

/// <summary>
/// The design canvas. It is sized in design coordinates (DIPs) and never scales with the
/// window, and it renders entirely from a <see cref="ScreenDocument"/>.
/// </summary>
internal sealed class DesignSurface : Grid
{
    private readonly GridOverlay gridOverlay = new();
    private readonly Canvas controlsLayer = new() { ClipToBounds = true };

    public DesignSurface()
    {
        Background = Brushes.White;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;

        Children.Add(gridOverlay);
        Children.Add(controlsLayer);
    }

    public void Render(ScreenDocument screen)
    {
        Width = screen.Width;
        Height = screen.Height;
        gridOverlay.GridSize = screen.GridSize;
    }
}
