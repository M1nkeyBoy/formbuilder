using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Design;

namespace StandaloneUiBuilder;

public partial class MainWindow : Window
{
    private const string AppTitle = "Standalone UI Builder";

    private readonly DesignEditor editor = new();
    private Guid? selectedId;
    private Point? toolboxDragStart;

    public MainWindow()
    {
        InitializeComponent();

        ToolboxList.ItemsSource = ControlCatalog.All;
        Surface.ControlClicked += (_, id) => Select(id);
        Surface.BlankClicked += Surface_BlankClicked;
        Surface.ControlDropped += (_, e) => AddControl(e.Type, e.X, e.Y);

        editor.Changed += (_, _) => RefreshAll();
        RefreshAll();

        // Keep the default size within small screens so the window opens fully visible.
        var workArea = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(Width, workArea.Width));
        Height = Math.Max(MinHeight, Math.Min(Height, workArea.Height));
    }

    private ControlDefinition? ArmedToolboxItem => ToolboxList.SelectedItem as ControlDefinition;

    private void RefreshAll()
    {
        if (selectedId is { } id && editor.FindControl(id) is null)
        {
            selectedId = null;
        }

        Surface.Render(editor.Document.Screen, selectedId);
        Title = $"{editor.Document.Name}{(editor.IsDirty ? " ●" : "")} — {AppTitle}";
        CommandManager.InvalidateRequerySuggested();
    }

    private void Select(Guid? id)
    {
        selectedId = id;
        Surface.SetSelection(id);
        CommandManager.InvalidateRequerySuggested();

        StatusText.Text = id is { } value && editor.FindControl(value) is { } control
            ? $"Selected {control.Name} ({control.Type})"
            : "Ready";
    }

    private void AddControl(ControlType type, double x, double y)
    {
        var control = editor.AddControl(type, x, y);
        ToolboxList.SelectedItem = null;
        Select(control.Id);
        StatusText.Text = $"Added {control.Name}";
    }

    private void Surface_BlankClicked(object? sender, Point position)
    {
        if (ArmedToolboxItem is { } armed)
        {
            AddControl(armed.Type, position.X, position.Y);
        }
        else
        {
            Select(null);
        }
    }

    private void ToolboxList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ArmedToolboxItem is { } armed)
        {
            StatusText.Text = $"Click the canvas to place a {armed.Type}, or press Enter to add it.";
        }
    }

    private void ToolboxList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ArmedToolboxItem is { } armed)
        {
            // Cascade keyboard-added controls so they do not stack exactly on top of each other.
            var offset = 20 + editor.Document.Screen.Controls.Count % 10 * 20;
            AddControl(armed.Type, offset, offset);
            e.Handled = true;
        }
    }

    private void ToolboxList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        toolboxDragStart = e.GetPosition(ToolboxList);
    }

    private void ToolboxList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || toolboxDragStart is not { } start)
        {
            return;
        }

        var delta = e.GetPosition(ToolboxList) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        toolboxDragStart = null;
        if (ItemsControl.ContainerFromElement(ToolboxList, (DependencyObject)e.OriginalSource) is ListBoxItem { DataContext: ControlDefinition definition })
        {
            var data = new DataObject(DesignSurface.ControlTypeDataFormat, definition.Type.ToString());
            DragDrop.DoDragDrop(ToolboxList, data, DragDropEffects.Copy);
        }
    }

    private void New_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        selectedId = null;
        editor.New();
        StatusText.Text = "New design";
    }

    private void Delete_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = selectedId is not null;

    private void Delete_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (selectedId is { } id && editor.FindControl(id) is { } control && editor.DeleteControl(id))
        {
            Select(null);
            StatusText.Text = $"Deleted {control.Name}";
        }
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
