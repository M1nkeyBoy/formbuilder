using System.Windows;
using System.Windows.Input;
using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder;

public partial class MainWindow : Window
{
    private const string AppTitle = "Standalone UI Builder";

    private readonly DesignEditor editor = new();

    public MainWindow()
    {
        InitializeComponent();

        editor.Changed += (_, _) => RefreshAll();
        RefreshAll();

        // Keep the default size within small screens so the window opens fully visible.
        var workArea = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(Width, workArea.Width));
        Height = Math.Max(MinHeight, Math.Min(Height, workArea.Height));
    }

    private void RefreshAll()
    {
        Surface.Render(editor.Document.Screen);
        Title = $"{editor.Document.Name}{(editor.IsDirty ? " ●" : "")} — {AppTitle}";
    }

    private void New_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        editor.New();
        StatusText.Text = "New design";
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void DesignMenuItem_Click(object sender, RoutedEventArgs e) => DesignModeButton.IsChecked = true;

    private void PreviewMenuItem_Click(object sender, RoutedEventArgs e) => PreviewModeButton.IsChecked = true;

    // Shell only: the mode switch updates the UI state; preview behavior arrives in Slice 5.
    private void DesignModeButton_Checked(object sender, RoutedEventArgs e) => SetMode(isPreview: false);

    private void PreviewModeButton_Checked(object sender, RoutedEventArgs e) => SetMode(isPreview: true);

    private void SetMode(bool isPreview)
    {
        // Checked handlers can fire during InitializeComponent, before later named elements exist.
        if (DesignMenuItem is null || PreviewMenuItem is null || StatusText is null)
        {
            return;
        }

        DesignMenuItem.IsChecked = !isPreview;
        PreviewMenuItem.IsChecked = isPreview;
        StatusText.Text = isPreview ? "Preview mode (not yet implemented)" : "Design mode";
    }
}
