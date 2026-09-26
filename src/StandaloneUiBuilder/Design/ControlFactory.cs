using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Design;

/// <summary>
/// Maps document control types to the WPF controls that display them. The WPF controls are
/// views of the document only; they are rebuilt from it and never saved.
/// Keep padding and alignment in step with WpfGenerator, so exported windows look the same.
/// </summary>
internal static class ControlFactory
{
    public static FrameworkElement Create(ControlDocument control, Action<ControlDocument>? buttonClicked)
    {
        var properties = control.Properties;
        FrameworkElement element = control.Type switch
        {
            // Text goes in a TextBlock so underscores are shown literally, not as access keys.
            ControlType.Label => new Label
            {
                Content = new TextBlock { Text = properties.Text ?? "" },
                Padding = new Thickness(2, 0, 2, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
            },
            ControlType.Button => CreateButton(control, buttonClicked),
            ControlType.TextBox when properties.IsMultiline == true => new TextBox
            {
                Text = properties.Text ?? "",
                VerticalContentAlignment = VerticalAlignment.Top,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
            ControlType.TextBox => new TextBox
            {
                Text = properties.Text ?? "",
                VerticalContentAlignment = VerticalAlignment.Center,
            },
            ControlType.PasswordBox => new PasswordBox { VerticalContentAlignment = VerticalAlignment.Center },
            ControlType.RadioButton => new RadioButton
            {
                Content = new TextBlock { Text = properties.Text ?? "" },
                IsChecked = properties.IsChecked ?? false,
                VerticalContentAlignment = VerticalAlignment.Center,
            },
            ControlType.ListBox => new ListBox { ItemsSource = properties.Items?.ToList() ?? [] },
            ControlType.Slider => new Slider
            {
                Minimum = properties.Minimum ?? 0,
                Maximum = properties.Maximum ?? 100,
                Value = properties.Value ?? 0,
                IsSnapToTickEnabled = true,
                TickFrequency = 1,
            },
            ControlType.ProgressBar => new ProgressBar
            {
                Minimum = properties.Minimum ?? 0,
                Maximum = properties.Maximum ?? 100,
                Value = properties.Value ?? 0,
            },
            ControlType.DatePicker => new DatePicker { VerticalContentAlignment = VerticalAlignment.Center },
            ControlType.Image => CreateImage(control),
            ControlType.CheckBox => new CheckBox
            {
                Content = new TextBlock { Text = properties.Text ?? "" },
                IsChecked = properties.IsChecked ?? false,
                VerticalContentAlignment = VerticalAlignment.Center,
            },
            ControlType.ComboBox => new ComboBox
            {
                // A copy, so interacting with the preview can never touch the document.
                ItemsSource = properties.Items?.ToList() ?? [],
                VerticalContentAlignment = VerticalAlignment.Center,
            },
            ControlType.StackPanel or ControlType.Grid => CreatePanel(control, buttonClicked),
            ControlType.GroupBox => CreateGroupBox(control, buttonClicked),
            ControlType.TabControl => CreateTabControl(control, buttonClicked),
            ControlType.TabPage => CreatePanel(control, buttonClicked),
            ControlType.Custom => CreateLibraryControl(control),
            _ => throw new ArgumentOutOfRangeException(nameof(control), control.Type, "Unknown control type."),
        };

        ApplyStyle(control, element is Grid { Children: [GroupBox group, ..] } ? group : element);

        // Named after the design, so tools and tests can find controls in the Preview. A
        // GroupBox's or TabControl's name is on the real control inside the element, as in
        // generated XAML.
        if (control.Type is not (ControlType.GroupBox or ControlType.TabControl))
        {
            element.Name = control.Name;
        }

        // The dark and system themes' Fluent styles set minimum sizes; the design's sizes
        // stand, as in the generated window.
        element.MinWidth = 0;
        element.MinHeight = 0;
        element.Width = control.Width;
        element.Height = control.Height;
        return element;
    }

    /// <summary>The drawing a library control gets on the canvas, when nothing better draws it.</summary>
    public static Func<ControlDocument, FrameworkElement?>? LibraryRenderer { get; set; }

    /// <summary>
    /// A library control: drawn by <see cref="LibraryRenderer"/> when it can, otherwise a
    /// labelled box with the control's type and its text, if it has one.
    /// </summary>
    private static FrameworkElement CreateLibraryControl(ControlDocument control)
    {
        if (LibraryRenderer?.Invoke(control) is { } rendered)
        {
            return rendered;
        }

        var type = control.Properties.LibraryType ?? "";
        var name = type[(type.LastIndexOf('.') + 1)..];
        var text = control.Properties.LibrarySettings?.FirstOrDefault(s => s.Name is "Content" or "Text" or "Label" && s.Kind == LibraryValueKind.Text)?.Value;
        var accent = Color.FromRgb(0x63, 0x5B, 0xDF);
        var box = new Grid { ClipToBounds = true, ToolTip = type };
        box.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Fill = new SolidColorBrush(Color.FromArgb(0x1C, accent.R, accent.G, accent.B)),
            Stroke = new SolidColorBrush(Color.FromArgb(0xB0, accent.R, accent.G, accent.B)),
            StrokeDashArray = [4, 3],
            RadiusX = 4,
            RadiusY = 4,
        });
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 2, 8, 2) };
        label.Children.Add(new TextBlock
        {
            Text = "◆ " + name,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x4B, 0x44, 0xB8)),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (!string.IsNullOrEmpty(text))
        {
            label.Children.Add(new TextBlock { Text = text, Opacity = 0.75, TextTrimming = TextTrimming.CharacterEllipsis });
        }

        box.Children.Add(label);
        return box;
    }

    /// <summary>
    /// A real WPF StackPanel or Grid holding its children, laid out the way the generated WPF
    /// window does it. Used for Preview; nested containers are built the same way.
    /// </summary>
    private static Panel CreatePanel(ControlDocument container, Action<ControlDocument>? buttonClicked)
    {
        var children = container.Children ?? [];
        var properties = container.Properties;
        if (container.Type is ControlType.StackPanel or ControlType.GroupBox or ControlType.TabPage)
        {
            var vertical = properties.Orientation != StackOrientation.Horizontal;
            var stack = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal, ClipToBounds = true };
            for (var i = 0; i < children.Count; i++)
            {
                var element = Create(children[i], buttonClicked);
                var gap = i > 0 ? properties.Spacing ?? 0 : 0;
                if (vertical)
                {
                    element.ClearValue(FrameworkElement.WidthProperty);
                    element.HorizontalAlignment = HorizontalAlignment.Stretch;
                    element.Margin = new Thickness(0, gap, 0, 0);
                }
                else
                {
                    element.ClearValue(FrameworkElement.HeightProperty);
                    element.VerticalAlignment = VerticalAlignment.Stretch;
                    element.Margin = new Thickness(gap, 0, 0, 0);
                }

                stack.Children.Add(element);
            }

            return stack;
        }

        var grid = new Grid { ClipToBounds = true };
        AddTracks(grid, container);

        foreach (var child in children)
        {
            var element = Create(child, buttonClicked);
            element.ClearValue(FrameworkElement.WidthProperty);
            element.ClearValue(FrameworkElement.HeightProperty);
            element.HorizontalAlignment = HorizontalAlignment.Stretch;
            element.VerticalAlignment = VerticalAlignment.Stretch;
            Grid.SetRow(element, child.Row ?? 0);
            Grid.SetColumn(element, child.Column ?? 0);
            Grid.SetRowSpan(element, child.RowSpan ?? 1);
            Grid.SetColumnSpan(element, child.ColumnSpan ?? 1);
            grid.Children.Add(element);
        }

        return grid;
    }

    /// <summary>
    /// An Image shows its picture; without one it is a dashed box saying so, so it can be seen
    /// and selected on the canvas.
    /// </summary>
    private static FrameworkElement CreateImage(ControlDocument control)
    {
        var properties = control.Properties;
        if (properties.ImageData is { } data && ImageFile.TryDecode(data, out var bytes))
        {
            try
            {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.StreamSource = new System.IO.MemoryStream(bytes);
                bitmap.EndInit();
                bitmap.Freeze();
                return new Image { Source = bitmap, Stretch = properties.Stretch == ImageStretch.Fill ? Stretch.Fill : Stretch.Uniform };
            }
            catch (Exception ex) when (ex is NotSupportedException or System.IO.IOException or ArgumentException or InvalidOperationException)
            {
                // A picture WPF cannot decode is shown as missing rather than failing the design.
            }
        }

        return new Border
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "No picture",
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    /// <summary>A control's own text size, weight and colours, where it has them.</summary>
    public static void ApplyStyle(ControlDocument control, FrameworkElement element)
    {
        var properties = control.Properties;
        if (element is Control styled)
        {
            if (properties.FontSize is { } size)
            {
                styled.FontSize = size;
            }

            if (properties.IsBold == true)
            {
                styled.FontWeight = FontWeights.Bold;
            }

            if (properties.Foreground is { } text)
            {
                styled.Foreground = Brush(text);
            }

            if (properties.Background is { } fill)
            {
                styled.Background = Brush(fill);
            }
        }
        else if (element is Panel panel && properties.Background is { } fill)
        {
            panel.Background = Brush(fill);
        }
    }

    public static SolidColorBrush Brush(string color)
    {
        var (red, green, blue) = ControlColor.Parts(color);
        return new SolidColorBrush(Color.FromRgb((byte)red, (byte)green, (byte)blue));
    }

    /// <summary>
    /// A GroupBox as generated XAML builds it: a Grid holding the real GroupBox, for the frame
    /// and title, and a StackPanel for the children, inset by the fixed GroupBox inset.
    /// </summary>
    private static Grid CreateGroupBox(ControlDocument group, Action<ControlDocument>? buttonClicked)
    {
        var (left, top, right, bottom) = ContainerLayout.GroupBoxInset;
        var content = CreatePanel(group, buttonClicked);
        content.Margin = new Thickness(left, top, right, bottom);
        return new Grid
        {
            Children =
            {
                new GroupBox { Name = group.Name, Header = new TextBlock { Text = group.Properties.Text ?? "" } },
                content,
            },
        };
    }

    /// <summary>
    /// A TabControl as generated XAML builds it: a Grid holding the real TabControl, with an
    /// empty tab for each page, and each page as a StackPanel on top, inset by the fixed
    /// TabControl inset. Choosing a tab shows its page, as the generated handler does.
    /// </summary>
    private static Grid CreateTabControl(ControlDocument tabs, Action<ControlDocument>? buttonClicked)
    {
        var (left, top, right, bottom) = ContainerLayout.TabControlInset;
        var control = CreateTabStrip(tabs);
        control.Name = tabs.Name;
        var grid = new Grid { Children = { control } };
        var pages = new List<FrameworkElement>();
        foreach (var page in tabs.Children ?? [])
        {
            var element = Create(page, buttonClicked);
            element.ClearValue(FrameworkElement.WidthProperty);
            element.ClearValue(FrameworkElement.HeightProperty);
            element.Margin = new Thickness(left, top, right, bottom);
            pages.Add(element);
            grid.Children.Add(element);
        }

        void ShowPage()
        {
            for (var i = 0; i < pages.Count; i++)
            {
                pages[i].Visibility = control.SelectedIndex == i ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        ShowPage();
        control.SelectionChanged += (_, _) => ShowPage();
        return grid;
    }

    /// <summary>A real TabControl with an empty tab for each page, showing the design's tab.</summary>
    private static TabControl CreateTabStrip(ControlDocument tabs)
    {
        var control = new TabControl();
        foreach (var page in tabs.Children ?? [])
        {
            control.Items.Add(new TabItem { Header = new TextBlock { Text = page.Properties.Text ?? "" } });
        }

        control.SelectedIndex = control.Items.Count > 0 ? ContainerLayout.ShownTab(tabs) : -1;
        return control;
    }

    /// <summary>The real TabControl inside an element made for a TabControl, if it is one.</summary>
    public static TabControl? FindTabControl(UIElement element) =>
        element as TabControl ?? (element as Panel)?.Children.OfType<TabControl>().FirstOrDefault();

    /// <summary>
    /// How a container looks in Design mode: a tinted, outlined area with its name, and cell
    /// lines for a Grid. Its children are drawn separately, on top, by the design surface.
    /// </summary>
    public static FrameworkElement CreateDesignContainer(ControlDocument container)
    {
        var outline = new SolidColorBrush(Color.FromRgb(0x9A, 0xA7, 0xB8));
        var content = new Grid();
        if (container.Type == ControlType.Grid)
        {
            var rows = container.Properties.Rows ?? 1;
            var columns = container.Properties.Columns ?? 1;
            AddTracks(content, container);

            for (var r = 0; r < rows; r++)
            {
                for (var c = 0; c < columns; c++)
                {
                    var cell = new Border
                    {
                        BorderBrush = outline,
                        BorderThickness = new Thickness(c > 0 ? 1 : 0, r > 0 ? 1 : 0, 0, 0),
                        Opacity = 0.6,
                    };
                    Grid.SetRow(cell, r);
                    Grid.SetColumn(cell, c);
                    content.Children.Add(cell);
                }
            }
        }

        // A TabControl shows its real tabs; the page shown is drawn over it.
        if (container.Type == ControlType.TabControl)
        {
            return new Grid
            {
                Width = container.Width,
                Height = container.Height,
                Children = { CreateTabStrip(container) },
            };
        }

        // A GroupBox shows its real frame and title; its children are drawn over it.
        if (container.Type == ControlType.GroupBox)
        {
            var frame = new GroupBox { Header = new TextBlock { Text = container.Properties.Text ?? "" } };
            ApplyStyle(container, frame);
            return new Grid
            {
                Width = container.Width,
                Height = container.Height,
                Background = new SolidColorBrush(Color.FromArgb(0x08, 0x5A, 0x7A, 0xA8)),
                Children = { frame },
            };
        }

        var caption = new TextBlock
        {
            Text = container.Type switch
            {
                ControlType.StackPanel => $"{container.Name} ({container.Properties.Orientation?.ToString().ToLowerInvariant()} stack)",
                ControlType.TabPage => $"{container.Name} (tab page)",
                _ => $"{container.Name} (grid)",
            },
            Foreground = outline,
            FontSize = 10,
            Margin = new Thickness(3, 1, 3, 1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Grid.SetRowSpan(caption, Math.Max(1, content.RowDefinitions.Count));
        Grid.SetColumnSpan(caption, Math.Max(1, content.ColumnDefinitions.Count));
        content.Children.Add(caption);

        return new Border
        {
            Background = container.Properties.Background is { } fill ? Brush(fill) : new SolidColorBrush(Color.FromArgb(0x10, 0x5A, 0x7A, 0xA8)),
            BorderBrush = outline,
            BorderThickness = new Thickness(1),
            Child = content,
            Width = container.Width,
            Height = container.Height,
        };
    }

    /// <summary>Row and column definitions sized as the design says: fixed DIPs or shares.</summary>
    private static void AddTracks(Grid grid, ControlDocument container)
    {
        static GridLength Length(GridTrackSize size) =>
            new(size.Value, size.IsProportional ? GridUnitType.Star : GridUnitType.Pixel);

        var properties = container.Properties;
        foreach (var size in GridTrackSize.Resolve(properties.RowSizes, properties.Rows ?? 1))
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = Length(size) });
        }

        foreach (var size in GridTrackSize.Resolve(properties.ColumnSizes, properties.Columns ?? 1))
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = Length(size) });
        }
    }

    private static Button CreateButton(ControlDocument control, Action<ControlDocument>? clicked)
    {
        var button = new Button { Content = new TextBlock { Text = control.Properties.Text ?? "" } };
        if (clicked is not null)
        {
            button.Click += (_, _) => clicked(control);
        }

        return button;
    }
}
