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
            _ => throw new ArgumentOutOfRangeException(nameof(control), control.Type, "Unknown control type."),
        };

        // Named after the design, so tools and tests can find controls in the Preview. A
        // GroupBox's name is on the GroupBox inside the element, as in generated XAML.
        if (control.Type != ControlType.GroupBox)
        {
            element.Name = control.Name;
        }

        element.Width = control.Width;
        element.Height = control.Height;
        return element;
    }

    /// <summary>
    /// A real WPF StackPanel or Grid holding its children, laid out the way the generated WPF
    /// window does it. Used for Preview; nested containers are built the same way.
    /// </summary>
    private static Panel CreatePanel(ControlDocument container, Action<ControlDocument>? buttonClicked)
    {
        var children = container.Children ?? [];
        var properties = container.Properties;
        if (container.Type is ControlType.StackPanel or ControlType.GroupBox)
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

        // A GroupBox shows its real frame and title; its children are drawn over it.
        if (container.Type == ControlType.GroupBox)
        {
            return new Grid
            {
                Width = container.Width,
                Height = container.Height,
                Background = new SolidColorBrush(Color.FromArgb(0x08, 0x5A, 0x7A, 0xA8)),
                Children = { new GroupBox { Header = new TextBlock { Text = container.Properties.Text ?? "" } } },
            };
        }

        var caption = new TextBlock
        {
            Text = $"{container.Name} ({(container.Type == ControlType.StackPanel ? container.Properties.Orientation?.ToString().ToLowerInvariant() + " stack" : "grid")})",
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
            Background = new SolidColorBrush(Color.FromArgb(0x10, 0x5A, 0x7A, 0xA8)),
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
