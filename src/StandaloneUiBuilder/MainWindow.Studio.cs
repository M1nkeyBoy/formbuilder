using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Design;

namespace StandaloneUiBuilder;

/// <summary>
/// The parts of the main window from the redesign (docs/design): the project header, the
/// toolbox's categories and search, the Layers list, the inspector's sections, zoom, the grid
/// switch and the status bar.
/// </summary>
public partial class MainWindow
{
    private static readonly double[] ZoomSteps = [0.25, 0.33, 0.5, 0.67, 0.75, 0.9, 1, 1.25, 1.5, 2, 3, 4];

    private double zoom = 1;
    private bool syncingLayers;
    private ICollectionView? toolboxView;

    private sealed record LayerItem(Guid Id, string Name, string Type, Thickness Indent);

    /// <summary>The toolbox as tiles in three groups, filtered by the search box.</summary>
    private void SetUpToolbox()
    {
        var types = ControlCatalog.All.Where(d => d.InToolbox)
            .Select((definition, index) => (definition, index))
            .OrderBy(t => Array.IndexOf(ToolboxCategories, ToolboxCategory(t.definition.Type)))
            .ThenBy(t => t.index)
            .Select(t => t.definition)
            .ToList();
        var source = new CollectionViewSource { Source = types };
        source.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ControlDefinition.Type), new ToolboxCategoryConverter()));
        toolboxView = source.View;
        toolboxView.Filter = item => item is ControlDefinition d
            && (ControlSearchBox.Text.Trim() is not { Length: > 0 } search || d.Type.ToString().Contains(search, StringComparison.OrdinalIgnoreCase));
        ToolboxList.ItemsSource = toolboxView;
    }

    private static readonly string[] ToolboxCategories = ["Essentials", "Containers", "More controls"];

    private static string ToolboxCategory(ControlType type) => type switch
    {
        ControlType.Label or ControlType.Button or ControlType.TextBox or ControlType.CheckBox or ControlType.ComboBox or ControlType.Image => "Essentials",
        ControlType.StackPanel or ControlType.Grid or ControlType.GroupBox or ControlType.TabControl => "Containers",
        _ => "More controls",
    };

    private sealed class ToolboxCategoryConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            ToolboxCategory((ControlType)value).ToUpperInvariant();

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>Whatever takes keyboard focus scrolls into view, in the inspector or the toolbox.</summary>
    private static void Window_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is FrameworkElement element and not DesignSurface)
        {
            element.BringIntoView();
        }
    }

    private void ControlSearchBox_TextChanged(object sender, TextChangedEventArgs e) => toolboxView?.Refresh();

    /// <summary>Everything outside the canvas and inspector that shows the document.</summary>
    private void RefreshStudio()
    {
        var screen = editor.Screen;
        ProjectNameText.Text = ProjectDisplayName + (editor.IsDirty ? " ●" : "");
        ProjectSubtitleText.Text = projectPath ?? "Not saved yet";
        ProjectSubtitleText.ToolTip = projectPath;
        ScreenSizeText.Text = $"{screen.Name}  /  {screen.Width} × {screen.Height}";
        var count = ControlTree.All(screen.Controls).Count();
        ControlCountText.Text = count == 1 ? "1 control" : $"{count} controls";
        var platform = editor.Document.Platform;
        PlatformButton.Content = platform.DisplayName();
        foreach (var item in PlatformMenuItem.Items.OfType<MenuItem>().Concat(PlatformMenu.Items.OfType<MenuItem>()))
        {
            item.IsChecked = (ProjectPlatform)item.Tag == platform;
        }

        ExportButton.Content = platform == ProjectPlatform.Any ? "Export ▾" : $"Export to {platform.DisplayName()}";
        ScreenInfoText.Text = $"{platform.DisplayName()} · {ThemeName(editor.Document.Theme)} theme · {screen.Width} × {screen.Height} DIPs · {Math.Round(zoom * 100)}%";
        ShowGridBox.IsChecked = ShowGridMenuItem.IsChecked = Surface.ShowGrid;
        RefreshLayers();
    }

    private static string ThemeName(ProjectTheme theme) => theme switch
    {
        ProjectTheme.Dark => "Dark",
        ProjectTheme.System => "System",
        _ => "Light",
    };

    /// <summary>The screen's controls, inside their containers, with the selection shown.</summary>
    private void RefreshLayers()
    {
        var items = ContainerLayout.Flatten(editor.Screen)
            .Select(p => new LayerItem(p.Control.Id, p.Control.Name, p.Control.Type.ToString(), new Thickness(p.Depth * 14, 0, 0, 0)))
            .ToList();
        syncingLayers = true;
        try
        {
            if (LayersList.ItemsSource is not List<LayerItem> shown || !shown.SequenceEqual(items))
            {
                LayersList.ItemsSource = items;
            }

            LayersList.SelectedItems.Clear();
            foreach (var item in ((List<LayerItem>)LayersList.ItemsSource).Where(i => selection.Contains(i.Id)))
            {
                LayersList.SelectedItems.Add(item);
            }
        }
        finally
        {
            syncingLayers = false;
        }
    }

    private void LayersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!syncingLayers)
        {
            SetSelection(LayersList.SelectedItems.Cast<LayerItem>().Select(i => i.Id).ToList());
        }
    }

    /// <summary>Hides a section of the inspector when none of its fields applies to the control.</summary>
    private void UpdateInspectorSections()
    {
        BoundsGrid.Visibility = Show(new[] { XRow, YRow, WidthRow, HeightRow }.Any(r => r.Visibility == Visibility.Visible));
        foreach (var section in new[] { IdentitySection, LayoutSection, AppearanceSection, InteractionSection })
        {
            var fields = ((Panel)section.Child).Children.OfType<FrameworkElement>().Where(f => f is not TextBlock);
            section.Visibility = Show(fields.Any(f => f.Visibility == Visibility.Visible));
        }
    }

    /// <summary>A project for one platform exports straight to it; one for any platform chooses from the menu.</summary>
    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (ExportCommand(editor.Document.Platform) is { } command)
        {
            command.Execute(null, this);
        }
        else
        {
            OpenMenu(ExportButton, ExportMenu);
        }
    }

    private static RoutedUICommand? ExportCommand(ProjectPlatform platform) => platform switch
    {
        ProjectPlatform.Wpf => EditorCommands.ExportWpf,
        ProjectPlatform.WinForms => EditorCommands.ExportWinForms,
        ProjectPlatform.WinUI => EditorCommands.ExportWinUI,
        ProjectPlatform.Maui => EditorCommands.ExportMaui,
        ProjectPlatform.Blazor => EditorCommands.ExportBlazor,
        _ => null,
    };

    private void Export_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = editor.Document.Platform == ProjectPlatform.Any || ExportCommand(editor.Document.Platform) == e.Command;

    /// <summary>The platform choices in Project > Platform and the header's platform menu.</summary>
    private void SetUpPlatformMenus()
    {
        foreach (var menu in new ItemsControl[] { PlatformMenuItem, PlatformMenu })
        {
            foreach (var platform in ProjectPlatforms.All)
            {
                menu.Items.Add(new MenuItem
                {
                    Header = platform.DisplayName(),
                    Command = EditorCommands.SetPlatform,
                    CommandParameter = platform.ToString(),
                    Tag = platform,
                });
            }
        }
    }

    private void PlatformButton_Click(object sender, RoutedEventArgs e) => OpenMenu(PlatformButton, PlatformMenu);

    private void SetPlatform_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var platform = Enum.Parse<ProjectPlatform>((string)e.Parameter);
        StatusText.Text = editor.SetPlatform(platform)
            ? platform == ProjectPlatform.Any ? "Platform: any; the project exports to all five" : $"Platform: {platform.DisplayName()}"
            : "That platform is already chosen";
    }

    /// <summary>
    /// Asks which platform a new project is for; the test hook UIB_START_PLATFORM answers
    /// instead, so tests start without the question.
    /// </summary>
    private ProjectPlatform? AskPlatform(ProjectPlatform initial)
    {
        if (Environment.GetEnvironmentVariable("UIB_START_PLATFORM") is { Length: > 0 } answer
            && Enum.TryParse<ProjectPlatform>(answer, ignoreCase: true, out var platform))
        {
            return platform;
        }

        return NewProjectWindow.Choose(this, initial);
    }

    private void ArrangeButton_Click(object sender, RoutedEventArgs e) => OpenMenu(ArrangeButton, ArrangeMenu);

    private static void OpenMenu(Button button, ContextMenu menu)
    {
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void ShowGridBox_Click(object sender, RoutedEventArgs e) => SetShowGrid(ShowGridBox.IsChecked == true);

    private void ShowGridMenuItem_Click(object sender, RoutedEventArgs e) => SetShowGrid(ShowGridMenuItem.IsChecked);

    private void SetShowGrid(bool show)
    {
        Surface.ShowGrid = show;
        ShowGridBox.IsChecked = ShowGridMenuItem.IsChecked = show;
        StatusText.Text = show ? "Grid shown" : "Grid hidden; controls still snap to it";
    }

    private void ZoomIn_Executed(object sender, ExecutedRoutedEventArgs e) =>
        SetZoom(ZoomSteps.FirstOrDefault(s => s > zoom + 0.001, ZoomSteps[^1]));

    private void ZoomOut_Executed(object sender, ExecutedRoutedEventArgs e) =>
        SetZoom(ZoomSteps.LastOrDefault(s => s < zoom - 0.001, ZoomSteps[0]));

    private void ActualSize_Executed(object sender, ExecutedRoutedEventArgs e) => SetZoom(1);

    /// <summary>As large as fits in the canvas without scrolling, up to 200%.</summary>
    private void FitToWindow_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var width = SurfaceScroller.ViewportWidth - 2 * Artboard.Margin.Left - 2;
        var height = SurfaceScroller.ViewportHeight - 2 * Artboard.Margin.Top - 2;
        if (width > 0 && height > 0 && Surface.Width > 0 && Surface.Height > 0)
        {
            SetZoom(Math.Min(2, Math.Min(width / Surface.Width, height / Surface.Height)));
        }
    }

    private void SurfaceScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            SetZoom(e.Delta > 0
                ? ZoomSteps.FirstOrDefault(s => s > zoom + 0.001, ZoomSteps[^1])
                : ZoomSteps.LastOrDefault(s => s < zoom - 0.001, ZoomSteps[0]));
            e.Handled = true;
        }
    }

    /// <summary>
    /// Draws the canvas larger or smaller. The design stays in DIPs: the surface reads the
    /// pointer in its own coordinates, so dragging and snapping work the same at any zoom.
    /// </summary>
    private void SetZoom(double value)
    {
        zoom = Math.Clamp(value, ZoomSteps[0], ZoomSteps[^1]);
        ZoomTransform.ScaleX = ZoomTransform.ScaleY = zoom;
        ZoomText.Text = $"{Math.Round(zoom * 100)}%";
        StatusText.Text = $"Zoom {Math.Round(zoom * 100)}%";
        RefreshStudio();
    }
}
