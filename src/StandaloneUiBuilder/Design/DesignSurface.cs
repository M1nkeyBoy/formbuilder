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

    private bool showGrid = true;

    /// <summary>Whether the design grid is drawn; controls snap to it either way.</summary>
    public bool ShowGrid
    {
        get => showGrid;
        set
        {
            showGrid = value;
            gridOverlay.Visibility = isPreview || !showGrid ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>The surface appears to UI Automation as "Surface", so tools can find where the design is drawn.</summary>
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() =>
        new System.Windows.Automation.Peers.FrameworkElementAutomationPeer(this);

    // A dark window's background in WPF's Fluent theme.
    private static readonly Brush DarkBackground = CreateFrozenBrush(Color.FromRgb(0x20, 0x20, 0x20));

    // The theme dictionary the controls are drawn with; null for WPF's usual look.
    private Uri? shownThemeSource;

    private readonly GridOverlay gridOverlay = new();
    private readonly Canvas controlsLayer = new() { ClipToBounds = true };

    // Preview lays controls out the way the generated WPF window does (alignment and margins),
    // so resizing the preview shows how anchored controls move and stretch.
    // Its own tab scope: the Preview's tab order is among its controls, not the editor's.
    private readonly Grid previewLayer = CreatePreviewLayer();

    private readonly List<Border> tabBadges = [];
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

    // Selection box drawn while dragging across blank canvas.
    private readonly Rectangle selectionBand = new()
    {
        Stroke = SelectionBrush,
        StrokeThickness = 1,
        StrokeDashArray = [4, 2],
        Fill = CreateFrozenBrush(Color.FromArgb(0x18, 0x1E, 0x6F, 0xE0)),
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };

    // Shows which container a dragged control will be dropped into.
    private readonly Rectangle dropTargetOutline = new()
    {
        Stroke = CreateFrozenBrush(Color.FromRgb(0x10, 0x9B, 0x5A)),
        StrokeThickness = 2,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };

    private readonly List<Rectangle> extraOutlines = [];
    private Dictionary<Guid, PlacedControl> placed = [];
    private readonly List<Border> handles = [];
    private readonly Dictionary<Guid, Border> hosts = [];
    private ScreenDocument screen = new();
    private List<Guid> selection = [];
    private bool isPreview;

    private DragMode dragMode;
    private Guid dragId;
    private ModifierKeys dragModifiers;
    private Point dragOrigin;
    private Point dragPoint;
    private ControlBounds dragStart;
    private ControlBounds dragCurrent;
    private ResizeEdges dragEdges;

    // Group move: starting bounds of every control that moves (the selected controls on the
    // screen and everything inside them), and the offset applied so far. A control dragged
    // out of a container moves freely (unsnapped) until it is dropped.
    private Dictionary<Guid, ControlBounds> groupStarts = [];
    private List<Guid> movingRoots = [];
    private (int X, int Y) groupOffset;
    private bool draggingChild;
    private Guid? dropTargetId;

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
        adornerLayer.Children.Add(selectionBand);
        adornerLayer.Children.Add(dropTargetOutline);
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
        BandPending,
        Band,
    }

    /// <summary>The user pressed the mouse on a control (with any modifier keys held).</summary>
    public event EventHandler<ControlClickEventArgs>? ControlClicked;

    /// <summary>The user pressed the mouse on one of a TabControl's tabs.</summary>
    public event EventHandler<TabClickEventArgs>? TabClicked;

    /// <summary>The user clicked a control and released without dragging it.</summary>
    public event EventHandler<ControlClickEventArgs>? ControlClickCompleted;

    /// <summary>The user clicked blank canvas at a point in design coordinates.</summary>
    public event EventHandler<Point>? BlankClicked;

    /// <summary>The user dragged a selection box across the canvas.</summary>
    public event EventHandler<BandSelectedEventArgs>? BandSelected;

    /// <summary>A drag moved the selected controls on the screen by an offset.</summary>
    public event EventHandler<MoveCommittedEventArgs>? MoveCommitted;

    /// <summary>
    /// A dragged control was dropped into a container (ContainerId set, Point is the pointer)
    /// or out of its container onto the screen (ContainerId null, Point is its new top-left).
    /// </summary>
    public event EventHandler<ReparentEventArgs>? ReparentRequested;

    /// <summary>An arrow key asked to move the selection by an offset.</summary>
    public event EventHandler<MoveCommittedEventArgs>? NudgeRequested;

    /// <summary>A toolbox item was dropped on the surface at a point in design coordinates.</summary>
    public event EventHandler<ControlDropEventArgs>? ControlDropped;

    /// <summary>A resize finished with new bounds for a control.</summary>
    public event EventHandler<BoundsChangedEventArgs>? BoundsCommitted;

    /// <summary>A move or resize is in progress; for live feedback such as the status bar.</summary>
    public event EventHandler<BoundsChangedEventArgs>? BoundsChanging;

    /// <summary>
    /// Rebuilds the surface from the document. In Preview the grid and selection are hidden
    /// and the controls respond to input; their state is thrown away on the next render.
    /// </summary>
    public void Render(ScreenDocument screen, IReadOnlyCollection<Guid> selection, bool isPreview = false, Action<ControlDocument>? buttonClicked = null, ProjectTheme theme = ProjectTheme.Light)
    {
        CancelDrag();
        ApplyTheme(theme);

        // Drawn as the generated windows show it: text on the design's own backgrounds stays readable.
        screen = ThemeContrast.Apply(theme, screen);

        this.screen = screen;
        this.isPreview = isPreview;
        Width = screen.Width;
        Height = screen.Height;
        gridOverlay.GridSize = screen.GridSize;
        gridOverlay.Visibility = isPreview || !showGrid ? Visibility.Collapsed : Visibility.Visible;
        adornerLayer.Visibility = isPreview ? Visibility.Collapsed : Visibility.Visible;
        AllowDrop = !isPreview;
        resizeGrip.Visibility = isPreview && AnchorLayout.IsResizable(screen) ? Visibility.Visible : Visibility.Collapsed;

        controlsLayer.Children.Clear();
        previewLayer.Children.Clear();
        hosts.Clear();
        placed = ContainerLayout.Flatten(screen).ToDictionary(p => p.Control.Id);
        if (isPreview)
        {
            // Containers bring their children with them, as real WPF panels.
            foreach (var control in screen.Controls)
            {
                var host = CreatePreviewHost(screen, control, buttonClicked);
                hosts[control.Id] = host;
                previewLayer.Children.Add(host);
            }

            ApplyTabOrder(screen);
        }
        else
        {
            // Every control gets its own host at its screen position, containers before what
            // they hold, so the innermost control under the pointer is the one clicked.
            foreach (var item in placed.Values)
            {
                // Pages behind the shown tab, and what they hold, are not drawn.
                if (item.IsHidden)
                {
                    continue;
                }

                var host = CreateDesignHost(item.Control, item.Bounds);
                ClipToContainers(host, item);
                hosts[item.Control.Id] = host;
                controlsLayer.Children.Add(host);
            }
        }

        SetSelection(selection);
    }

    /// <summary>
    /// Draws the controls in the project's theme, as the generated WPF window does: the usual
    /// look for Light, and WPF's Fluent styles in dark (or in the colours Windows is set to
    /// use, for System).
    /// </summary>
    private void ApplyTheme(ProjectTheme theme)
    {
        var dark = theme == ProjectTheme.Dark || (theme == ProjectTheme.System && WindowsAppsUseDarkMode());
        var source = theme == ProjectTheme.Light
            ? null
            : new Uri($"pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.{(dark ? "Dark" : "Light")}.xaml", UriKind.Absolute);
        if (source == shownThemeSource)
        {
            return;
        }

        shownThemeSource = source;
        foreach (var layer in new Panel[] { controlsLayer, previewLayer })
        {
            layer.Resources.MergedDictionaries.Clear();
            if (source is not null)
            {
                layer.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = source });
            }
        }

        Background = dark ? DarkBackground : Brushes.White;
        gridOverlay.IsDark = dark;
    }

    /// <summary>Whether Windows is set to show apps in dark mode.</summary>
    private static bool WindowsAppsUseDarkMode()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }

    private static Grid CreatePreviewLayer()
    {
        var layer = new Grid { ClipToBounds = true };
        KeyboardNavigation.SetTabNavigation(layer, KeyboardNavigationMode.Local);
        return layer;
    }

    /// <summary>
    /// With a tab order set on the screen, each Preview control Tab visits gets its place in it,
    /// as in the generated WPF window.
    /// </summary>
    private void ApplyTabOrder(ScreenDocument screen)
    {
        if (screen.TabOrder is null)
        {
            return;
        }

        var sequence = TabSequence.Resolve(screen);
        for (var i = 0; i < sequence.Count; i++)
        {
            if (LogicalTreeHelper.FindLogicalNode(previewLayer, sequence[i].Name) is DependencyObject element)
            {
                KeyboardNavigation.SetTabIndex(element, i);
                if (sequence[i].Type is ControlType.DatePicker or ControlType.TabControl)
                {
                    KeyboardNavigation.SetTabNavigation(element, KeyboardNavigationMode.Local);
                }
            }
        }
    }

    /// <summary>
    /// Numbers each control Tab visits with its place in the tab order, or removes the numbers
    /// (null). Numbers up to <paramref name="assigned"/> are drawn as already set.
    /// </summary>
    public void ShowTabOrder(IReadOnlyList<Guid>? order, int assigned = 0)
    {
        foreach (var badge in tabBadges)
        {
            adornerLayer.Children.Remove(badge);
        }

        tabBadges.Clear();
        if (order is null || isPreview)
        {
            return;
        }

        for (var i = 0; i < order.Count; i++)
        {
            // Controls on hidden tab pages are not drawn, so neither are their numbers.
            if (!hosts.ContainsKey(order[i]) || !placed.TryGetValue(order[i], out var item))
            {
                continue;
            }

            var badge = new Border
            {
                Background = i < assigned ? new SolidColorBrush(Color.FromRgb(0x1B, 0x7F, 0x3B)) : new SolidColorBrush(Color.FromRgb(0x1E, 0x6F, 0xD9)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 0, 4, 1),
                IsHitTestVisible = false,
                Child = new TextBlock { Text = (i + 1).ToString(System.Globalization.CultureInfo.CurrentCulture), Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.Bold },
                Tag = "TabBadge",
            };
            System.Windows.Automation.AutomationProperties.SetName(badge, $"Tab {i + 1}: {item.Control.Name}");
            Canvas.SetLeft(badge, item.Bounds.X);
            Canvas.SetTop(badge, item.Bounds.Y);
            tabBadges.Add(badge);
            adornerLayer.Children.Add(badge);
        }
    }

    /// <summary>Shows a selection. With one control, it also gets resize handles and anchor lines.</summary>
    public void SetSelection(IReadOnlyCollection<Guid> ids)
    {
        selection = ids.Where(hosts.ContainsKey).ToList();
        UpdateAdorners();
    }

    private Guid? SingleSelection => selection.Count == 1 ? selection[0] : null;

    private Border CreateDesignHost(ControlDocument control, ControlBounds bounds)
    {
        var element = control.Children is not null
            ? ControlFactory.CreateDesignContainer(control)
            : ControlFactory.Create(control, buttonClicked: null);

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
        PlaceHost(host, element, bounds);
        return host;
    }

    /// <summary>
    /// Like the real containers, a control is only visible within its container (and theirs):
    /// children that overflow a stack are clipped rather than drawn outside it.
    /// </summary>
    private void ClipToContainers(Border host, PlacedControl item)
    {
        var visible = new Rect(item.Bounds.X, item.Bounds.Y, item.Bounds.Width, item.Bounds.Height);
        for (var parentId = item.ParentId; parentId is { } id && placed.TryGetValue(id, out var parent); parentId = parent.ParentId)
        {
            visible.Intersect(new Rect(parent.Bounds.X, parent.Bounds.Y, parent.Bounds.Width, parent.Bounds.Height));
        }

        if (visible.IsEmpty)
        {
            host.Visibility = Visibility.Collapsed;
        }
        else if (visible.Width < item.Bounds.Width || visible.Height < item.Bounds.Height)
        {
            host.Clip = new RectangleGeometry(new Rect(visible.X - item.Bounds.X, visible.Y - item.Bounds.Y, visible.Width, visible.Height));
        }
    }

    /// <summary>The index of the tab under the pointer, when a TabControl is clicked on one.</summary>
    private int? TabAt(Guid id, MouseButtonEventArgs e)
    {
        if (!hosts.TryGetValue(id, out var host) || ControlFactory.FindTabControl(host.Child) is not { } tabs)
        {
            return null;
        }

        for (var i = 0; i < tabs.Items.Count; i++)
        {
            if (tabs.Items[i] is TabItem item && item.IsVisible
                && item.TransformToAncestor(host).TransformBounds(new Rect(item.RenderSize)).Contains(e.GetPosition(host)))
            {
                return i;
            }
        }

        return null;
    }

    private bool IsRoot(Guid id) => placed.TryGetValue(id, out var item) && item.ParentId is null;

    private static Border CreatePreviewHost(ScreenDocument screen, ControlDocument control, Action<ControlDocument>? buttonClicked)
    {
        var element = ControlFactory.Create(control, buttonClicked);

        // Each control on the screen has its own host here, but in a generated window they
        // share one parent, so RadioButtons on the screen form one group.
        if (element is RadioButton radio)
        {
            radio.GroupName = "Screen";
        }

        // The host is placed like the generated WPF control; the control fills the host (set
        // here, since some Fluent styles centre a control instead).
        element.ClearValue(WidthProperty);
        element.ClearValue(HeightProperty);
        element.HorizontalAlignment = HorizontalAlignment.Stretch;
        element.VerticalAlignment = VerticalAlignment.Stretch;
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

    /// <summary>Where a control is drawn right now, including an unfinished drag.</summary>
    private ControlBounds? CurrentBounds(Guid id)
    {
        if (dragMode == DragMode.Resize && dragId == id)
        {
            return dragCurrent;
        }

        if (dragMode == DragMode.Move && groupStarts.TryGetValue(id, out var start))
        {
            return start with { X = start.X + groupOffset.X, Y = start.Y + groupOffset.Y };
        }

        return placed.TryGetValue(id, out var item) ? item.Bounds : null;
    }

    private ControlBounds? SelectedBounds() => SingleSelection is { } id ? CurrentBounds(id) : null;

    private void UpdateAdorners()
    {
        // Several selected controls: an outline each, no handles.
        var multi = selection.Count > 1 ? selection.Select(CurrentBounds).OfType<ControlBounds>().ToList() : [];
        while (extraOutlines.Count < multi.Count)
        {
            var outline = new Rectangle { Stroke = SelectionBrush, StrokeThickness = 1, IsHitTestVisible = false };
            extraOutlines.Add(outline);
            adornerLayer.Children.Add(outline);
        }

        for (var i = 0; i < extraOutlines.Count; i++)
        {
            var outline = extraOutlines[i];
            outline.Visibility = i < multi.Count ? Visibility.Visible : Visibility.Collapsed;
            if (i < multi.Count)
            {
                PlaceOutline(outline, multi[i]);
            }
        }

        var visibility = SelectedBounds() is null ? Visibility.Collapsed : Visibility.Visible;
        selectionOutline.Visibility = visibility;

        // Resize handles and anchor lines belong to controls placed directly on the screen.
        var onScreen = SingleSelection is { } single && IsRoot(single);
        foreach (var handle in handles)
        {
            handle.Visibility = onScreen ? visibility : Visibility.Collapsed;
        }

        foreach (var line in anchorLines.Values)
        {
            line.Visibility = Visibility.Collapsed;
        }

        if (SelectedBounds() is not { } bounds)
        {
            return;
        }

        PlaceOutline(selectionOutline, bounds);
        if (!onScreen)
        {
            return;
        }

        // Dashed lines from the control to each screen edge it is anchored to.
        var anchor = screen.Controls.Find(c => c.Id == SingleSelection)?.Anchor ?? AnchorEdges.Default;
        var midX = bounds.X + bounds.Width / 2.0;
        var midY = bounds.Y + bounds.Height / 2.0;
        SetAnchorLine(AnchorEdges.Left, anchor, 0, midY, bounds.X, midY);
        SetAnchorLine(AnchorEdges.Right, anchor, bounds.Right, midY, screen.Width, midY);
        SetAnchorLine(AnchorEdges.Top, anchor, midX, 0, midX, bounds.Y);
        SetAnchorLine(AnchorEdges.Bottom, anchor, midX, bounds.Bottom, midX, screen.Height);

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

    // The outline sits just outside the control so it does not cover its edges.
    private static void PlaceOutline(Rectangle outline, ControlBounds bounds)
    {
        outline.Width = bounds.Width + 2;
        outline.Height = bounds.Height + 2;
        Canvas.SetLeft(outline, bounds.X - 1);
        Canvas.SetTop(outline, bounds.Y - 1);
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
        dragModifiers = Keyboard.Modifiers;

        var source = e.OriginalSource as DependencyObject;
        if (FindAncestor(source, b => b.Tag is ResizeEdges) is { } handle && SingleSelection is { } selected
            && IsRoot(selected) && screen.Controls.Find(c => c.Id == selected) is { } control)
        {
            BeginDrag(DragMode.Resize, control.Id, e);
            dragStart = dragCurrent = control.Bounds;
            dragEdges = (ResizeEdges)handle.Tag;
            return;
        }

        if (FindAncestor(source, b => b.Tag is Guid && b.Parent == controlsLayer)?.Tag is Guid id)
        {
            // The window updates the selection first; a drag then moves whatever is selected.
            ControlClicked?.Invoke(this, new ControlClickEventArgs(id, dragModifiers, e.GetPosition(controlsLayer)));
            if (TabAt(id, e) is { } tab)
            {
                // Showing another tab redraws the surface; the TabControl stays selected.
                TabClicked?.Invoke(this, new TabClickEventArgs(id, tab));
                if (!hosts.ContainsKey(id))
                {
                    return;
                }
            }

            if (selection.Contains(id))
            {
                BeginDrag(DragMode.Pending, id, e);
            }

            return;
        }

        BeginDrag(DragMode.BandPending, Guid.Empty, e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragMode == DragMode.None)
        {
            return;
        }

        dragPoint = e.GetPosition(controlsLayer);
        var delta = dragPoint - dragOrigin;
        var moved = Math.Abs(delta.X) >= SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(delta.Y) >= SystemParameters.MinimumVerticalDragDistance;

        switch (dragMode)
        {
            case DragMode.Pending when moved:
                StartGroupMove();
                break;
            case DragMode.BandPending when moved:
                dragMode = DragMode.Band;
                selectionBand.Visibility = Visibility.Visible;
                break;
        }

        switch (dragMode)
        {
            case DragMode.Move:
                UpdateGroupMove(delta);
                break;
            case DragMode.Resize when hosts.TryGetValue(dragId, out var host):
                var next = DesignGeometry.Resize(screen, ControlCatalog.Get(TypeOf(dragId)), dragStart, dragEdges, delta.X, delta.Y);
                if (next != dragCurrent)
                {
                    dragCurrent = next;
                    PlaceHost(host, (FrameworkElement)host.Child, next);
                    UpdateAdorners();
                    BoundsChanging?.Invoke(this, new BoundsChangedEventArgs(dragId, next));
                }

                break;
            case DragMode.Band:
                var band = BandRect();
                Canvas.SetLeft(selectionBand, band.X);
                Canvas.SetTop(selectionBand, band.Y);
                selectionBand.Width = band.Width;
                selectionBand.Height = band.Height;
                break;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (dragMode == DragMode.None)
        {
            return;
        }

        var (mode, id, modifiers) = (dragMode, dragId, dragModifiers);
        var (start, end, offset) = (dragStart, dragCurrent, groupOffset);
        var (roots, child, target) = (movingRoots, draggingChild, dropTargetId);
        var band = BandRect();
        dragMode = DragMode.None;
        selectionBand.Visibility = Visibility.Collapsed;
        ReleaseMouseCapture();

        switch (mode)
        {
            case DragMode.Pending:
                ControlClickCompleted?.Invoke(this, new ControlClickEventArgs(id, modifiers, dragPoint));
                break;
            case DragMode.Move when target is { } containerId:
                ReparentRequested?.Invoke(this, new ReparentEventArgs(id, containerId, dragPoint));
                break;
            case DragMode.Move when child && offset != (0, 0) && groupStarts.TryGetValue(id, out var childStart):
                ReparentRequested?.Invoke(this, new ReparentEventArgs(id, null, new Point(childStart.X + offset.X, childStart.Y + offset.Y)));
                break;
            case DragMode.Move when !child && offset != (0, 0):
                MoveCommitted?.Invoke(this, new MoveCommittedEventArgs(roots, offset.X, offset.Y));
                break;
            case DragMode.Resize when end != start:
                BoundsCommitted?.Invoke(this, new BoundsChangedEventArgs(id, end));
                break;
            case DragMode.BandPending:
                BlankClicked?.Invoke(this, dragOrigin);
                break;
            case DragMode.Band:
                BandSelected?.Invoke(this, new BandSelectedEventArgs(
                    new ControlBounds((int)band.X, (int)band.Y, (int)Math.Ceiling(band.Width), (int)Math.Ceiling(band.Height)),
                    modifiers));
                break;
        }

        groupStarts = [];
        groupOffset = (0, 0);
        dropTargetId = null;
        dropTargetOutline.Visibility = Visibility.Collapsed;
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
            return;
        }

        if (isPreview || dragMode != DragMode.None || selection.Count == 0)
        {
            return;
        }

        // Arrow keys nudge the selection by 1 DIP, or by one grid step with Shift.
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? Math.Max(1, screen.GridSize) : 1;
        (int X, int Y)? nudge = e.Key switch
        {
            Key.Left => (-step, 0),
            Key.Right => (step, 0),
            Key.Up => (0, -step),
            Key.Down => (0, step),
            _ => null,
        };
        if (nudge is { } n)
        {
            NudgeRequested?.Invoke(this, new MoveCommittedEventArgs(selection, n.X, n.Y));
            e.Handled = true;
        }
    }

    private void BeginDrag(DragMode mode, Guid id, MouseEventArgs e)
    {
        dragMode = mode;
        dragId = id;
        dragOrigin = dragPoint = e.GetPosition(controlsLayer);
        groupStarts = [];
        groupOffset = (0, 0);
        CaptureMouse();
    }

    private void StartGroupMove()
    {
        dragMode = DragMode.Move;

        // A control inside a container is dragged on its own; controls on the screen move
        // together with the rest of the selection that is on the screen.
        draggingChild = !IsRoot(dragId);
        movingRoots = draggingChild ? [] : selection.Where(IsRoot).Append(dragId).Distinct().ToList();
        var moving = draggingChild ? [dragId] : movingRoots;
        groupStarts = moving
            .SelectMany(id => screen.Controls.Concat(ControlTree.All(screen.Controls)).Where(c => c.Id == id).Take(1))
            .SelectMany(c => ControlTree.All([c]))
            .Where(c => placed.ContainsKey(c.Id))
            .ToDictionary(c => c.Id, c => placed[c.Id].Bounds);

        // A control being dragged out of its container is shown whole, wherever it goes.
        foreach (var id in groupStarts.Keys)
        {
            if (hosts.TryGetValue(id, out var host))
            {
                host.Clip = null;
                host.Visibility = Visibility.Visible;
            }
        }
    }

    /// <summary>
    /// The control under the pointer snaps to the grid; the rest of the selection moves by the
    /// same offset, and the offset stops where any selected control would leave the screen.
    /// </summary>
    private void UpdateGroupMove(Vector delta)
    {
        if (!groupStarts.TryGetValue(dragId, out var primary))
        {
            return;
        }

        UpdateDropTarget();

        int dx, dy;
        if (draggingChild)
        {
            (dx, dy) = ((int)Math.Round(delta.X), (int)Math.Round(delta.Y));
        }
        else
        {
            var snapped = DesignGeometry.Move(screen, primary, delta.X, delta.Y);
            var starts = movingRoots.Select(id => groupStarts[id]).ToList();
            dx = Math.Clamp(snapped.X - primary.X, -starts.Min(b => b.X), screen.Width - starts.Max(b => b.Right));
            dy = Math.Clamp(snapped.Y - primary.Y, -starts.Min(b => b.Y), screen.Height - starts.Max(b => b.Bottom));
        }

        if ((dx, dy) == groupOffset)
        {
            return;
        }

        groupOffset = (dx, dy);
        foreach (var (id, start) in groupStarts)
        {
            if (hosts.TryGetValue(id, out var host))
            {
                PlaceHost(host, (FrameworkElement)host.Child, start with { X = start.X + dx, Y = start.Y + dy });
            }
        }

        UpdateAdorners();
        BoundsChanging?.Invoke(this, new BoundsChangedEventArgs(dragId, primary with { X = primary.X + dx, Y = primary.Y + dy }));
    }

    /// <summary>
    /// While one control is dragged, the container under the pointer (not the control itself or
    /// anything inside it) is where it would be dropped; it is outlined.
    /// </summary>
    private void UpdateDropTarget()
    {
        var single = draggingChild || movingRoots.Count == 1;
        var target = single ? ContainerLayout.ContainerAt(screen, dragPoint.X, dragPoint.Y, [dragId], placed[dragId].Control.Type) : null;
        dropTargetId = target?.Control.Id;
        dropTargetOutline.Visibility = target is null ? Visibility.Collapsed : Visibility.Visible;
        if (target is not null)
        {
            PlaceOutline(dropTargetOutline, target.Bounds);
        }
    }

    private Rect BandRect() => new(dragOrigin, dragPoint);

    private void CancelDrag()
    {
        if (dragMode == DragMode.None)
        {
            return;
        }

        var affected = groupStarts.Keys.Append(dragId).ToList();
        dragMode = DragMode.None;
        groupStarts = [];
        groupOffset = (0, 0);
        dropTargetId = null;
        dropTargetOutline.Visibility = Visibility.Collapsed;
        selectionBand.Visibility = Visibility.Collapsed;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        foreach (var id in affected)
        {
            if (hosts.TryGetValue(id, out var host) && placed.TryGetValue(id, out var item))
            {
                PlaceHost(host, (FrameworkElement)host.Child, item.Bounds);
            }
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

internal sealed record TabClickEventArgs(Guid TabControlId, int Index);

internal sealed record ControlClickEventArgs(Guid Id, ModifierKeys Modifiers, Point Point);

internal sealed record BandSelectedEventArgs(ControlBounds Area, ModifierKeys Modifiers);

internal sealed record MoveCommittedEventArgs(IReadOnlyCollection<Guid> Ids, int Dx, int Dy);

internal sealed record ReparentEventArgs(Guid Id, Guid? ContainerId, Point Point);
