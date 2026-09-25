using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Design;

namespace StandaloneUiBuilder;

public partial class MainWindow : Window
{
    private const string AppTitle = "Standalone UI Builder";
    private const string FileFilter = "UI Builder project (*.uibproj)|*.uibproj";

    private readonly DesignEditor editor = new();
    private readonly Dictionary<TextBox, string> fieldErrors = [];
    private Guid? selectedId;
    private Guid? inspectedId;
    private Point? toolboxDragStart;
    private string? projectPath;

    public MainWindow()
    {
        InitializeComponent();

        ToolboxList.ItemsSource = ControlCatalog.All;
        Surface.ControlClicked += (_, id) => Select(id);
        Surface.BlankClicked += Surface_BlankClicked;
        Surface.ControlDropped += (_, e) => AddControl(e.Type, e.X, e.Y);
        Surface.BoundsChanging += (_, e) => ShowBounds(e.Id, e.Bounds);
        Surface.BoundsCommitted += Surface_BoundsCommitted;

        foreach (var box in new[] { NameBox, XBox, YBox, WidthBox, HeightBox, TextValueBox, ItemsBox })
        {
            box.LostKeyboardFocus += (_, _) => CommitField(box);
            box.KeyDown += InspectorField_KeyDown;
        }

        editor.Changed += (_, _) => RefreshAll();
        RefreshAll();

        // Keep the default size within small screens so the window opens fully visible.
        var workArea = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(Width, workArea.Width));
        Height = Math.Max(MinHeight, Math.Min(Height, workArea.Height));

        // A project path on the command line (for example from Explorer) is opened at startup.
        var startupPath = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault();
        if (startupPath is not null)
        {
            Loaded += (_, _) => OpenPath(startupPath);
        }
    }

    private string ProjectDisplayName =>
        projectPath is not null ? Path.GetFileNameWithoutExtension(projectPath) : editor.Document.Name;

    private ControlDefinition? ArmedToolboxItem => ToolboxList.SelectedItem as ControlDefinition;

    private void RefreshAll()
    {
        if (selectedId is { } id && editor.FindControl(id) is null)
        {
            selectedId = null;
        }

        Surface.Render(editor.Document.Screen, selectedId);
        RefreshInspector();
        Title = $"{ProjectDisplayName}{(editor.IsDirty ? " ●" : "")} — {AppTitle}";
        CommandManager.InvalidateRequerySuggested();
    }

    private void Select(Guid? id)
    {
        selectedId = id;
        Surface.SetSelection(id);
        RefreshInspector();
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

    private void ShowBounds(Guid id, ControlBounds bounds)
    {
        if (editor.FindControl(id) is { } control)
        {
            StatusText.Text = $"{control.Name}: {bounds.X}, {bounds.Y} · {bounds.Width} × {bounds.Height}";
        }
    }

    private void Surface_BoundsCommitted(object? sender, BoundsChangedEventArgs e)
    {
        if (editor.SetBounds(e.Id, e.Bounds) is { } error)
        {
            StatusText.Text = error;
            RefreshAll();
        }
        else
        {
            ShowBounds(e.Id, e.Bounds);
        }
    }

    private void RefreshInspector()
    {
        var control = selectedId is { } id ? editor.FindControl(id) : null;
        if (control?.Id != inspectedId)
        {
            foreach (var box in fieldErrors.Keys.ToList())
            {
                SetFieldError(box, null);
            }
        }

        inspectedId = control?.Id;
        NoSelectionText.Visibility = control is null ? Visibility.Visible : Visibility.Collapsed;
        InspectorPanel.Visibility = control is null ? Visibility.Collapsed : Visibility.Visible;
        if (control is null)
        {
            return;
        }

        var definition = ControlCatalog.Get(control.Type);
        var properties = control.Properties;
        TypeText.Text = control.Type.ToString();
        SetField(NameBox, control.Name);
        SetField(XBox, control.X.ToString(CultureInfo.CurrentCulture));
        SetField(YBox, control.Y.ToString(CultureInfo.CurrentCulture));
        SetField(WidthBox, control.Width.ToString(CultureInfo.CurrentCulture));
        SetField(HeightBox, control.Height.ToString(CultureInfo.CurrentCulture));

        TextRow.Visibility = definition.HasText ? Visibility.Visible : Visibility.Collapsed;
        SetField(TextValueBox, properties.Text ?? "");
        IsCheckedRow.Visibility = definition.HasIsChecked ? Visibility.Visible : Visibility.Collapsed;
        IsCheckedBox.IsChecked = properties.IsChecked == true;
        ItemsRow.Visibility = definition.HasItems ? Visibility.Visible : Visibility.Collapsed;
        SetField(ItemsBox, string.Join(Environment.NewLine, properties.Items ?? []));
    }

    /// <summary>Shows a document value, unless the field holds rejected input the user has not fixed.</summary>
    private void SetField(TextBox box, string value)
    {
        if (!fieldErrors.ContainsKey(box))
        {
            box.Text = value;
        }
    }

    private void CommitField(TextBox box)
    {
        if (inspectedId is not { } id || editor.FindControl(id) is not { } control)
        {
            return;
        }

        var error =
            box == NameBox ? editor.Rename(id, box.Text)
            : box == TextValueBox ? editor.SetText(id, box.Text)
            : box == ItemsBox ? editor.SetItems(id, box.Text.Split('\n').Select(line => line.TrimEnd('\r')))
            : CommitBoundsField(control, box);

        SetFieldError(box, error);
        if (error is null)
        {
            // Show the value as stored, for example trimmed or with blank items removed.
            RefreshInspector();
        }
    }

    private string? CommitBoundsField(ControlDocument control, TextBox box)
    {
        if (!int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var value))
        {
            return "Enter a whole number of DIPs.";
        }

        var bounds = control.Bounds;
        bounds = box == XBox ? bounds with { X = value }
            : box == YBox ? bounds with { Y = value }
            : box == WidthBox ? bounds with { Width = value }
            : bounds with { Height = value };

        // Exact values are allowed here, without snapping, so alignment can be corrected precisely.
        return editor.SetBounds(control.Id, bounds);
    }

    private void SetFieldError(TextBox box, string? error)
    {
        if (error is null)
        {
            fieldErrors.Remove(box);
            box.ClearValue(Control.BorderBrushProperty);
            box.ClearValue(ToolTipProperty);
        }
        else
        {
            fieldErrors[box] = error;
            box.BorderBrush = InspectorErrorText.Foreground;
            box.ToolTip = error;
        }

        InspectorErrorText.Text = string.Join(Environment.NewLine, fieldErrors.Values);
        InspectorErrorText.Visibility = fieldErrors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void InspectorField_KeyDown(object sender, KeyEventArgs e)
    {
        var box = (TextBox)sender;
        if (e.Key == Key.Enter && !box.AcceptsReturn)
        {
            CommitField(box);
            box.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SetFieldError(box, null);
            RefreshInspector();
            box.SelectAll();
            e.Handled = true;
        }
    }

    private void IsCheckedBox_Click(object sender, RoutedEventArgs e)
    {
        if (inspectedId is { } id)
        {
            editor.SetIsChecked(id, IsCheckedBox.IsChecked == true);
        }
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

    /// <summary>Applies a value still being typed in the inspector before a file command runs.</summary>
    private void CommitFocusedField()
    {
        if (Keyboard.FocusedElement is TextBox box && InspectorPanel.IsAncestorOf(box))
        {
            CommitField(box);
        }
    }

    /// <summary>
    /// Offers to save unsaved changes. Returns false if the user cancelled or the save failed,
    /// in which case the current work must stay open.
    /// </summary>
    private bool ConfirmCloseDocument()
    {
        CommitFocusedField();
        if (!editor.IsDirty)
        {
            return true;
        }

        return UnsavedChangesDialog.Ask(this, ProjectDisplayName) switch
        {
            UnsavedChangesChoice.Save => Save(saveAs: false),
            UnsavedChangesChoice.Discard => true,
            _ => false,
        };
    }

    private bool Save(bool saveAs)
    {
        CommitFocusedField();

        var path = projectPath;
        if (saveAs || path is null)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = FileFilter,
                DefaultExt = ProjectFile.Extension,
                AddExtension = true,
                FileName = ProjectDisplayName,
            };
            if (dialog.ShowDialog(this) != true)
            {
                return false;
            }

            path = dialog.FileName;
        }

        try
        {
            ProjectFile.Save(editor.Document with { Name = Path.GetFileNameWithoutExtension(path) }, path);
        }
        catch (ProjectFileException ex)
        {
            ShowError($"Could not save the project to \"{path}\".", ex.Message);
            return false;
        }

        projectPath = path;
        editor.MarkSaved();
        StatusText.Text = $"Saved {path}";
        return true;
    }

    private void OpenPath(string path)
    {
        ProjectDocument document;
        try
        {
            document = ProjectFile.Load(path);
        }
        catch (ProjectFileException ex)
        {
            // The current project stays open and unchanged.
            ShowError($"Could not open \"{path}\".", ex.Message);
            return;
        }

        projectPath = Path.GetFullPath(path);
        selectedId = null;
        editor.Reset(document);
        StatusText.Text = $"Opened {projectPath}";
    }

    private void ShowError(string summary, string detail) =>
        MessageBox.Show(this, $"{summary}{Environment.NewLine}{Environment.NewLine}{detail}", AppTitle,
            MessageBoxButton.OK, MessageBoxImage.Error);

    private void New_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (!ConfirmCloseDocument())
        {
            return;
        }

        projectPath = null;
        selectedId = null;
        editor.New();
        StatusText.Text = "New design";
    }

    private void Open_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (!ConfirmCloseDocument())
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = FileFilter };
        if (dialog.ShowDialog(this) == true)
        {
            OpenPath(dialog.FileName);
        }
    }

    private void Save_Executed(object sender, ExecutedRoutedEventArgs e) => Save(saveAs: false);

    private void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e) => Save(saveAs: true);

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!e.Cancel && !ConfirmCloseDocument())
        {
            e.Cancel = true;
        }
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

    private void Undo_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = editor.CanUndo;

    private void Undo_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        editor.Undo();
        StatusText.Text = "Undo";
    }

    private void Redo_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = editor.CanRedo;

    private void Redo_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        editor.Redo();
        StatusText.Text = "Redo";
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
