using System.Windows;
using System.Windows.Input;
using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder;

/// <summary>
/// Edits a screen's view model code: C# members that every export writes into the view model,
/// such as the On… methods its commands and bindings call.
/// </summary>
public partial class CodeWindow : Window
{
    private readonly ScreenDocument screen;

    private CodeWindow(ScreenDocument screen, string className)
    {
        InitializeComponent();
        this.screen = screen;
        Title = $"Code — {className}";
        IntroText.Text = $"Members of {className}, the view model of screen \"{screen.Name}\". Every export writes them into the class, "
            + "so they can use its properties and implement its hooks. Using directives may come first.";
        CodeBox.Text = screen.Code?.Replace("\n", Environment.NewLine, StringComparison.Ordinal) ?? "";

        var properties = DataBindings.Properties(screen);
        var commands = DataBindings.Commands(screen);
        MembersText.Text = properties.Count + commands.Count == 0
            ? "The screen has no bindings or commands yet; bind controls in the Properties panel to give the view model properties."
            : "Properties: " + (properties.Count == 0 ? "none" : string.Join(", ", properties.Select(p => $"{p.Name} ({Describe(p.Kind)})")))
                + ". Commands: " + (commands.Count == 0 ? "none" : string.Join(", ", commands.Select(c => $"{c.Name}()"))) + ".";
        RefreshHooks();
        CodeBox.TextChanged += (_, _) => RefreshHooks();
        Loaded += (_, _) => CodeBox.Focus();
    }

    /// <summary>Shows the window; returns the edited code, or null if the user cancelled.</summary>
    public static string? Edit(Window owner, ScreenDocument screen, string className)
    {
        var window = new CodeWindow(screen, className) { Owner = owner };
        return window.ShowDialog() == true ? window.CodeBox.Text : null;
    }

    private static string Describe(BindingKind kind) => kind switch
    {
        BindingKind.Text => "text",
        BindingKind.Flag => "on or off",
        BindingKind.Number => "number",
        BindingKind.Choice => "chosen item",
        _ => "date",
    };

    private sealed record HookItem(ViewModelHook Hook, string Title, string Description);

    private void RefreshHooks()
    {
        var selected = (HooksList.SelectedItem as HookItem)?.Hook.Name;
        HooksList.ItemsSource = DataBindings.Hooks(screen)
            .Select(h => new HookItem(h, (DataBindings.Implements(CodeBox.Text, h) ? "✓ " : "") + h.Name + "()", h.Description))
            .ToList();
        HooksList.SelectedItem = HooksList.Items.Cast<HookItem>().FirstOrDefault(i => i.Hook.Name == selected);
    }

    private void AddHook()
    {
        if (HooksList.SelectedItem is not HookItem item)
        {
            return;
        }

        if (!DataBindings.Implements(CodeBox.Text, item.Hook))
        {
            var text = CodeBox.Text.TrimEnd();
            var stub = item.Hook.Stub.Replace("\n", Environment.NewLine, StringComparison.Ordinal);
            CodeBox.Text = text.Length == 0 ? stub : text + Environment.NewLine + Environment.NewLine + stub;

            // The caret goes inside the new method's braces.
            CodeBox.CaretIndex = CodeBox.Text.TrimEnd().Length - 1;
        }

        RefreshHooks();
        CodeBox.Focus();
    }

    private void HooksList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => AddHook();

    private void AddHook_Click(object sender, RoutedEventArgs e) => AddHook();

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    /// <summary>Tab indents with four spaces; Ctrl+Enter applies.</summary>
    private void CodeBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None)
        {
            var caret = CodeBox.CaretIndex;
            CodeBox.SelectedText = "";
            CodeBox.Text = CodeBox.Text.Insert(caret, "    ");
            CodeBox.CaretIndex = caret + 4;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            DialogResult = true;
            e.Handled = true;
        }
    }
}
