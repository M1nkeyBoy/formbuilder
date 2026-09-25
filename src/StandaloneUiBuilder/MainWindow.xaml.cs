using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using StandaloneUiBuilder.Core;
using StandaloneUiBuilder.Design;
using StandaloneUiBuilder.Output;
using StandaloneUiBuilder.Output.Blazor;
using StandaloneUiBuilder.Output.WinForms;
using StandaloneUiBuilder.Output.Wpf;

namespace StandaloneUiBuilder;

public partial class MainWindow : Window
{
    private const string AppTitle = "Standalone UI Builder";
    private const string FileFilter = "UI Builder project (*.uibproj)|*.uibproj";

    private readonly DesignEditor editor = new();
    private readonly Dictionary<TextBox, string> fieldErrors = [];
    private bool refreshingInspector;
    private string? anchorError;
    private List<Guid> selection = [];
    private List<ControlDocument> clipboard = [];
    private int pasteCount;
    private Guid? inspectedId;
    private Point? toolboxDragStart;
    private string? projectPath;
    private string? lastExportFolder;
    private bool isPreview;
    private string? shownScreenId;

    // Screens opened by buttons in Preview, most recent last, so a closing button goes back.
    private readonly Stack<string> previewTrail = new();
    private bool refreshingScreenTabs;

    // UIB_RECOVERY_DIR moves recovery drafts elsewhere, so automated tests never touch a
    // user's real drafts or each other's.
    private readonly RecoveryStore recoveryStore = new(
        Environment.GetEnvironmentVariable("UIB_RECOVERY_DIR") is { Length: > 0 } recoveryDirectory
            ? recoveryDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StandaloneUiBuilder", "Recovery"));

    private readonly DispatcherTimer draftTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private RecoverySession? recoverySession;

    public MainWindow()
    {
        InitializeComponent();

        ToolboxList.ItemsSource = ControlCatalog.All;
        Surface.ControlClicked += Surface_ControlClicked;
        Surface.ControlClickCompleted += Surface_ControlClickCompleted;
        Surface.BlankClicked += Surface_BlankClicked;
        Surface.BandSelected += Surface_BandSelected;
        Surface.MoveCommitted += (_, e) => MoveSelection(e.Ids, e.Dx, e.Dy);
        Surface.ReparentRequested += Surface_ReparentRequested;
        Surface.NudgeRequested += (_, e) => MoveSelection(e.Ids, e.Dx, e.Dy);
        Surface.ControlDropped += (_, e) => AddControl(e.Type, e.X, e.Y);
        Surface.BoundsChanging += (_, e) => ShowBounds(e.Id, e.Bounds);
        Surface.BoundsCommitted += Surface_BoundsCommitted;

        foreach (var box in new[] { NameBox, XBox, YBox, WidthBox, HeightBox, TextValueBox, ItemsBox, ScreenWidthBox, ScreenHeightBox,
                                    RowBox, ColumnBox, RowSpanBox, ColumnSpanBox, SpacingBox, RowsBox, ColumnsBox,
                                    RowSizesBox, ColumnSizesBox, ScreenNameBox,
                                    MinimumBox, MaximumBox, ValueBox, FontSizeBox, TextColorBox, BackgroundBox })
        {
            box.LostKeyboardFocus += (_, _) => CommitField(box);
            box.KeyDown += InspectorField_KeyDown;
        }

        editor.Changed += (_, _) => RefreshAll();
        editor.Changed += (_, _) => ScheduleRecoveryDraft();
        draftTimer.Tick += (_, _) => WriteRecoveryDraft();
        RefreshAll();

        try
        {
            recoverySession = recoveryStore.StartSession();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Recovery copies are off: {ex.Message}";
        }

        // Keep the default size within small screens so the window opens fully visible.
        var workArea = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(Width, workArea.Width));
        Height = Math.Max(MinHeight, Math.Min(Height, workArea.Height));

        Loaded += (_, _) =>
        {
            // A project path on the command line (for example from Explorer) is opened at
            // startup, unless the user chooses to recover unsaved work instead.
            var startupPath = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault();
            if (!OfferRecovery() && startupPath is not null)
            {
                OpenPath(startupPath);
            }
        };
    }

    private string ProjectDisplayName =>
        projectPath is not null ? Path.GetFileNameWithoutExtension(projectPath) : editor.Document.Name;

    private ControlDefinition? ArmedToolboxItem => ToolboxList.SelectedItem as ControlDefinition;

    /// <summary>The selected control when exactly one is selected; the inspector edits it.</summary>
    private Guid? selectedId => selection.Count == 1 ? selection[0] : null;

    private void RefreshAll()
    {
        if (editor.Screen.Id != shownScreenId)
        {
            // Another screen: nothing on it is selected, and rejected input typed for the
            // previous screen's fields no longer applies.
            shownScreenId = editor.Screen.Id;
            selection.Clear();
            SetFieldError(ScreenNameBox, null);
            SetFieldError(ScreenWidthBox, null);
            SetFieldError(ScreenHeightBox, null);
            SurfaceScroller.ScrollToHome();
        }

        RefreshScreenTabs();
        selection.RemoveAll(id => editor.FindControl(id) is null);
        Surface.Render(editor.Screen, selection, isPreview, PreviewButton_Clicked);
        RefreshInspector();
        Title = $"{ProjectDisplayName}{(editor.IsDirty ? " ●" : "")} — {AppTitle}";
        CommandManager.InvalidateRequerySuggested();
    }

    private void Select(Guid? id) => SetSelection(id is { } value ? [value] : []);

    /// <summary>Replaces the selection, in the order given (the last is the most recent).</summary>
    private void SetSelection(IEnumerable<Guid> ids)
    {
        selection = ids.Distinct().Where(id => editor.FindControl(id) is not null).ToList();
        Surface.SetSelection(selection);
        RefreshInspector();
        CommandManager.InvalidateRequerySuggested();

        StatusText.Text = selection.Count switch
        {
            0 => "Ready",
            1 when editor.FindControl(selection[0]) is { } control => $"Selected {control.Name} ({control.Type})",
            var n => $"{n} controls selected",
        };
    }

    private void Surface_ControlClicked(object? sender, ControlClickEventArgs e)
    {
        // With a toolbox item chosen, a click places it: into a container if one is clicked.
        if (ArmedToolboxItem is { } armed)
        {
            AddControl(armed.Type, e.Point.X, e.Point.Y);
            return;
        }

        if (e.Modifiers.HasFlag(ModifierKeys.Control))
        {
            // Ctrl+click adds or removes one control.
            SetSelection(selection.Contains(e.Id) ? selection.Where(id => id != e.Id) : selection.Append(e.Id));
        }
        else if (e.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            SetSelection(selection.Where(id => id != e.Id).Append(e.Id));
        }
        else if (!selection.Contains(e.Id))
        {
            Select(e.Id);
        }

        // A plain click on an already-selected control keeps the group so it can be dragged;
        // if the mouse is released without a drag, the click selects just that control.
    }

    private void Surface_ControlClickCompleted(object? sender, ControlClickEventArgs e)
    {
        if ((e.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0 && selection.Count > 1)
        {
            Select(e.Id);
        }
    }

    private void Surface_BandSelected(object? sender, BandSelectedEventArgs e)
    {
        var area = e.Area;
        var inside = editor.Screen.Controls
            .Where(c => c.X < area.Right && c.Right() > area.X && c.Y < area.Bottom && c.Bottom() > area.Y)
            .Select(c => c.Id);
        var additive = (e.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
        SetSelection(additive ? selection.Concat(inside) : inside);
    }

    private void MoveSelection(IReadOnlyCollection<Guid> ids, int dx, int dy)
    {
        if (editor.MoveControls(ids, dx, dy) && ids.Count == 1 && editor.FindControl(ids.First()) is { } control)
        {
            ShowBounds(control.Id, control.Bounds);
        }
        else if (ids.Count > 1)
        {
            StatusText.Text = $"Moved {ids.Count} controls";
        }
    }

    /// <summary>Adds a control at a point: into the innermost container there, or onto the screen.</summary>
    private void AddControl(ControlType type, double x, double y)
    {
        var container = ContainerLayout.ContainerAt(editor.Screen, x, y);
        var control = (container is not null ? editor.AddControlTo(type, container.Control.Id, x, y) : null)
            ?? editor.AddControl(type, x, y);
        ToolboxList.SelectedItem = null;
        Select(control.Id);
        StatusText.Text = container is null ? $"Added {control.Name}" : $"Added {control.Name} to {container.Control.Name}";
    }

    private void Surface_ReparentRequested(object? sender, ReparentEventArgs e)
    {
        var error = e.ContainerId is { } containerId
            ? editor.MoveIntoContainer(e.Id, containerId, e.Point.X, e.Point.Y)
            : editor.MoveToScreen(e.Id, e.Point.X, e.Point.Y);
        Select(e.Id);
        if (error is not null)
        {
            StatusText.Text = error;
        }
        else if (editor.FindControl(e.Id) is { } control)
        {
            StatusText.Text = editor.ParentOf(e.Id) is { } parent ? $"Moved {control.Name} into {parent.Name}" : $"Moved {control.Name} onto the screen";
        }
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

        // Nothing selected: the panel edits the screen instead. Several: it says so.
        NoSelectionText.Text = selection.Count > 1
            ? $"{selection.Count} controls selected. Drag, nudge with the arrow keys, copy or delete them together; select one to edit its properties."
            : "Nothing selected. Select a control to edit its properties.";
        ScreenPanel.Visibility = selection.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (control?.Id != inspectedId)
        {
            foreach (var box in fieldErrors.Keys.ToList())
            {
                SetFieldError(box, null);
            }
        }

        if (control?.Id != inspectedId)
        {
            anchorError = null;
            UpdateInspectorErrors();
        }

        SetField(ScreenNameBox, editor.Screen.Name);
        SetField(ScreenWidthBox, editor.Screen.Width.ToString(CultureInfo.CurrentCulture));
        SetField(ScreenHeightBox, editor.Screen.Height.ToString(CultureInfo.CurrentCulture));

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

        // Inside a container, the container decides placement: show only what still applies.
        var parent = editor.ParentOf(control.Id);
        var inStack = parent is not null && ControlCatalog.Get(parent.Type).IsStack;
        var verticalStack = inStack && parent!.Properties.Orientation != StackOrientation.Horizontal;
        XRow.Visibility = YRow.Visibility = AnchorRow.Visibility = Show(parent is null);
        WidthRow.Visibility = Show(parent is null || (inStack && !verticalStack));
        HeightRow.Visibility = Show(parent is null || verticalStack);
        CellRow.Visibility = SpanRow.Visibility = Show(parent?.Type == ControlType.Grid);
        OrderRow.Visibility = Show(inStack);
        if (inStack)
        {
            var index = parent!.Children!.FindIndex(c => c.Id == control.Id);
            EarlierButton.IsEnabled = index > 0;
            LaterButton.IsEnabled = index < parent.Children.Count - 1;
        }

        SetField(RowBox, (control.Row ?? 0).ToString(CultureInfo.CurrentCulture));
        SetField(ColumnBox, (control.Column ?? 0).ToString(CultureInfo.CurrentCulture));
        SetField(RowSpanBox, (control.RowSpan ?? 1).ToString(CultureInfo.CurrentCulture));
        SetField(ColumnSpanBox, (control.ColumnSpan ?? 1).ToString(CultureInfo.CurrentCulture));

        OrientationRow.Visibility = SpacingRow.Visibility = Show(definition.IsStack);
        GridSizeRow.Visibility = TrackSizesRow.Visibility = Show(definition.IsGrid);
        SetField(RowSizesBox, string.Join(", ", GridTrackSize.Resolve(properties.RowSizes, properties.Rows ?? 1)));
        SetField(ColumnSizesBox, string.Join(", ", GridTrackSize.Resolve(properties.ColumnSizes, properties.Columns ?? 1)));
        refreshingInspector = true;
        OrientationBox.SelectedIndex = properties.Orientation == StackOrientation.Horizontal ? 1 : 0;
        refreshingInspector = false;
        SetField(SpacingBox, (properties.Spacing ?? 0).ToString(CultureInfo.CurrentCulture));
        SetField(RowsBox, (properties.Rows ?? 1).ToString(CultureInfo.CurrentCulture));
        SetField(ColumnsBox, (properties.Columns ?? 1).ToString(CultureInfo.CurrentCulture));
        SetField(NameBox, control.Name);
        SetField(XBox, control.X.ToString(CultureInfo.CurrentCulture));
        SetField(YBox, control.Y.ToString(CultureInfo.CurrentCulture));
        SetField(WidthBox, control.Width.ToString(CultureInfo.CurrentCulture));
        SetField(HeightBox, control.Height.ToString(CultureInfo.CurrentCulture));

        TextRow.Visibility = definition.HasText ? Visibility.Visible : Visibility.Collapsed;
        SetField(TextValueBox, properties.Text ?? "");
        AnchorLeftBox.IsChecked = control.Anchor.HasFlag(AnchorEdges.Left);
        AnchorTopBox.IsChecked = control.Anchor.HasFlag(AnchorEdges.Top);
        AnchorRightBox.IsChecked = control.Anchor.HasFlag(AnchorEdges.Right);
        AnchorBottomBox.IsChecked = control.Anchor.HasFlag(AnchorEdges.Bottom);

        ActionRow.Visibility = Show(definition.HasAction);
        if (definition.HasAction)
        {
            // Nothing, close this screen, or open any other screen, by name.
            var actions = new List<ButtonAction> { new("Nothing else", null, false), new("Close this screen", null, true) };
            actions.AddRange(editor.Document.Screens.Where(s => s.Id != editor.Screen.Id).Select(s => new ButtonAction($"Open {s.Name}", s.Id, false)));
            if (properties.OpensScreen is { } target && actions.All(a => a.OpensScreen != target))
            {
                actions.Add(new ButtonAction("Open this screen again", target, false));
            }

            refreshingInspector = true;
            ActionBox.ItemsSource = actions;
            ActionBox.SelectedIndex = actions.FindIndex(a => a.OpensScreen == properties.OpensScreen && a.ClosesScreen == (properties.ClosesScreen == true));
            refreshingInspector = false;
        }

        MultilineRow.Visibility = Show(definition.HasMultiline);
        MultilineBox.IsChecked = properties.IsMultiline == true;
        RangeRow.Visibility = Show(definition.HasRange);
        SetField(MinimumBox, (properties.Minimum ?? 0).ToString(CultureInfo.CurrentCulture));
        SetField(MaximumBox, (properties.Maximum ?? 0).ToString(CultureInfo.CurrentCulture));
        SetField(ValueBox, (properties.Value ?? 0).ToString(CultureInfo.CurrentCulture));

        ImageRow.Visibility = Show(definition.HasImage);
        RemoveImageButton.IsEnabled = properties.ImageData is not null;
        refreshingInspector = true;
        StretchBox.SelectedIndex = properties.Stretch == ImageStretch.Fill ? 1 : 0;
        refreshingInspector = false;
        ColorRow.Visibility = Show(definition.HasBackground);

        FontRow.Visibility = Show(definition.HasFont);
        SetField(FontSizeBox, properties.FontSize?.ToString(CultureInfo.CurrentCulture) ?? "");
        BoldBox.IsChecked = properties.IsBold == true;
        TextColorLabel.Visibility = TextColorBox.Visibility = Show(definition.HasFont);
        SetField(TextColorBox, properties.Foreground ?? "");
        SetField(BackgroundBox, properties.Background ?? "");

        IsCheckedRow.Visibility = definition.HasIsChecked ? Visibility.Visible : Visibility.Collapsed;
        IsCheckedBox.IsChecked = properties.IsChecked == true;
        ItemsRow.Visibility = definition.HasItems ? Visibility.Visible : Visibility.Collapsed;
        SetField(ItemsBox, string.Join(Environment.NewLine, properties.Items ?? []));
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private void EarlierButton_Click(object sender, RoutedEventArgs e)
    {
        if (inspectedId is { } id)
        {
            editor.MoveWithinContainer(id, -1);
        }
    }

    private void LaterButton_Click(object sender, RoutedEventArgs e)
    {
        if (inspectedId is { } id)
        {
            editor.MoveWithinContainer(id, 1);
        }
    }

    private void OrientationBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!refreshingInspector && inspectedId is { } id && OrientationBox.SelectedIndex >= 0)
        {
            editor.SetOrientation(id, OrientationBox.SelectedIndex == 1 ? StackOrientation.Horizontal : StackOrientation.Vertical);
        }
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
        if (box == ScreenWidthBox || box == ScreenHeightBox)
        {
            CommitScreenSize(box);
            return;
        }

        if (box == ScreenNameBox)
        {
            var renameError = editor.RenameScreen(box.Text.Trim());
            SetFieldError(box, renameError);
            if (renameError is null)
            {
                RefreshInspector();
            }

            return;
        }

        if (inspectedId is not { } id || editor.FindControl(id) is not { } control)
        {
            return;
        }

        var error =
            box == NameBox ? editor.Rename(id, box.Text)
            : box == TextValueBox ? editor.SetText(id, box.Text)
            : box == ItemsBox ? editor.SetItems(id, box.Text.Split('\n').Select(line => line.TrimEnd('\r')))
            : box == RowBox || box == ColumnBox ? CommitWholeNumbers(values => editor.SetGridCell(id, values[0], values[1]), RowBox, ColumnBox)
            : box == RowSpanBox || box == ColumnSpanBox ? CommitWholeNumbers(values => editor.SetGridSpan(id, values[0], values[1]), RowSpanBox, ColumnSpanBox)
            : box == RowSizesBox || box == ColumnSizesBox ? editor.SetGridTrackSizes(id, SplitSizes(RowSizesBox), SplitSizes(ColumnSizesBox))
            : box == MinimumBox || box == MaximumBox || box == ValueBox
                ? CommitWholeNumbers(values => editor.SetRange(id, values[0], values[1], values[2]), MinimumBox, MaximumBox, ValueBox)
            : box == FontSizeBox ? CommitFont(id)
            : box == TextColorBox || box == BackgroundBox ? editor.SetColors(id, TextColorBox.Text, BackgroundBox.Text)
            : box == SpacingBox ? CommitWholeNumbers(values => editor.SetSpacing(id, values[0]), SpacingBox)
            : box == RowsBox || box == ColumnsBox ? CommitWholeNumbers(values => editor.SetGridSize(id, values[0], values[1]), RowsBox, ColumnsBox)
            : CommitBoundsField(control, box);

        SetFieldError(box, error);
        if (error is null)
        {
            // Fields applied together were all accepted, including any flagged earlier.
            foreach (var partner in FieldGroup(box))
            {
                SetFieldError(partner, null);
            }

            // Show the value as stored, for example trimmed or with blank items removed.
            RefreshInspector();
        }
    }

    private void CommitScreenSize(TextBox box)
    {
        var screen = editor.Screen;
        string? error;
        if (!int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var value))
        {
            error = "Enter a whole number of DIPs.";
        }
        else
        {
            error = box == ScreenWidthBox ? editor.SetScreenSize(value, screen.Height) : editor.SetScreenSize(screen.Width, value);
        }

        SetFieldError(box, error);
        if (error is null)
        {
            RefreshInspector();
        }
    }

    /// <summary>The fields applied together with a field, which share its outcome.</summary>
    private TextBox[] FieldGroup(TextBox box) =>
        new[]
        {
            new[] { RowBox, ColumnBox },
            [RowSpanBox, ColumnSpanBox],
            [RowsBox, ColumnsBox],
            [RowSizesBox, ColumnSizesBox],
            [MinimumBox, MaximumBox, ValueBox],
            [TextColorBox, BackgroundBox],
        }.FirstOrDefault(group => group.Contains(box)) ?? [box];

    private static string[] SplitSizes(TextBox box) => box.Text.Split(',');

    /// <summary>Parses whole numbers from fields and applies them together.</summary>
    private static string? CommitWholeNumbers(Func<int[], string?> apply, params TextBox[] boxes)
    {
        var values = new int[boxes.Length];
        for (var i = 0; i < boxes.Length; i++)
        {
            if (!int.TryParse(boxes[i].Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out values[i]))
            {
                return "Enter a whole number.";
            }
        }

        return apply(values);
    }

    private string? CommitBoundsField(ControlDocument control, TextBox box)
    {
        if (!int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var value))
        {
            return "Enter a whole number of DIPs.";
        }

        // Inside a StackPanel only the size along the stack can change.
        if (editor.ParentOf(control.Id) is not null)
        {
            return editor.SetStackSize(control.Id, value);
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

        UpdateInspectorErrors();
    }

    private void UpdateInspectorErrors()
    {
        // Screen fields show their errors in the Screen section; control fields in the inspector.
        var screenMessages = fieldErrors.Where(f => IsScreenField(f.Key)).Select(f => f.Value).ToList();
        ScreenErrorText.Text = string.Join(Environment.NewLine, screenMessages);
        ScreenErrorText.Visibility = screenMessages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var messages = fieldErrors.Where(f => !IsScreenField(f.Key)).Select(f => f.Value)
            .Append(anchorError).OfType<string>().ToList();
        InspectorErrorText.Text = string.Join(Environment.NewLine, messages);
        InspectorErrorText.Visibility = messages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool IsScreenField(TextBox box) => box == ScreenNameBox || box == ScreenWidthBox || box == ScreenHeightBox;

    private void AnchorBox_Click(object sender, RoutedEventArgs e)
    {
        if (inspectedId is not { } id)
        {
            return;
        }

        var anchor = AnchorEdges.None;
        if (AnchorLeftBox.IsChecked == true)
        {
            anchor |= AnchorEdges.Left;
        }

        if (AnchorTopBox.IsChecked == true)
        {
            anchor |= AnchorEdges.Top;
        }

        if (AnchorRightBox.IsChecked == true)
        {
            anchor |= AnchorEdges.Right;
        }

        if (AnchorBottomBox.IsChecked == true)
        {
            anchor |= AnchorEdges.Bottom;
        }

        // A rejected combination is shown and the boxes go back to the stored anchor.
        anchorError = editor.SetAnchor(id, anchor);
        RefreshInspector();
        UpdateInspectorErrors();
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

    private void ActionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!refreshingInspector && inspectedId is { } id && ActionBox.SelectedItem is ButtonAction action)
        {
            anchorError = editor.SetButtonAction(id, action.OpensScreen, action.ClosesScreen);
            UpdateInspectorErrors();
        }
    }

    /// <summary>A choice in the Properties panel's "On click" list.</summary>
    private sealed record ButtonAction(string Label, string? OpensScreen, bool ClosesScreen);

    /// <summary>Applies the text size (blank for the standard size) and the Bold setting together.</summary>
    private string? CommitFont(Guid id)
    {
        int? size = null;
        if (FontSizeBox.Text.Trim().Length > 0)
        {
            if (!int.TryParse(FontSizeBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var value))
            {
                return "Enter a whole number for the text size, or leave it blank.";
            }

            size = value;
        }

        return editor.SetFont(id, size, BoldBox.IsChecked == true);
    }

    private void ChooseImageButton_Click(object sender, RoutedEventArgs e)
    {
        if (inspectedId is not { } id)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose a picture", Filter = ImageFile.OpenFilter };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            anchorError = editor.SetImage(id, File.ReadAllBytes(dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            anchorError = $"Could not read \"{dialog.FileName}\": {ex.Message}";
        }

        UpdateInspectorErrors();
        if (anchorError is null)
        {
            StatusText.Text = $"Picture set from {Path.GetFileName(dialog.FileName)}";
        }
    }

    private void RemoveImageButton_Click(object sender, RoutedEventArgs e)
    {
        if (inspectedId is { } id)
        {
            editor.SetImage(id, null);
        }
    }

    private void StretchBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!refreshingInspector && inspectedId is { } id && StretchBox.SelectedIndex >= 0)
        {
            editor.SetImageStretch(id, StretchBox.SelectedIndex == 1 ? ImageStretch.Fill : ImageStretch.Uniform);
        }
    }

    private void BoldBox_Click(object sender, RoutedEventArgs e)
    {
        if (inspectedId is { } id)
        {
            SetFieldError(FontSizeBox, CommitFont(id));
        }
    }

    private void MultilineBox_Click(object sender, RoutedEventArgs e)
    {
        if (inspectedId is { } id)
        {
            editor.SetIsMultiline(id, MultilineBox.IsChecked == true);
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
            var offset = 20 + editor.Screen.Controls.Count % 10 * 20;
            AddControl(armed.Type, offset, offset);
            e.Handled = true;
        }
    }

    private void ToolboxList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        toolboxDragStart = e.GetPosition(ToolboxList);
    }

    private void ToolboxList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // A click, not a drag: forget the press so a later move cannot start a drag.
        toolboxDragStart = null;
    }

    private void ToolboxList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (toolboxDragStart is not { } start)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            toolboxDragStart = null;
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

    /// <summary>Applies a value still being typed in the Properties panel before a command runs.</summary>
    private void CommitFocusedField()
    {
        if (Keyboard.FocusedElement is TextBox box && (InspectorPanel.IsAncestorOf(box) || ScreenPanel.IsAncestorOf(box)))
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
        selection.Clear();
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
        selection.Clear();
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

    private void ExportWpf_Executed(object sender, ExecutedRoutedEventArgs e) =>
        Export("WPF", WpfGenerator.Check, WpfExporter.Export);

    private void ExportWinForms_Executed(object sender, ExecutedRoutedEventArgs e) =>
        Export("WinForms", WinFormsGenerator.Check, WinFormsExporter.Export);

    private void ExportBlazor_Executed(object sender, ExecutedRoutedEventArgs e) =>
        Export("Blazor", BlazorGenerator.Check, BlazorExporter.Export);

    private void Export(
        string target,
        Func<ProjectDocument, IReadOnlyList<string>> check,
        Func<ProjectDocument, string, ExportResult> export)
    {
        CommitFocusedField();

        // The generated code and namespace are named after the project as the user sees it.
        var document = editor.Document with { Name = ProjectDisplayName };
        if (check(document) is { Count: > 0 } problems)
        {
            ShowError($"The design cannot be exported to {target} yet.", string.Join(Environment.NewLine, problems));
            return;
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = $"Choose the folder to put the {target} project in",
            InitialDirectory = lastExportFolder ?? (projectPath is not null ? Path.GetDirectoryName(projectPath) : null) ?? "",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        ExportResult result;
        try
        {
            result = export(document, dialog.FolderName);
        }
        catch (ExportException ex)
        {
            ShowError($"Could not export to {target}.", ex.Message);
            return;
        }

        lastExportFolder = dialog.FolderName;
        StatusText.Text = $"Exported {target} project to {result.ProjectFolder}";

        var summary = new List<string> { $"Exported the {target} project to:{Environment.NewLine}{result.ProjectFolder}" };
        if (result.Created.Count > 0)
        {
            summary.Add("Created: " + string.Join(", ", result.Created));
        }

        if (result.Updated.Count > 0)
        {
            summary.Add("Updated: " + string.Join(", ", result.Updated));
        }

        if (result.Kept.Count > 0)
        {
            summary.Add("Left unchanged: " + string.Join(", ", result.Kept));
        }

        summary.Add("Open the folder?");
        var answer = MessageBox.Show(this, string.Join(Environment.NewLine + Environment.NewLine, summary), AppTitle,
            MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (answer == MessageBoxResult.Yes)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{result.ProjectFolder}\"") { UseShellExecute = true });
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

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        // A normal close: the user has saved or chosen to discard, so no draft is needed.
        draftTimer.Stop();
        recoverySession?.DeleteDraft();
        recoverySession?.Dispose();
    }

    private void HasSelection_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = !isPreview && selection.Count > 0;

    private void Delete_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var names = selection.Select(id => editor.FindControl(id)?.Name).OfType<string>().ToList();
        if (editor.DeleteControls(selection) > 0)
        {
            Select(null);
            StatusText.Text = names.Count == 1 ? $"Deleted {names[0]}" : $"Deleted {names.Count} controls";
        }
    }

    private void Copy_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        // Copies keep the design's draw order, whatever order they were selected in. A control
        // copied from inside a container becomes a free-standing copy at its screen position;
        // one inside another selected control is already copied with it.
        var screen = editor.Screen;
        clipboard = ContainerLayout.Flatten(screen)
            .Where(p => selection.Contains(p.Control.Id))
            .Where(p => !selection.Any(other => other != p.Control.Id && ControlTree.IsSelfOrDescendant(screen.Controls, other, p.Control.Id)))
            .Select(p => p.Control.WithBounds(p.Bounds) with { Row = null, Column = null })
            .ToList();
        pasteCount = 0;
        StatusText.Text = clipboard.Count == 1 ? $"Copied {clipboard[0].Name}" : $"Copied {clipboard.Count} controls";
        CommandManager.InvalidateRequerySuggested();
    }

    private void Cut_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        Copy_Executed(sender, e);
        editor.DeleteControls(selection);
        Select(null);
        StatusText.Text = clipboard.Count == 1 ? $"Cut {clipboard[0].Name}" : $"Cut {clipboard.Count} controls";
    }

    private void Paste_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = !isPreview && clipboard.Count > 0;

    private void Paste_Executed(object sender, ExecutedRoutedEventArgs e) => Paste();

    private void Duplicate_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        Copy_Executed(sender, e);
        Paste();
    }

    /// <summary>Each paste of the same copy lands one grid step further down and right.</summary>
    private void Paste()
    {
        pasteCount++;
        var pasted = editor.PasteControls(clipboard, pasteCount * Math.Max(1, editor.Screen.GridSize));
        SetSelection(pasted.Select(c => c.Id));
        StatusText.Text = pasted.Count == 1 ? $"Pasted {pasted[0].Name}" : $"Pasted {pasted.Count} controls";
    }

    private void SelectAll_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = !isPreview && editor.Screen.Controls.Count > 0;

    private void SelectAll_Executed(object sender, ExecutedRoutedEventArgs e) =>
        SetSelection(editor.Screen.Controls.Select(c => c.Id));

    private void BringToFront_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (editor.BringToFront(selection))
        {
            StatusText.Text = "Brought to front";
        }
    }

    private void SendToBack_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (editor.SendToBack(selection))
        {
            StatusText.Text = "Sent to back";
        }
    }

    /// <summary>Keeps the screen tabs in step with the document and the screen being shown.</summary>
    private void RefreshScreenTabs()
    {
        var tabs = editor.Document.Screens
            .Select((screen, index) => new ScreenTab(screen.Id, screen.Name, index == 0
                ? $"{screen.Name}: the main screen, which an exported app opens with"
                : screen.Name))
            .ToList();
        refreshingScreenTabs = true;
        if (ScreenTabs.ItemsSource is not List<ScreenTab> shown || !shown.SequenceEqual(tabs))
        {
            ScreenTabs.ItemsSource = tabs;
        }

        ScreenTabs.SelectedIndex = tabs.FindIndex(t => t.Id == editor.Screen.Id);
        refreshingScreenTabs = false;
    }

    private void ScreenTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!refreshingScreenTabs && ScreenTabs.SelectedItem is ScreenTab tab)
        {
            ShowScreen(tab.Id);
        }
    }

    private void ShowScreen(string id)
    {
        // A value typed for the current screen applies to it before another screen shows.
        CommitFocusedField();
        if (editor.SelectScreen(id))
        {
            StatusText.Text = $"Screen {editor.Screen.Name}";
        }
    }

    /// <summary>
    /// Ctrl+PageUp and Ctrl+PageDown switch screens from anywhere. They are handled before the
    /// design surface's scroll viewer, which would otherwise take them as page scrolling.
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.PageUp or Key.PageDown)
        {
            var command = e.Key == Key.PageUp ? EditorCommands.PreviousScreen : EditorCommands.NextScreen;
            if (command.CanExecute(null, this))
            {
                command.Execute(null, this);
            }

            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private int ScreenIndex => editor.Document.Screens.FindIndex(s => s.Id == editor.Screen.Id);

    private void Designing_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !isPreview;

    private void AddScreen_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        CommitFocusedField();
        var screen = editor.AddScreen();
        StatusText.Text = $"Added screen {screen.Name}. Rename it in the Properties panel.";
    }

    private void DuplicateScreen_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        CommitFocusedField();
        var screen = editor.DuplicateScreen();
        StatusText.Text = $"Added screen {screen.Name}, a copy.";
    }

    private void DeleteScreen_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = !isPreview && editor.Document.Screens.Count > 1;

    private void DeleteScreen_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var name = editor.Screen.Name;
        if (editor.DeleteScreen() is { } error)
        {
            StatusText.Text = error;
            return;
        }

        StatusText.Text = $"Deleted screen {name}. Undo brings it back.";
    }

    private void MoveScreenEarlier_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !isPreview && ScreenIndex > 0;

    private void MoveScreenEarlier_Executed(object sender, ExecutedRoutedEventArgs e) => MoveScreen(-1);

    private void MoveScreenLater_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = !isPreview && ScreenIndex < editor.Document.Screens.Count - 1;

    private void MoveScreenLater_Executed(object sender, ExecutedRoutedEventArgs e) => MoveScreen(1);

    private void MoveScreen(int delta)
    {
        if (editor.MoveScreen(delta))
        {
            StatusText.Text = ScreenIndex == 0
                ? $"{editor.Screen.Name} is now the main screen, which an exported app opens with."
                : $"Moved screen {editor.Screen.Name}";
        }
    }

    // Switching screens works in Preview too, to try each one.
    private void PreviousScreen_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = ScreenIndex > 0;

    private void PreviousScreen_Executed(object sender, ExecutedRoutedEventArgs e) => ShowScreen(editor.Document.Screens[ScreenIndex - 1].Id);

    private void NextScreen_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = ScreenIndex < editor.Document.Screens.Count - 1;

    private void NextScreen_Executed(object sender, ExecutedRoutedEventArgs e) => ShowScreen(editor.Document.Screens[ScreenIndex + 1].Id);

    private sealed record ScreenTab(string Id, string Name, string ToolTip);

    private void Undo_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !isPreview && editor.CanUndo;

    private void Undo_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        editor.Undo();
        StatusText.Text = "Undo";
    }

    private void Redo_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !isPreview && editor.CanRedo;

    private void Redo_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        editor.Redo();
        StatusText.Text = "Redo";
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void DesignMenuItem_Click(object sender, RoutedEventArgs e) => DesignModeButton.IsChecked = true;

    private void PreviewMenuItem_Click(object sender, RoutedEventArgs e) => PreviewModeButton.IsChecked = true;

    private void DesignModeButton_Checked(object sender, RoutedEventArgs e) => SetMode(isPreview: false);

    private void PreviewModeButton_Checked(object sender, RoutedEventArgs e) => SetMode(isPreview: true);

    private void SetMode(bool isPreview)
    {
        // Checked handlers can fire during InitializeComponent, before later named elements exist.
        if (DesignMenuItem is null || PreviewMenuItem is null || StatusText is null)
        {
            return;
        }

        CommitFocusedField();
        this.isPreview = isPreview;
        previewTrail.Clear();
        DesignMenuItem.IsChecked = !isPreview;
        PreviewMenuItem.IsChecked = isPreview;
        ToolboxList.IsEnabled = !isPreview;
        InspectorPanel.IsEnabled = !isPreview;

        // Rendering from the document again is what discards anything typed or toggled in Preview.
        RefreshAll();
        StatusText.Text = isPreview
            ? "Preview: try the controls. Nothing you do here changes the design."
            : "Design mode";
    }

    /// <summary>
    /// A button clicked in Preview: it opens the screen it is set to open, or goes back from a
    /// screen it closes, the way the exported application would.
    /// </summary>
    private void PreviewButton_Clicked(ControlDocument button) =>
        // After the click finishes: switching screens rebuilds the Preview, button included.
        Dispatcher.BeginInvoke(() => FollowPreviewButton(button));

    private void FollowPreviewButton(ControlDocument button)
    {
        var properties = button.Properties;
        if (properties.OpensScreen is { } target && editor.Document.FindScreen(target) is { } screen)
        {
            previewTrail.Push(editor.Screen.Id);
            editor.SelectScreen(screen.Id);
            StatusText.Text = $"{button.Name} clicked: opened {screen.Name}";
        }
        else if (properties.ClosesScreen == true && previewTrail.TryPop(out var previous))
        {
            var closed = editor.Screen.Name;
            editor.SelectScreen(previous);
            StatusText.Text = $"{button.Name} clicked: closed {closed}";
        }
        else if (properties.ClosesScreen == true)
        {
            StatusText.Text = $"{button.Name} clicked: this would close the {editor.Screen.Name} window";
        }
        else
        {
            StatusText.Text = $"{button.Name} clicked";
        }
    }

    private void ScheduleRecoveryDraft()
    {
        draftTimer.Stop();
        if (editor.IsDirty)
        {
            // Written after a short pause in editing, not on every change.
            draftTimer.Start();
        }
        else
        {
            // Saved, discarded or undone back to the saved state: the draft is stale.
            recoverySession?.DeleteDraft();
        }
    }

    private void WriteRecoveryDraft()
    {
        draftTimer.Stop();
        if (recoverySession is null || !editor.IsDirty)
        {
            return;
        }

        try
        {
            recoverySession.WriteDraft(editor.Document, projectPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Could not write a recovery copy to \"{recoverySession.DraftPath}\": {ex.Message}";
        }
    }

    /// <summary>
    /// Offers unsaved work left by a session that did not close normally. Returns true if the
    /// user recovered it. Declining deletes the draft and never touches the saved project file.
    /// </summary>
    private bool OfferRecovery()
    {
        IReadOnlyList<RecoveryDraft> drafts;
        try
        {
            drafts = recoveryStore.FindOrphanedDrafts();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Could not check for recovery copies: {ex.Message}";
            return false;
        }

        if (drafts.Count == 0)
        {
            return false;
        }

        // Offer the newest; any older drafts are offered on a later start.
        var draft = drafts[0];
        var name = draft.ProjectPath is not null ? Path.GetFileNameWithoutExtension(draft.ProjectPath) : draft.Document.Name;
        var keep = draft.ProjectPath is not null ? "keep the last saved version" : "discard these changes";
        var answer = MessageBox.Show(this,
            $"{AppTitle} did not close normally. Unsaved changes to \"{name}\" from {draft.SavedAt.LocalDateTime:g} can be recovered."
                + $"{Environment.NewLine}{Environment.NewLine}Recover them? Choose No to {keep}.",
            AppTitle, MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            RecoveryStore.Discard(draft.DraftPath);
            return false;
        }

        projectPath = draft.ProjectPath;
        selection.Clear();
        editor.Reset(draft.Document, isDirty: true);

        // Take over the draft in this session before removing the old one.
        WriteRecoveryDraft();
        RecoveryStore.Discard(draft.DraftPath);
        StatusText.Text = "Recovered unsaved changes. Save to keep them.";
        return true;
    }
}
