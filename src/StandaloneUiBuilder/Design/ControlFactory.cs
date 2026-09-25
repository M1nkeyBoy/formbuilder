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
            ControlType.TextBox => new TextBox
            {
                Text = properties.Text ?? "",
                VerticalContentAlignment = VerticalAlignment.Center,
            },
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
            _ => throw new ArgumentOutOfRangeException(nameof(control), control.Type, "Unknown control type."),
        };

        // Named after the design, so tools and tests can find controls in the Preview.
        element.Name = control.Name;
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
        if (container.Type == ControlType.StackPanel)
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
        for (var r = 0; r < (properties.Rows ?? 1); r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        for (var c = 0; c < (properties.Columns ?? 1); c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

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
            for (var r = 0; r < rows; r++)
            {
                content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            }

            for (var c = 0; c < columns; c++)
            {
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

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
