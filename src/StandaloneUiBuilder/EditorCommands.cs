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
}
