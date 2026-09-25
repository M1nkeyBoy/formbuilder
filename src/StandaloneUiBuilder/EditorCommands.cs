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

    public static RoutedUICommand ExportWinUI { get; } = new(
        "Export to Win_UI 3…",
        nameof(ExportWinUI),
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
