using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Design;

/// <summary>
/// The design canvas. It is sized in design coordinates (DIPs) and never scales with the
/// window, and it renders entirely from a <see cref="ScreenDocument"/>. It reports what the
/// user asks for through events and never changes the document itself. While a move or
/// resize is in progress it only moves the visuals; the result is reported once, on release.
/// </summary>
internal sealed class DesignSurface : Grid
{
    /// <summary>Drag-and-drop format carrying a <see cref="ControlType"/> name.</summary>
    public const string ControlTypeDataFormat = "StandaloneUiBuilder.ControlType";

    // The visible handle is small; the area that responds to the pointer is larger so it is
    // easy to hit at 100% and 150% display scaling.
    private const double HandleVisualSize = 8;
    private const double HandleHitSize = 12;

    // Below this size the middle-of-edge handles are hidden so small controls keep an area
    // that can be grabbed to move them.
    private const int MidHandleMinimumSpan = 40;

    private static readonly Brush SelectionBrush = CreateFrozenBrush(Color.FromRgb(0x1E, 0x6F, 0xE0));

    private static readonly (ResizeEdges Edges, Cursor Cursor)[] HandleLayout =
    [
        (ResizeEdges.Left | ResizeEdges.Top, Cursors.SizeNWSE),
        (ResizeEdges.Top, Cursors.SizeNS),
        (ResizeEdges.Right | ResizeEdges.Top, Cursors.SizeNESW),
        (ResizeEdges.Right, Cursors.SizeWE),
        (ResizeEdges.Right | ResizeEdges.Bottom, Cursors.SizeNWSE),
        (ResizeEdges.Bottom, Cursors.SizeNS),
        (ResizeEdges.Left | ResizeEdges.Bottom, Cursors.SizeNESW),
        (ResizeEdges.Left, Cursors.SizeWE),
    ];

    private static readonly Brush AnchorBrush = CreateFrozenBrush(Color.FromRgb(0x1E, 0x6F, 0xE0));

    private readonly GridOverlay gridOverlay = new();
    private readonly Canvas controlsLayer = new() { ClipToBounds = true };

    // Preview lays controls out the way the generated WPF window does (alignment and margins),
    // so resizing the preview shows how anchored controls move and stretch.
    private readonly Grid previewLayer = new() { ClipToBounds = true };
    private readonly Thumb resizeGrip = new()
    {
        Width = 14,
        Height = 14,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom,
        Cursor = Cursors.SizeNWSE,
        ToolTip = "Drag to resize the preview",
        Visibility = Visibility.Collapsed,
    };

    private readonly Dictionary<AnchorEdges, Line> anchorLines = [];
    private readonly Canvas adornerLayer = new();
    private readonly Rectangle selectionOutline = new()
    {
        Stroke = SelectionBrush,
        StrokeThickness = 1,
        IsHitTestVisible = false,
    };

    private readonly List<Border> handles = [];
    private readonly Dictionary<Guid, Border> hosts = [];
    private ScreenDocument screen = new();
    private Guid? selectedId;
    private bool isPreview;

    private DragMode dragMode;
    private Guid dragId;
    private Point dragOrigin;
    private ControlBounds dragStart;
    private ControlBounds dragCurrent;
    private ResizeEdges dragEdges;

    public DesignSurface()
    {
        Background = Brushes.White;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Focusable = true;
        FocusVisualStyle = null;
        AllowDrop = true;

        // Focusing the surface would otherwise scroll the whole canvas into view and make the
        // view jump when the user clicks while scrolled.
        RequestBringIntoView += (_, e) => e.Handled = e.TargetObject == this;

        adornerLayer.Children.Add(selectionOutline);
        foreach (var (edges, cursor) in HandleLayout)
        {
            var handle = new Border
            {
                Width = HandleHitSize,
                Height = HandleHitSize,
                Background = Brushes.Transparent,
                Cursor = cursor,
                Tag = edges,
                Child = new Rectangle
                {
                    Width = HandleVisualSize,
                    Height = HandleVisualSize,
                    Fill = Brushes.White,
                    Stroke = SelectionBrush,
                    StrokeThickness = 1,
                },
            };
            handles.Add(handle);
            adornerLayer.Children.Add(handle);
        }

        foreach (var edge in new[] { AnchorEdges.Left, AnchorEdges.Top, AnchorEdges.Right, AnchorEdges.Bottom })
        {
            var line = new Line
            {
                Stroke = AnchorBrush,
                StrokeThickness = 1,
                StrokeDashArray = [3, 2],
                IsHitTestVisible = false,
                SnapsToDevicePixels = true,
            };
            anchorLines[edge] = line;
            adornerLayer.Children.Insert(0, line);
        }

        resizeGrip.DragDelta += ResizeGrip_DragDelta;
        System.Windows.Automation.AutomationProperties.SetName(resizeGrip, "Resize preview");

        Children.Add(gridOverlay);
        Children.Add(controlsLayer);
        Children.Add(previewLayer);
        Children.Add(adornerLayer);
        Children.Add(resizeGrip);
        UpdateAdorners();
    }

    private enum DragMode
    {
        None,
        Pending,
        Move,
        Resize,
    }

    /// <summary>The user clicked a control.</summary>
    public event EventHandler<Guid>? ControlClicked;

    /// <summary>The user clicked blank canvas at a point in design coordinates.</summary>
    public event EventHandler<Point>? BlankClicked;

    /// <summary>A toolbox item was dropped on the surface at a point in design coordinates.</summary>
    public event EventHandler<ControlDropEventArgs>? ControlDropped;

    /// <summary>A move or resize finished with new bounds for a control.</summary>
    public event EventHandler<BoundsChangedEventArgs>? BoundsCommitted;

    /// <summary>A move or resize is in progress; for live feedback such as the status bar.</summary>
    public event EventHandler<BoundsChangedEventArgs>? BoundsChanging;

    /// <summary>
    /// Rebuilds the surface from the document. In Preview the grid and selection are hidden
    /// and the controls respond to input; their state is thrown away on the next render.
    /// </summary>
    public void Render(ScreenDocument screen, Guid? selectedId, bool isPreview = false, Action<ControlDocument>? buttonClicked = null)
    {
        CancelDrag();

        this.screen = screen;
        this.isPreview = isPreview;
        Width = screen.Width;
        Height = screen.Height;
        gridOverlay.GridSize = screen.GridSize;
        gridOverlay.Visibility = isPreview ? Visibility.Collapsed : Visibility.Visible;
        adornerLayer.Visibility = isPreview ? Visibility.Collapsed : Visibility.Visible;
        AllowDrop = !isPreview;
        resizeGrip.Visibility = isPreview && AnchorLayout.IsResizable(screen) ? Visibility.Visible : Visibility.Collapsed;

        controlsLayer.Children.Clear();
        previewLayer.Children.Clear();
        hosts.Clear();
        foreach (var control in screen.Controls)
        {
            var host = isPreview ? CreatePreviewHost(screen, control, buttonClicked) : CreateDesignHost(control);
            hosts[control.Id] = host;
            (isPreview ? previewLayer.Children : controlsLayer.Children).Add(host);
        }

        SetSelection(selectedId);
    }

    public void SetSelection(Guid? id)
    {
        selectedId = id is { } value && hosts.ContainsKey(value) ? value : null;
        UpdateAdorners();
    }

    private Border CreateDesignHost(ControlDocument control)
    {
        var element = ControlFactory.Create(control, buttonClicked: null);

        // In Design mode the control is display-only: the transparent host takes the pointer.
        element.IsHitTestVisible = false;
        element.Focusable = false;
        KeyboardNavigation.SetIsTabStop(element, false);

        var host = new Border
        {
            Background = Brushes.Transparent,
            Child = element,
            Tag = control.Id,
            Cursor = Cursors.SizeAll,
            ToolTip = control.Name,
        };
        PlaceHost(host, element, control.Bounds);
        return host;
    }

    private static Border CreatePreviewHost(ScreenDocument screen, ControlDocument control, Action<ControlDocument>? buttonClicked)
    {
        var element = ControlFactory.Create(control, buttonClicked);

        // The host is placed like the generated WPF control; the control fills the host.
        element.ClearValue(WidthProperty);
        element.ClearValue(HeightProperty);
        var placement = AnchorLayout.Place(screen, control);
        return new Border
        {
            Child = element,
            Tag = control.Id,
            HorizontalAlignment = ToWpf(placement.Horizontal, HorizontalAlignment.Left, HorizontalAlignment.Right, HorizontalAlignment.Stretch),
            VerticalAlignment = ToWpf(placement.Vertical, VerticalAlignment.Top, VerticalAlignment.Bottom, VerticalAlignment.Stretch),
            Margin = new Thickness(
                placement.Horizontal == AxisAlignment.End ? 0 : placement.MarginLeft,
                placement.Vertical == AxisAlignment.End ? 0 : placement.MarginTop,
                placement.Horizontal == AxisAlignment.Start ? 0 : placement.MarginRight,
                placement.Vertical == AxisAlignment.Start ? 0 : placement.MarginBottom),
            Width = placement.Width ?? double.NaN,
            Height = placement.Height ?? double.NaN,
        };
    }

    private static T ToWpf<T>(AxisAlignment alignment, T start, T end, T stretch) => alignment switch
    {
        AxisAlignment.Start => start,
        AxisAlignment.End => end,
        _ => stretch,
    };

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        // The preview can grow beyond the design size but not shrink below it, like the
        // generated window's content.
        Width = Math.Max(screen.Width, Width + e.HorizontalChange);
        Height = Math.Max(screen.Height, Height + e.VerticalChange);
    }

    private static void PlaceHost(Border host, FrameworkElement element, ControlBounds bounds)
    {
        Canvas.SetLeft(host, bounds.X);
        Canvas.SetTop(host, bounds.Y);
        host.Width = element.Width = bounds.Width;
        host.Height = element.Height = bounds.Height;
    }

    private ControlBounds? SelectedBounds() =>
        selectedId is not { } id ? null
        : dragMode is DragMode.Move or DragMode.Resize && dragId == id ? dragCurrent
        : screen.Controls.Find(c => c.Id == id)?.Bounds;

    private void UpdateAdorners()
    {
        var visibility = SelectedBounds() is null ? Visibility.Collapsed : Visibility.Visible;
        selectionOutline.Visibility = visibility;
        foreach (var handle in handles)
        {
            handle.Visibility = visibility;
        }

        foreach (var line in anchorLines.Values)
        {
            line.Visibility = Visibility.Collapsed;
        }

        if (SelectedBounds() is not { } bounds)
        {
            return;
        }

        // Dashed lines from the control to each screen edge it is anchored to.
        var anchor = screen.Controls.Find(c => c.Id == selectedId)?.Anchor ?? AnchorEdges.Default;
        var midX = bounds.X + bounds.Width / 2.0;
        var midY = bounds.Y + bounds.Height / 2.0;
        SetAnchorLine(AnchorEdges.Left, anchor, 0, midY, bounds.X, midY);
        SetAnchorLine(AnchorEdges.Right, anchor, bounds.Right, midY, screen.Width, midY);
        SetAnchorLine(AnchorEdges.Top, anchor, midX, 0, midX, bounds.Y);
        SetAnchorLine(AnchorEdges.Bottom, anchor, midX, bounds.Bottom, midX, screen.Height);

        // The outline sits just outside the control so it does not cover its edges.
        selectionOutline.Width = bounds.Width + 2;
        selectionOutline.Height = bounds.Height + 2;
        Canvas.SetLeft(selectionOutline, bounds.X - 1);
        Canvas.SetTop(selectionOutline, bounds.Y - 1);

        foreach (var handle in handles)
        {
            var edges = (ResizeEdges)handle.Tag;
            var isHorizontalMid = !edges.HasFlag(ResizeEdges.Left) && !edges.HasFlag(ResizeEdges.Right);
            var isVerticalMid = !edges.HasFlag(ResizeEdges.Top) && !edges.HasFlag(ResizeEdges.Bottom);
            handle.Visibility = (isHorizontalMid && bounds.Width < MidHandleMinimumSpan)
                || (isVerticalMid && bounds.Height < MidHandleMinimumSpan)
                ? Visibility.Collapsed
                : Visibility.Visible;

            var x = edges.HasFlag(ResizeEdges.Left) ? bounds.X
                : edges.HasFlag(ResizeEdges.Right) ? bounds.Right
                : bounds.X + bounds.Width / 2.0;
            var y = edges.HasFlag(ResizeEdges.Top) ? bounds.Y
                : edges.HasFlag(ResizeEdges.Bottom) ? bounds.Bottom
                : bounds.Y + bounds.Height / 2.0;
            Canvas.SetLeft(handle, x - HandleHitSize / 2);
            Canvas.SetTop(handle, y - HandleHitSize / 2);
        }
    }

    private void SetAnchorLine(AnchorEdges edge, AnchorEdges anchor, double x1, double y1, double x2, double y2)
    {
        var line = anchorLines[edge];
        line.X1 = x1;
        line.Y1 = y1;
        line.X2 = x2;
        line.Y2 = y2;
        line.Visibility = anchor.HasFlag(edge) ? Visibility.Visible : Visibility.Collapsed;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (isPreview)
        {
            // Preview controls handle their own input.
            return;
        }

        Focus();
        e.Handled = true;

        var source = e.OriginalSource as DependencyObject;
        if (FindAncestor(source, b => b.Tag is ResizeEdges) is { } handle && selectedId is { } selected
            && screen.Controls.Find(c => c.Id == selected) is { } control)
        {
            BeginDrag(DragMode.Resize, control, e);
            dragEdges = (ResizeEdges)handle.Tag;
            return;
        }

        if (FindAncestor(source, b => b.Tag is Guid && b.Parent == controlsLayer)?.Tag is Guid id)
        {
            ControlClicked?.Invoke(this, id);
            if (screen.Controls.Find(c => c.Id == id) is { } clicked)
            {
                BeginDrag(DragMode.Pending, clicked, e);
            }

            return;
        }

        BlankClicked?.Invoke(this, e.GetPosition(controlsLayer));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragMode == DragMode.None || !hosts.TryGetValue(dragId, out var host))
        {
            return;
        }

        var delta = e.GetPosition(controlsLayer) - dragOrigin;
        if (dragMode == DragMode.Pending)
        {
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            dragMode = DragMode.Move;
        }

        var next = dragMode == DragMode.Move
            ? DesignGeometry.Move(screen, dragStart, delta.X, delta.Y)
            : DesignGeometry.Resize(screen, ControlCatalog.Get(TypeOf(dragId)), dragStart, dragEdges, delta.X, delta.Y);

        if (next != dragCurrent)
        {
            dragCurrent = next;
            PlaceHost(host, (FrameworkElement)host.Child, next);
            UpdateAdorners();
            BoundsChanging?.Invoke(this, new BoundsChangedEventArgs(dragId, next));
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (dragMode == DragMode.None)
        {
            return;
        }

        var (mode, id, start, end) = (dragMode, dragId, dragStart, dragCurrent);
        dragMode = DragMode.None;
        ReleaseMouseCapture();

        if (mode is DragMode.Move or DragMode.Resize && end != start)
        {
            BoundsCommitted?.Invoke(this, new BoundsChangedEventArgs(id, end));
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);

        // Capture lost mid-drag (for example by switching windows): abandon the drag and put
        // the visuals back where the document says they are.
        CancelDrag();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && dragMode != DragMode.None)
        {
            CancelDrag();
            e.Handled = true;
        }
    }

    private void BeginDrag(DragMode mode, ControlDocument control, MouseEventArgs e)
    {
        dragMode = mode;
        dragId = control.Id;
        dragOrigin = e.GetPosition(controlsLayer);
        dragStart = dragCurrent = control.Bounds;
        CaptureMouse();
    }

    private void CancelDrag()
    {
        if (dragMode == DragMode.None)
        {
            return;
        }

        var id = dragId;
        dragMode = DragMode.None;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        if (hosts.TryGetValue(id, out var host) && screen.Controls.Find(c => c.Id == id) is { } control)
        {
            PlaceHost(host, (FrameworkElement)host.Child, control.Bounds);
        }

        UpdateAdorners();
    }

    private ControlType TypeOf(Guid id) => screen.Controls.Find(c => c.Id == id)!.Type;

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        e.Effects = !isPreview && TryGetDroppedType(e.Data, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        if (!isPreview && TryGetDroppedType(e.Data, out var type))
        {
            var position = e.GetPosition(controlsLayer);
            ControlDropped?.Invoke(this, new ControlDropEventArgs(type, position.X, position.Y));
            e.Handled = true;
        }
    }

    private static bool TryGetDroppedType(IDataObject data, out ControlType type)
    {
        type = default;
        return data.GetDataPresent(ControlTypeDataFormat)
            && data.GetData(ControlTypeDataFormat) is string name
            && Enum.TryParse(name, out type);
    }

    private Border? FindAncestor(DependencyObject? source, Func<Border, bool> match)
    {
        for (var current = source; current is not null && current != this; current = GetParent(current))
        {
            if (current is Border border && match(border))
            {
                return border;
            }
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject child) =>
        child is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(child)
            : LogicalTreeHelper.GetParent(child);

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

internal sealed record ControlDropEventArgs(ControlType Type, double X, double Y);

internal sealed record BoundsChangedEventArgs(Guid Id, ControlBounds Bounds);
