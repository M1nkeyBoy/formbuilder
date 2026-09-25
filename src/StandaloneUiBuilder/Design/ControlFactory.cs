using System.Windows;
using System.Windows.Controls;
using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder.Design;

/// <summary>
/// Maps document control types to the WPF controls that display them. The WPF controls are
/// views of the document only; they are rebuilt from it and never saved.
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
            _ => throw new ArgumentOutOfRangeException(nameof(control), control.Type, "Unknown control type."),
        };

        element.Width = control.Width;
        element.Height = control.Height;
        return element;
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
