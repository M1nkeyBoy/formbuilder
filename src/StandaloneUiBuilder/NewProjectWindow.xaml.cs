using System.Windows;
using System.Windows.Input;
using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder;

/// <summary>Asks which platform a new project is for.</summary>
public partial class NewProjectWindow : Window
{
    private sealed record PlatformItem(ProjectPlatform Platform, string Name, string Description);

    private NewProjectWindow(ProjectPlatform initial)
    {
        InitializeComponent();
        var items = ProjectPlatforms.All.Select(p => new PlatformItem(p, p.DisplayName(), p.Description())).ToList();
        PlatformList.ItemsSource = items;
        PlatformList.SelectedItem = items.First(i => i.Platform == initial);
        Loaded += (_, _) =>
        {
            if (PlatformList.ItemContainerGenerator.ContainerFromItem(PlatformList.SelectedItem) is UIElement item)
            {
                item.Focus();
            }
        };
    }

    /// <summary>Shows the window; returns the chosen platform, or null if the user cancelled.</summary>
    public static ProjectPlatform? Choose(Window owner, ProjectPlatform initial = ProjectPlatform.Wpf)
    {
        var window = new NewProjectWindow(initial) { Owner = owner };
        return window.ShowDialog() == true && window.PlatformList.SelectedItem is PlatformItem item ? item.Platform : null;
    }

    private void Create_Click(object sender, RoutedEventArgs e) => DialogResult = PlatformList.SelectedItem is not null;

    private void PlatformList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PlatformList.SelectedItem is not null)
        {
            DialogResult = true;
        }
    }
}
