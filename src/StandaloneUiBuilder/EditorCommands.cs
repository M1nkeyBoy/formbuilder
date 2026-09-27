using System.Windows.Input;

namespace StandaloneUiBuilder;

/// <summary>Editor commands that WPF's built-in ApplicationCommands do not cover.</summary>
public static class EditorCommands
{
    public static RoutedUICommand ExportWpf { get; } = new(
        "Export to _WPF…",
        nameof(ExportWpf),
        typeof(EditorCommands),
        [new KeyGesture(Key.E, ModifierKeys.Control)]);

    public static RoutedUICommand ExportWinForms { get; } = new(
        "Export to Win_Forms…",
        nameof(ExportWinForms),
        typeof(EditorCommands),
        [new KeyGesture(Key.E, ModifierKeys.Control | ModifierKeys.Shift)]);

    /// <summary>Opens File Explorer at the saved project file, selected.</summary>
    public static RoutedUICommand ShowInExplorer { get; } = new(
        "Show in _Explorer",
        nameof(ShowInExplorer),
        typeof(EditorCommands));

    public static RoutedUICommand ImportWpf { get; } = new(
        "_Import from WPF…",
        nameof(ImportWpf),
        typeof(EditorCommands));

    public static RoutedUICommand ExportWinUI { get; } = new(
        "Export to Win_UI 3…",
        nameof(ExportWinUI),
        typeof(EditorCommands));

    public static RoutedUICommand ExportMaui { get; } = new(
        "Export to ._NET MAUI…",
        nameof(ExportMaui),
        typeof(EditorCommands));

    public static RoutedUICommand ExportBlazor { get; } = new(
        "Export to _Blazor…",
        nameof(ExportBlazor),
        typeof(EditorCommands));

    public static RoutedUICommand Duplicate { get; } = new(
        "D_uplicate",
        nameof(Duplicate),
        typeof(EditorCommands),
        [new KeyGesture(Key.D, ModifierKeys.Control)]);

    public static RoutedUICommand BringToFront { get; } = new(
        "Bring to _Front",
        nameof(BringToFront),
        typeof(EditorCommands),
        [new KeyGesture(Key.OemCloseBrackets, ModifierKeys.Control)]);

    public static RoutedUICommand SendToBack { get; } = new(
        "Send to _Back",
        nameof(SendToBack),
        typeof(EditorCommands),
        [new KeyGesture(Key.OemOpenBrackets, ModifierKeys.Control)]);

    /// <summary>Turns on (or off) setting the tab order by clicking controls in turn.</summary>
    public static RoutedUICommand SetTabOrder { get; } = new(
        "Set _Tab Order",
        nameof(SetTabOrder),
        typeof(EditorCommands),
        [new KeyGesture(Key.T, ModifierKeys.Control)]);

    public static RoutedUICommand TabOrderByPosition { get; } = new(
        "Tab Order by _Position",
        nameof(TabOrderByPosition),
        typeof(EditorCommands));

    public static RoutedUICommand ZoomIn { get; } = new(
        "Zoom _In",
        nameof(ZoomIn),
        typeof(EditorCommands),
        [new KeyGesture(Key.OemPlus, ModifierKeys.Control), new KeyGesture(Key.Add, ModifierKeys.Control)]);

    public static RoutedUICommand ZoomOut { get; } = new(
        "Zoom _Out",
        nameof(ZoomOut),
        typeof(EditorCommands),
        [new KeyGesture(Key.OemMinus, ModifierKeys.Control), new KeyGesture(Key.Subtract, ModifierKeys.Control)]);

    public static RoutedUICommand ActualSize { get; } = new(
        "_Actual Size",
        nameof(ActualSize),
        typeof(EditorCommands),
        [new KeyGesture(Key.D0, ModifierKeys.Control), new KeyGesture(Key.NumPad0, ModifierKeys.Control)]);

    public static RoutedUICommand FitToWindow { get; } = new(
        "_Fit to Canvas",
        nameof(FitToWindow),
        typeof(EditorCommands));

    /// <summary>Opens the current screen's view model code.</summary>
    public static RoutedUICommand EditCode { get; } = new(
        "_Code…",
        nameof(EditCode),
        typeof(EditorCommands),
        [new KeyGesture(Key.F7)]);

    /// <summary>Project > Libraries: the project's control libraries.</summary>
    public static RoutedUICommand Libraries { get; } = new(
        "_Libraries…",
        nameof(Libraries),
        typeof(EditorCommands),
        [new KeyGesture(Key.L, ModifierKeys.Control | ModifierKeys.Shift)]);

    /// <summary>Changes the platform the project is for; the parameter is a <see cref="Core.ProjectPlatform"/> name.</summary>
    public static RoutedUICommand SetPlatform { get; } = new(
        "Platform",
        nameof(SetPlatform),
        typeof(EditorCommands));

    /// <summary>Chooses the project's theme; the parameter is Light, Dark or System.</summary>
    public static RoutedUICommand SetTheme { get; } = new(
        "Theme",
        nameof(SetTheme),
        typeof(EditorCommands));

    /// <summary>Chooses the project's style; the parameter is Modern or Classic.</summary>
    public static RoutedUICommand SetStyle { get; } = new(
        "Style",
        nameof(SetStyle),
        typeof(EditorCommands));

    public static RoutedUICommand ResetTabOrder { get; } = new(
        "R_eset Tab Order",
        nameof(ResetTabOrder),
        typeof(EditorCommands));

    /// <summary>
    /// Lines up, sizes or spaces the selected controls; the parameter says how (Lefts,
    /// Centers, Rights, Tops, Middles, Bottoms, Width, Height, Both, Horizontally, Vertically).
    /// </summary>
    public static RoutedUICommand Arrange { get; } = new(
        "Arrange",
        nameof(Arrange),
        typeof(EditorCommands));

    public static RoutedUICommand AddScreen { get; } = new(
        "_Add Screen",
        nameof(AddScreen),
        typeof(EditorCommands),
        [new KeyGesture(Key.N, ModifierKeys.Control | ModifierKeys.Shift)]);

    public static RoutedUICommand DuplicateScreen { get; } = new(
        "D_uplicate Screen",
        nameof(DuplicateScreen),
        typeof(EditorCommands));

    public static RoutedUICommand DeleteScreen { get; } = new(
        "_Delete Screen",
        nameof(DeleteScreen),
        typeof(EditorCommands));

    public static RoutedUICommand MoveScreenEarlier { get; } = new(
        "Move _Earlier",
        nameof(MoveScreenEarlier),
        typeof(EditorCommands));

    public static RoutedUICommand MoveScreenLater { get; } = new(
        "Move _Later",
        nameof(MoveScreenLater),
        typeof(EditorCommands));

    public static RoutedUICommand PreviousScreen { get; } = new(
        "_Previous Screen",
        nameof(PreviousScreen),
        typeof(EditorCommands),
        [new KeyGesture(Key.PageUp, ModifierKeys.Control)]);

    public static RoutedUICommand NextScreen { get; } = new(
        "_Next Screen",
        nameof(NextScreen),
        typeof(EditorCommands),
        [new KeyGesture(Key.PageDown, ModifierKeys.Control)]);
}
