using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Design;

/// <summary>
/// The design canvas. It is sized in design coordinates (DIPs) and never scales with the
/// window, and it renders entirely from a <see cref="ScreenDocument"/>. It reports what the
/// user asks for through events and never changes the document itself.
/// </summary>
internal sealed class DesignSurface : Grid
{
    /// <summary>Drag-and-drop format carrying a <see cref="ControlType"/> name.</summary>
    public const string ControlTypeDataFormat = "StandaloneUiBuilder.ControlType";

    private static readonly Brush SelectionBrush = CreateFrozenBrush(Color.FromRgb(0x1E, 0x6F, 0xE0));

    private readonly GridOverlay gridOverlay = new();
    private readonly Canvas controlsLayer = new() { ClipToBounds = true };
    private readonly Canvas adornerLayer = new();
    private readonly Rectangle selectionOutline = new()
    {
        Stroke = SelectionBrush,
        StrokeThickness = 1,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };

    private readonly Dictionary<Guid, Border> hosts = [];
    private ScreenDocument screen = new();
    private Guid? selectedId;

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
        Children.Add(gridOverlay);
        Children.Add(controlsLayer);
        Children.Add(adornerLayer);
    }

    /// <summary>The user clicked a control.</summary>
    public event EventHandler<Guid>? ControlClicked;

    /// <summary>The user clicked blank canvas at a point in design coordinates.</summary>
    public event EventHandler<Point>? BlankClicked;

    /// <summary>A toolbox item was dropped on the surface at a point in design coordinates.</summary>
    public event EventHandler<ControlDropEventArgs>? ControlDropped;

    public void Render(ScreenDocument screen, Guid? selectedId)
    {
        this.screen = screen;
        Width = screen.Width;
        Height = screen.Height;
        gridOverlay.GridSize = screen.GridSize;

        controlsLayer.Children.Clear();
        hosts.Clear();
        foreach (var control in screen.Controls)
        {
            var host = CreateDesignHost(control);
            hosts[control.Id] = host;
            controlsLayer.Children.Add(host);
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
            Width = control.Width,
            Height = control.Height,
            Cursor = Cursors.SizeAll,
            ToolTip = control.Name,
        };
        Canvas.SetLeft(host, control.X);
        Canvas.SetTop(host, control.Y);
        return host;
    }

    private void UpdateAdorners()
    {
        var control = selectedId is { } id ? screen.Controls.Find(c => c.Id == id) : null;
        if (control is null)
        {
            selectionOutline.Visibility = Visibility.Collapsed;
            return;
        }

        // The outline sits just outside the control so it does not cover its edges.
        selectionOutline.Width = control.Width + 2;
        selectionOutline.Height = control.Height + 2;
        Canvas.SetLeft(selectionOutline, control.X - 1);
        Canvas.SetTop(selectionOutline, control.Y - 1);
        selectionOutline.Visibility = Visibility.Visible;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();

        if (FindHost(e.OriginalSource as DependencyObject)?.Tag is Guid id)
        {
            ControlClicked?.Invoke(this, id);
        }
        else
        {
            BlankClicked?.Invoke(this, e.GetPosition(controlsLayer));
        }

        e.Handled = true;
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        e.Effects = TryGetDroppedType(e.Data, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        if (TryGetDroppedType(e.Data, out var type))
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

    private Border? FindHost(DependencyObject? source)
    {
        for (var current = source; current is not null && current != this; current = GetParent(current))
        {
            if (current is Border { Tag: Guid } border && border.Parent == controlsLayer)
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
